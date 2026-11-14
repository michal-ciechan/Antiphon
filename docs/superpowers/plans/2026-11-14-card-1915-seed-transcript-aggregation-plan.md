# CARD-1915 seed transcript aggregation plan

Plan owner: ee0bb3d9. Source/master at investigation start:
`cf05ed8a048b5e6419eabae3bf873c1413d9a550`. This is a **plan and scratch-SQL
measurement**, not an implementation. Verification design is folded into this plan.
CARD-1887's pin rewrite remains intact; CARD-1886 S1/S2/S4/S5 are separate work.

**Recommendation:** an additive, per-session durable transcript summary, maintained
transactionally by transcript triggers, with an exact legacy fallback. The measured
512-row summary-read prototype uses 86 shared blocks and 3.8–6.0 ms, versus 162,246
blocks and 1.95–2.45 seconds for the current seed. No tested index or bounded-tail
alternative both eliminates the scan and preserves the result.

**Decision gate before Code:** D-1 expands the older CARD-1915 text's “No migration
… unless an index is shown to remove the count.” The index experiment does not meet
that condition. The dispatch explicitly requested a summary-table alternative; this
plan chooses it under that stated default rather than pretending an index removes
an exact count. Have the caller settle that scope on the card and commission the
migration-roster performance Debug described below. The artifact is ready for plan
review/decision; it does not authorize silently starting the expanded implementation.

## Code commissioning amendment (8e452527, S1–S3 only)

D-1 and D-3 were accepted on CARD-1915 revision 6 under standing authority.
Performance Debug: task 71aaddba, report commit c42a35a57; see
[roster evidence](../../investigations/2026-11-14-card-1915-roster-performance.md).
RP replacements supersede CP-15/18/19/23/24/29 without changing membership.
Replacement filters remain unmeasured standalone; bounds are provisional, not p95.
Execution caps: RP-15a=600s, RP-15b=480s, RP-15c=510s, RP-18a=330s, RP-18b=510s, RP-18c=510s, RP-18d=300s, RP-19a=330s, RP-19b=330s, RP-19c=540s, RP-19d=300s, RP-23a=570s, RP-23b=270s, RP-24a=420s, RP-24b=540s, RP-24c=420s, RP-29a=450s, RP-29b=300s.

Still unmeasured: CP-30..33; requested repeats CP-11/15/19/23; additional
budget repeats CP-24/28/29. These remain owed for S8 qualification. S1–S3
are explicitly commissioned now with CP-1/2/3. The brief narrows CP-1's
provisional execution cap from 14 to 10 minutes; CP-2/3 retain 8/4.
Later S4–S8, V-4..8, completion of V-1/R-1/R-2 and operational acceptance
remain for the separately commissioned continuation. No reader, backfill or
activation is introduced by S1–S3. All PCs remain pending SourceLanding Mutation.

Publication correction: 59ff5ab imported the plan/report, but the amendment
script could not run because Python is absent. This follow-up applies the actual
partitions; the earlier commit title does not constitute amendment evidence.

## Ground truth

| Card/brief assumption | Code and observed behavior at the source SHA | Consequence |
|---|---|---|
| Loader is under Application | `server/Infrastructure/Data/SessionStateLoader.cs`; interface is `server/Application/Interfaces/ISessionStateLoader.cs` | Change the infrastructure implementation; keep the public snapshot contract. |
| Each dashboard read reseeds | `SessionStateStore.ReadCoreAsync/EnsureLoadedAsync` returns admitted Ready entries from memory. `AgentService` and queue cached reads use this store. `AgentSessionService.GetTranscriptAsync` separately syncs the runner and pages transcript rows. | An additional process cache duplicates existing behavior and cannot fix cold/reconcile cost. A transcript/dashboard request can indirectly ingest/reconcile, but is not inherently one seed. |
| Warmup reads all transcripts each minute | `SessionStateWarmupService.StartAsync` waits for `WarmAsync`; its one-minute loop refreshes pins, evicts idle entries, and loads only cold pinned entries | Preserve cadence and startup/cache gates. Pins are a separate statement. |
| Only startup causes seed | Cold reads, first ingestion through `BeginWriteAsync`, Missing/fault retry after the default 5 s, non-admitted reads under the 4,096-entry cap, terminal eviction after 15 min, failed/non-append publication, ambiguous commit, retention and board/project deletion reconciliation also seed | Preserve all paths, including a durable result after an append and reset. |
| Seed takes 1.9 s at every size | CARD-1887's 3,118,500-row fixture gave 1,917 ms; its *different* 100,000-row review fixture gave 86.554 ms. This task reproduced 1,953–2,452 ms for 512 sessions | Do not compare those fixtures as a before/after optimization. |
| Transcript indexes are missing | Unique session/sequence, session/kind/sequence, end-sequence and end-timestamp partial indexes already exist | Last-row/marker probes already seek; the expensive work is totals and activity. |
| LastSequence is the count; newest sequence is newest timestamp | Exact `count(*)`; `max(coalesce(Timestamp,CreatedAt))`; sequence and producer time are independent. Stored sequence is unique, but failed per-row persistence can leave gaps | No replacement by max sequence, newest-row timestamp, approximate count or last N rows. |
| One global row ordering is promised | Loader returns a dictionary; pins a set; neither SQL has an outer ORDER BY. Within-session last/marker selection is ordered by unique Sequence | Compare complete rows sorted by SessionId for a deterministic oracle, and separately assert every sequence/time field. Do not add an outer-order contract. |
| Cache is the main remaining win | Passive HTTP diagnostics, same epoch, 02:45:41–02:51:10 UTC: 3 seeds, 6 pins, 4,525 hits, 501 ingests, 1 eviction in 328.164 s; capacity fallbacks were zero. Historical desktop trace: 29 seeds/120 s and 3,067/44.1 h | Current observed rate is 0.55 seeds/min, not the historical 14.5/min burst. Counters do not identify individual trigger causes; no causal attribution is invented. |

Callers reviewed: `SessionStateStore`, `SessionStateWarmupService`, `AgentService`,
`SessionMessageQueueService`, `AgentSessionRuntime` (normal, synthetic, partial-save,
ambiguous-commit and receipt reads), `DataRetentionService`, and
`SessionStateDeletion`. `SessionMessageQueueService.IsWorkingBatchAsync` is the
separate `TranscriptWorkingStateQuery` fallback; optimizing it is not this card.

## Measurements and option ranking

Evidence root for this task is
`/work/worktrees/task-ee0bb3d9/.antiphon/evidence/card-1915/` (ignored).
`measure.cjs`, `options.cjs`, `writes.cjs`, their SQL/JSON/log outputs, source SHA,
load snapshots and HTTP diagnostics are there. Essential results are copied here
because generated evidence must not be committed. No build or TUnit suite ran. The captured seed SQL used for experiments was
reconstructed from the current loader/constants and matched after whitespace
normalization; it was not an unverified old-query substitute.

Used an owned database `card1915_ee0bb3d9`, cloned from the existing shared test
Postgres template; no container was created and no production database was opened.
Reused `SessionStatePinQueryTests.Scale_plans_enumerate_open_tasks_once`'s transcript
INSERT verbatim and its task distribution: 512 sessions, 27,695 tasks (256 open),
32 × 75,000 plus 479 × 1,500 transcript rows, one empty session, **3,118,500 total**.
Schema came from the test template; synthetic session/task prototypes came from the
retained CARD-1887 test-only scratch database, not live data. The materialized task
copy retained the existing fixture's pattern. UUIDs and non-predicate prototype
metadata are not a production-data replay. Exact cardinality was checked again
following the rolled-back write probes.

Postgres 16.15, Linux x64, 128 MB shared_buffers, `SET jit=off`, session-bounded
60-second statements; `EXPLAIN (ANALYZE, BUFFERS, SETTINGS, FORMAT JSON, TIMING OFF)`.
Root shared blocks means hit + read, not a sum over nested nodes. Three samples per
baseline/seek/tail query; five per summary, covering+seek and actual denormalized-column query.
Initial 1-minute load was 17–20, rising to 40–48 during the covering-index series.
These are small, contended samples, not production p95 estimates. Do not infer a
wall-time speedup from the index series across that load change.

| Rank / candidate | 512-session execution ms, observed range | Root cost / shared blocks | Write cost, migration risk and rollback |
|---|---:|---|---|
| Reference: current seed | 1,953–2,452 | 17,753,365 / 162,246 | Current behavior. Totals and activity each visit the session history. |
| **1: full per-session summary** | **3.840–6.010**; later loaded-host repeat 3.575–5.953 | **14,528 / 86** | Read prototype only: 512 precomputed complete rows, fresh pin/session fields. Transactional maintenance/backfill still require implementation and tests. Extra small row update per transcript INSERT statement/session; invalidation on UPDATE/DELETE. New table/triggers, online backfill. Roll back reader flag to legacy; leave additive data intact. |
| 2: additive covering index + activity range seek | 1,202–2,201 | 12,605,655 / 33,202–33,207 | `("AgentSessionId") INCLUDE ("Timestamp","CreatedAt")`: 154,894,336 bytes; concurrent build 14.303 s. Visits all 3,118,500 rows for totals, even with index-only access. Extra index insertion per row. Drop only this index concurrently in a forward rollback; original indexes remain. |
| 3: query-only activity split | 1,271–1,736 | 21,317,705 / 89,289 | Split null-end and non-null-end paths with UNION ALL so Sequence is an index condition on the latter. Zero additional writes/migration. Revert SQL. Removes a redundant scan but still reads the exact totals; estimated cost even increases. |
| 4: denormalized newest-effective-time column, plus activity split | 657–980 | 12,662,455 / 40,339 | Actual nullable column added/backfilled on the scratch AgentSessions table (208 ms client wall); totals retains exact count. A last-activity-only column likewise cannot supply count and all markers. Adds an update/lock on the wider session row. Stop reading the column on rollback; leave additive schema in place. |
| Existing cache / proposed second seed cache | Warm hit: **0 DB statements** by code and existing cache tests; cold: reference timings above | Warm: no plan/blocks; cold: reference | Already implemented. TTL-only caching is stale after append/reset; correct invalidation recreates the existing store. No additional cache is selected. Actual in-process hit latency was not measured. |
| Rejected: last N history, N=256 measured | 219–235 | 12,985,851 / 20,666 | No write/migration; revert SQL. **1,022 symmetric-difference rows = 511 changed sessions**. Exact count is lost; history can contain a maximum timestamp or marker before the tail. LIMIT 1 is already used for last row and indexed boundaries; it cannot supply totals. |

Covering index **without** the activity split: 2,961–4,676 ms, cost 9,041,305,
99,031 blocks under the higher load. It does not eliminate the remaining scan.
A preliminary scalar-summary lookup for the denormalized option took 760–1,238 ms
and 54,976 blocks; the actual session-column experiment above supersedes that proxy.
A partial boundary index cannot cover an exact count over every kind. The covering
experiment's totals became `Index Only Scan c1915_cover`, 512 loops, approximately
6,091 rows/loop, 20,321 blocks, 52 heap fetches in its first plan. Visibility helps
heap access; it does not make counting constant-time.

Complete-row differential on this fixture: current versus activity split **0**,
current versus summary-read prototype **0**, current versus last-256 **1,022**.
The summary prototype materialization took 4.029 s including client/process overhead;
heap + PK totaled 163,840 bytes after the probes. This is a backfill-cost witness,
not a measurement of the future background worker or trigger implementation.

Five rolled-back `EXPLAIN (ANALYZE, BUFFERS, WAL)` append samples per shape:

| Scratch operation | Execution ms | Blocks / WAL bytes | Interpretation |
|---|---:|---|---|
| 1 transcript row, original indexes | 7.838–12.847 | 180–181 / 399 | Existing insert and FK/index work. |
| 1 row with covering index | 7.790–12.921 | 203–204 / 487 | +88 WAL bytes in this sample; not a CPU estimate. |
| 100 rows, original indexes | 18.086–30.929 | 1,080–1,317 / 40,512–47,848 | One INSERT SELECT, not an EF batch measurement. |
| 100 rows with covering index | 24.625–30.641 | 1,604–1,649 / 49,879–58,533 | Extra index work persists. |
| One summary-row projection update | 0.382–0.568 | 9–12 / 360 | Lower-bound maintenance proxy; excludes classification, transition-table and lock contention. |
| One AgentSessions timestamp update | 1.292–1.614 | 52–55 / 764 | Proxy for the denormalized-column write location, not a new column benchmark. |

Record, rather than hide, two experiment failures: the first scratch task copy
omitted the fixture's materialization and hit its 60 s timeout; no task rows
committed. Restoring the fixture's materialized-copy shape took 10.628 s. A default
parallel VACUUM failed on the test container's shared-memory capacity; scratch-only
`VACUUM (PARALLEL 0, ANALYZE)` succeeded in 1.459 s. No timeout, production setting,
container memory limit or test assertion was relaxed. Transcript seed batches took
274.349 s total, maximum batch 39.393 s. No test green/red claim is based on these
setup failures.

### Three-line run summary

1. Shared-test clone, reused CARD-1887 scale seed: 512 sessions / 27,695 tasks / 3,118,500 transcripts; transcript preparation 274.349 s; no builds or TUnit tests.
2. Current seed: 1,953–2,452 ms, cost 17,753,365, 162,246 blocks; summary prototype: 3.840–6.010 ms, cost 14,528, 86 blocks; complete-row differential 0.
3. Covering+seek: 33,202–33,207 blocks and still 3,118,500 counted rows; last-256 changed 511 sessions; append/summary-update probes rolled back; scratch database removed after evidence capture.

### Which indexes each current lateral uses

| Lateral | Actual index/access in the baseline scale plan | Semantics to preserve |
|---|---|---|
| totals | Bitmap index/heap via `IX_TranscriptEntries_AgentSessionId_Sequence`; ~75k root-inclusive blocks for this subtree | Exact bigint count; MAX of row-wise timestamp-or-CreatedAt, not separate maxima. |
| last_row | Backward `IX_TranscriptEntries_AgentSessionId_Sequence`, LIMIT 1 | Id/kind/timestamp/CreatedAt all from the greatest Sequence row. |
| end_seq | Backward index-only `IX_TranscriptEntries_End_AgentSessionId_Sequence` | Maximum sequence satisfying frozen EndPredicate. |
| end_ts | Backward index-only `IX_TranscriptEntries_End_AgentSessionId_Timestamp` | Independent maximum non-null end timestamp. |
| activity | Bitmap index/heap via session/sequence; OR condition remains a filter and rescans the full session | Activity after greatest end sequence; any activity, any null timestamp, maximum activity timestamp. |
| turn_end / title / prompt | Backward index-only `IX_TranscriptEntries_AgentSessionId_Kind_Sequence` | Three independent kind-specific sequence maxima; absent marker remains null. |

Existing index owners: `AppDbContext.cs:1714`, migrations
`20260925081051_AddTranscriptHotPathIndexes` and
`20261108121426_Card1773TranscriptPromptStampIndex`. Their retry helper performs
invalid-index detection/rename in an autocommit DO, then separate
`DROP INDEX CONCURRENTLY` and `CREATE INDEX CONCURRENTLY IF NOT EXISTS`, each with
`suppressTransaction: true`; model indexes use `IsCreatedConcurrently()`.
Concurrent DDL is not legal inside either an EF migration transaction or the DO.
If the rejected index option is revived, preserve that invalid-index recovery and
LF-neutral SQL/model parity. The selected option adds indexes only on a **new, empty
summary table**; it adds no large-table index and rewrites no TranscriptEntries rows.

## Decisions

- **D-1 — scope default requiring caller decision:** choose the full durable summary,
  accepting an additive migration and bounded write amplification. A query-only
  half-fix and an index that still counts all history do not satisfy the cold-seed
  target. This expands the old card restriction as explained above.
- **D-2 — database-maintained metadata:** statement-level PostgreSQL triggers with
  transition tables maintain a summary in the transcript transaction. Reject a
  process-only publisher or post-commit task: they can lose freshness on crash,
  fallback persistence, retention, raw SQL or a rolling deployment of old code.
- **D-3 — one fast read, bounded exact fallback:** a cheap summary seed projects all
  rows plus a private `ProjectionValid` bit. If any existing session lacks a valid
  current-version summary, discard the *entire* probe and execute the frozen legacy
  seed for the whole requested batch. Never combine rows from two statement
  snapshots. Ready batches take one read; fallback batches take exactly two; the
  disabled flag takes the original one. Keep both tagged `session-state.seed` and
  document the additional read-attempt count. Reject one giant guarded UNION with
  the legacy query: its estimated fallback cost can still trigger JIT even when
  never executed. Do not hide that cost behind a function's arbitrary COST value.
- **D-4 — summary fields:** store only the 15 transcript projection fields listed in
  the loader (Count through LastUserPromptSequence), keyed by AgentSessionId with
  cascade FK, plus schema version, validity and nullable NextBackfillAttemptAt.
  Session existence, StartedAt and all
  pin arms are read live in the seed statement. No text, UUID payload, pin cache,
  user-visible revision or ServerEpoch is persisted in this table.
- **D-5 — conservative invalidation:** append incrementally only if every inserted
  Sequence is greater than the locked valid summary's LastSequence. An out-of-order
  insert, any UPDATE, or DELETE invalidates; it does not guess. Missing/invalid
  summaries remain invalid until an exact backfill. No approximate count.
- **D-6 — online backfill:** an owned background service backfills missing/invalid
  rows under per-summary transaction locks; skip locked candidates and bound each
  batch/call. Do not backfill millions of transcripts inside migration or block
  startup readiness. The legacy fallback remains correct during partial progress.
- **D-7 — rollout/rollback:** `SessionState:DurableTranscriptSummaryEnabled` initially
  false. Enabling admits summary reads and backfill. Disabling returns the original
  seed SQL; triggers continue maintaining existing valid summaries, so later
  re-enable is safe. A later forward migration can remove triggers if write cost is
  unacceptable, leaving the table and reader disabled. Down fails safely; it does
  not delete summary or transcript data. Activation is separately commissioned.
- **D-8 — test/placement:** portable server/Postgres integration lane; native Windows
  only for the expressly listed impacted witnesses. GET runner-defaults and
  session-runners were read (revision 2; eligible portable capacity available).
  Re-read them at dispatch; omit `-Runner` and `-Platform` for portable tasks.
  `-Platform Any` unpins a previous restriction. No fleet address is baked into this
  plan. Do not use production runner sessions or create a private Postgres container.

## Why this is safe

The summary is a materialized copy of metadata, not a new source of transcript
truth. It is used only with a valid recognized version. INSERT maintenance and
invalidation commit with transcript changes; a crash or transaction rollback cannot
publish only half. A seed statement sees both through one MVCC snapshot. If the
probe finds any invalid row, its result is discarded before cache publication and
one complete legacy statement supplies the final snapshot.

The trigger locks touched summary keys in UUID order before changing them. For a
missing key it creates an invalid placeholder with conflict handling, then locks
that row. A concurrent older writer still runs these database triggers. Duplicate
replays with no inserted rows do not increment counts. Multi-row statements fold
rows by Sequence, not transition-table encounter order. Current EF batching must be
measured: one SQL INSERT may be one entity, so do **not** budget one summary write
per runner batch. Bound the overhead at one update per inserted SQL statement and
affected session; no-op/duplicate-only persistence adds no summary write. Current
runtime append transactions concern one session. Add a multi-session transaction
race witness too: sorting within a statement alone does not guarantee transaction-
wide lock order. Any deadlock must abort atomically and take the existing persistence
recovery path; do not introduce an unbounded retry or claim contention is measured.

For a valid monotonic append, add the inserted count, take MAX of effective times,
and choose the greatest sequence's complete row. Combine each kind maximum
independently. Compute the greatest inserted end sequence and maximum inserted end
timestamp independently. If a new end exists, discard earlier activity and fold
only inserted activity above that end; otherwise combine existing activity with the
new rows. Null timestamps, equal end/activity timestamps (`>=`), manual compaction,
interrupted prompts and excluded control prompts follow the exact current SQL
predicates. Migration SQL freezes those predicates; tests compare it with the
existing projection oracle so future predicate changes cannot silently drift.

UPDATE invalidates both old and new session keys if a row moves. DELETE invalidates
surviving session summaries; session deletion cascades the summary and must never
recreate a row for a missing session. Invalidation must clear validity even when
Count/LastSequence happen not to change. Arbitrary administrative TRUNCATE is not a
runtime API; add an AFTER TRUNCATE invalidation trigger as a conservative guard so
maintenance cannot leave valid summaries over an empty table.

Backfill creates/locks the placeholder, reads exact legacy metadata in that
transaction, then marks the same row valid/versioned before commit. An append whose
trigger waits on this lock either precedes the reconstruction or subsequently folds
onto it/invalidates it. A delete/update waiting there subsequently invalidates it.
Do not use an unlocked read followed by an upsert. Do not delete/replace summaries
as a queue; invalid rows are durable retry work. Timeout/cancellation rolls back
and leaves invalidity. Failed sessions receive NextBackfillAttemptAt = now +
5 seconds, with at most one attempt per scheduling cycle; keyset fairness keeps a poison/busy key from starving
other sessions. Invalidation clears the delay when fresh data can repair the state.

Migration contains only new table/index/trigger/function definitions. Use
`SET LOCAL lock_timeout = '5s'` as a bounded DDL admission wait and surface refusal,
not an unbounded migration wait; trigger
installation takes a brief table lock but no transcript rewrite/backfill. A
partially installed migration cannot expose a valid summary before all invalidation
paths are installed. Keep version validity false until the complete installation
commits. A new migration ID must sort after then-current master. Preserve old
migrations and safe Down behavior; build predecessor schemas forward in tests.

This plan does not add a user-input waiting session or alter seat release/parking.

## Slices

Each estimate is Code authoring time, 30–60 minutes, excluding the separately
budgeted ordinary verification. Commit regression tests before the corresponding
implementation, commit/push every slice, and preserve failing receipts.

| Slice | Minutes | Files and result | Tests / PCs |
|---|---:|---|---|
| S1 | 45 | Extract existing scale seeder into `tests/Antiphon.Tests/TestHelpers/SessionStateScaleFixture.cs`; reuse it from `SessionStatePinQueryTests.cs`. Freeze **post-CARD-1887** seed in `TestHelpers/SessionStateSeedBaseline.cs`; keep existing pre-pin baseline unchanged | Existing pin guards; frozen semantic oracle; CP-1. The new summary-seed tests are authored in S6. |
| S2 | 60 | `server/Domain/Entities/SessionTranscriptSummary.cs`, `AppDbContext.cs`, CLI-generated `server/Migrations/<timestamp>_AddSessionTranscriptSummaries{,.Designer}.cs`, model snapshot. New table/version/validity, cascade FK, incomplete-work index, trigger/function skeletons that only invalidate | `SessionTranscriptSummaryMigrationTests.cs`; additive/forward predecessor/model tests; CP-2; PC-migration, PC-invalidate. |
| S3 | 60 | Finish INSERT trigger's monotonic fold in the new migration; freeze classification SQL; no change to runtime append/dedupe/rebase implementation | `SessionTranscriptSummaryAppendTests.cs`; CP-3; PC-order, PC-count, PC-append, PC-tie, PC-end. |
| S4 | 45 | Finish UPDATE/DELETE/TRUNCATE invalidation and deterministic lock order in that migration. No mutation of historical migrations | `SessionTranscriptSummaryInvalidationTests.cs`; existing retention/commit tests; CP-4; PC-invalidate, PC-delete. |
| S5 | 60 | `server/Infrastructure/Data/SessionTranscriptSummaryBackfill.cs`, `Infrastructure/Supervision/SessionTranscriptSummaryBackfillService.cs`; register in `server/Program.cs`; setting in `Application/Settings/SessionStateSettings.cs`. Bounded shared-clock scheduling, keyset work discovery, per-session transaction and skipped locks | `SessionTranscriptSummaryBackfillTests.cs`; CP-5; PC-race, PC-backfill. |
| S6 | 45 | `SessionStateLoader.cs`: separate summary SQL, validity bit and whole-batch legacy fallback; keep legacy SQL and pin CTE available. Wire setting; no public DTO/route change | Complete `SessionTranscriptSummarySeedTests.cs`, cache/working-state tests; CP-6; PC-missing, PC-snapshot, PC-stale. |
| S7 | 60 | `SessionTranscriptSummaryScaleTests.cs` on shared S1 fixture; randomized/adversarial differential, scale plan/read/write witnesses, fixture timing evidence | CP-7, pending PC-scan; collect actual trigger costs and fast-query plan rather than reusing prototype numbers. |
| S8 | 45 | Required model/downgrade fixture repairs if safe Down exposes them; regenerate `tests/derived-impact-index.json`; update only relevant test documentation/registry entries if guards demand them | CP-8 onward: closed impacted census, downstream/native witnesses and final guards. No policy or historical receipt repinning. |

Authoring floor: **420 minutes**. This is not a 60-minute Code task disguised as a
plan. Separate ordinary verification and post-land Mutation costs below remain owed.

## Verification design

The public result is unchanged; D-3 intentionally introduces a bounded extra read
only while durable metadata is unavailable. Existing disabled-mode one-command
assertions stay intact. Add explicit enabled/valid=1 and enabled/invalid=2 command
assertions; a command per session is a regression. Pin lookup is always one command.

| ID | Exact behavior and method to implement | Evidence |
|---|---|---|
| V-1 | `SessionTranscriptSummarySeedTests.Legacy_rows_match_randomized_and_adversarial_histories`: fixed PRNG seeds, 128 sessions, sparse/negative sequences including bigint minimum, empty/missing sessions, shuffled insert order, null/equal/reversed timestamps, all kinds/control text, manual/auto boundaries, multiple generations | Full projected rows equal frozen SQL at one transaction snapshot; dictionaries compared by sorted SessionId, sequence selections asserted independently. Duplicate requested IDs retain rejection; 0 and >512 bounds unchanged. |
| V-2 | `SessionTranscriptSummaryAppendTests.Monotonic_append_folds_only_committed_rows`: 1/100-row SQL statements, actual EF append batching, duplicate replay, mixed kinds, partial fallback/stub, savepoint rollback and ambiguous commit | Exact new summary, then fresh-process seed equals legacy. Count is actual inserted rows; gaps are retained. Run existing SessionStateCommitTests and AgentSessionRuntimeTests. |
| V-3 | `SessionTranscriptSummaryAppendTests.Timestamps_ties_and_end_maxima_remain_independent` | Earlier-sequence end has greatest time; newest-sequence end has earlier/null time; equal-time activity remains working; all fields match. |
| V-4 | `SessionTranscriptSummaryInvalidationTests.Nonappend_delete_update_and_truncate_never_leave_valid_stale_rows` and `...Rolled_back_mutations_leave_the_previous_summary` | Direct SQL, retention, board/project cascades, moved session key, emptied transcript, metadata/text-only edit, delete latest vs older end, rollback. Raw operations are test-owned. |
| V-5 | `SessionTranscriptSummaryBackfillTests.Backfill_serializes_with_append_delete_and_an_older_writer` and `...Cancellation_restart_and_busy_keys_leave_retryable_work` | Barriers at locked read and before publish; both transaction orders; pre-summary writer bypasses application maintenance but cannot bypass DB trigger. No sleeps for correctness. Restart resumes invalid work, skipped locks do not starve others. |
| V-6 | `SessionTranscriptSummarySeedTests.Missing_and_empty_sessions_keep_readiness_and_metadata`, `...A_mixed_batch_uses_one_final_statement_snapshot`, `...Ready_and_fallback_statement_budgets_are_bounded` | No missing key lost to inner join. Change a previously valid session between failed probe and legacy read; final batch must wholly reflect legacy read. 1/2 read counts, fresh pin arms after task transitions. |
| V-7 | `SessionTranscriptSummaryScaleTests.Scale_seed_matches_legacy_without_transcript_scans` | Existing scale data, batches 1/64/512, before/after all-field differential; valid summary fast SQL cost <100,000; root blocks <=2,000 and <=baseline/50 for 512; zero TranscriptEntries scan loops; open-task enumeration once. Save full plans before asserting. No wall-clock gate. |
| V-8 | `SessionTranscriptSummaryScaleTests.Append_cost_and_backfill_phases_are_recorded` | Baseline vs actual trigger: five measured samples each after one warm-up, 1/100-row SQL and actual EF batches; rows/statements, buffers, WAL, lock waits and timings. Report write regression explicitly; no unmeasured “per batch” savings. |
| R-1 | New migration class: predecessor→tip, model parity, idempotent complete install, interrupted install retry, safe Down refusal, FK cascade, LF-neutral SQL, all invalidation triggers present | Existing migration/downgrade classes below; native Windows generated-text/model witness. |
| R-2 | Closed guard/impact census, suffix roster check, actual diff evidence audit and conflict-free merge-tree | Every row gets nonzero fresh TRX counts and source-bound build receipt. |

V-7's prototype has comfortable headroom (86 blocks); the actual fast path adds
validity/version filtering, not a hidden legacy plan. If its cost/blocks exceed the
budget, investigate the plan; never relax thresholds or force planner settings.
For invalid summaries, legacy scan/cost is expected and must be visible in evidence.
Repeat randomized differential before/after append, invalidation and backfill. A
synthetic summary-read table alone cannot discharge V-2 through V-6.

### Positive controls (pending post-land SourceLanding Mutation)

Every row below uses `/*/*/Class/ExactMethod`, one method per red/restore/green cycle.
A compiling semantic defect and the intended assertion failure are required; zero
matches, fixture/build errors and timeout are not red. Production/test repairs do
not happen in a SourceLanding snapshot. Independent controls may share a setup only
under the repository's method-bound mutation custody rules.

| PC | Deliberate defect | Exact method / expected assertion |
|---|---|---|
| PC-order | Pick last-row fields by Timestamp rather than Sequence | `SessionTranscriptSummarySeedTests.Legacy_rows_match_randomized_and_adversarial_histories` / full row differs. |
| PC-count | Increment count by highest sequence delta, including a gap | `SessionTranscriptSummaryAppendTests.Monotonic_append_folds_only_committed_rows` / exact count differs. |
| PC-append | Omit the summary update for a successful append | Same exact V-2 method / new seed is stale. |
| PC-tie | Change working equality from >= to > in the projection consumer under test | `SessionTranscriptSummaryAppendTests.Timestamps_ties_and_end_maxima_remain_independent` / equal-time activity must be working. |
| PC-end | Use timestamp from greatest-sequence end instead of independent MAX | Same exact V-3 method / EndTimestamp and Working differ. |
| PC-missing | Inner-join requested sessions to summaries | `SessionTranscriptSummarySeedTests.Missing_and_empty_sessions_keep_readiness_and_metadata` / missing/empty key absent. |
| PC-stale | Treat invalid summary as ready | `SessionTranscriptSummarySeedTests.Ready_and_fallback_statement_budgets_are_bounded` / direct non-append edit returns stale values instead of legacy result. |
| PC-snapshot | Keep valid probe rows and replace only invalid rows from fallback | `SessionTranscriptSummarySeedTests.A_mixed_batch_uses_one_final_statement_snapshot` / deliberately changed valid row has wrong value. |
| PC-invalidate | Skip invalidation when UPDATE leaves count/max-sequence unchanged | `SessionTranscriptSummaryInvalidationTests.Nonappend_delete_update_and_truncate_never_leave_valid_stale_rows` / text/timestamp edit leaves stale metadata. |
| PC-delete | Omit DELETE invalidation | Same exact V-4 method / removed latest/only end survives. |
| PC-race | Move backfill's summary lock after its legacy read | `SessionTranscriptSummaryBackfillTests.Backfill_serializes_with_append_delete_and_an_older_writer` / barrier schedule loses append/invalidation. |
| PC-backfill | Mark a canceled backfill valid | `SessionTranscriptSummaryBackfillTests.Cancellation_restart_and_busy_keys_leave_retryable_work` / canceled work must remain invalid/retryable. |
| PC-migration | Install validity/reader path without the UPDATE trigger | `SessionTranscriptSummaryMigrationTests.Validity_requires_every_invalidation_trigger` / actual edit detects missing coverage; not merely a SQL-string check. |
| PC-scan | Replace the ready fast query with legacy seed | `SessionTranscriptSummaryScaleTests.Scale_seed_matches_legacy_without_transcript_scans` / transcript loops/block/cost budget fails with equal rows. |

Ordinary Review is separate before land. All 14 controls remain pending, not
claimed executed by this Plan. Mutation must test the actual landed SHA and retain
its restoration record outside a SourceLanding snapshot.

## Existing tests affected (CARD-1782)

Owner: [impacted-classes table](../../../tests/impacted-classes.json) and
[selector contract](../../testing-and-build.md#impacted-classes-card-1782).
The planned schema/model paths select **migrations**, including forward/downgrade
consumers, not just the new migration test. The model-only prospective census has
78 classes; the full prospective path list (including Program and loader) has 97
plus 3 native Windows classes. The closed rows below include that returned census.
The reverse graph additionally reports 571 capped broad dependencies for
AppDbContext; those are explicitly **not ordinary coverage credit** and stay owed
to downstream release qualification. No claim of exhaustive release coverage.

Public DTOs/routes: none changed. Remote script functions: none changed. Existing
operation tags stay the same; include `TestTimingOperationLabelTests` if fixture
phase instrumentation adds spans (already in the final guard row here). The table's
hand-authored `statement-budgets` glob does not match the selected production paths,
but the directly relevant seed/cache budgets in V-6 and existing pin/cache/warmup
classes are required. No dispatch-journal budget is silently broadened.

At Code/Review rerun `scripts/impacted-classes.ps1 -Range <base>..<HEAD> -Json`.
An expanded actual footprint requires a plan amendment naming exact additional
rows before running them. Do not silently run a namespace or whole assembly.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards)
and [test performance standards](../../test-performance-standards.md), not the
checkpoint importer's default 15-minute/3x allowance. Small-sample estimates here
are explicitly provisional under CARD-1915; CARD-1404 owns measured cost packing,
CARD-1514 slow/shared fixtures, and CARD-1531 admission versus execution timing.

The schema expands the verification cost substantially. The prospective 97-class
census has no current per-selection p95. Its closed batches below are provisionally
**over 30 minutes in aggregate**. Before Code, the caller must commission a **Debug
performance task for this exact roster**, using retained current-source timing
receipts first and measuring only gaps. It must record its task/card in this plan,
return actual p95 and phase costs, and repack/amend these rows if needed. This is a
real prerequisite, not an instruction to run a large suite and hope it fits. Do not
launch a monolithic run of all rows. Do not dispatch this subtask from this delegate.

| Work / phase | Measurement and budget | Timeout / status |
|---|---|---|
| Build and slot admission | No build in this Plan. Prior CARD-1887 rows used bounded 10-minute commissioning; no current cold-build p95 | Separate admission cap 10 min; slot timeout is not permission to run unleased. Until Debug measures the graph, cold build cap 10 min is a provisional exception, not p95. |
| Shared DB start/readiness/migrations | Existing shared test Postgres/template reused; new-container startup and migration times not measured here | Once per test assembly/run via TestDbFixture; no new container for a test/class. Record unknown phases as unknown. |
| Clone / baseline preparation | Own schema clone about 1 s including client overhead; task copy 10.628 s; transcript batches 274.349 s, max 39.393 s | One scale setup per run. Keep inherited 60 s per-insert deadline; do not enlarge it. Whole scale setup provisional 6 min. n=1 total is **not** a measured p95. |
| Small semantic/race tests | New tests not implemented; target <=10 s each after shared setup, small-clone initialization separately recorded | Provisional 20 s per method, 2 min per small-test row after build. Collect first five comparable runs; derive approximately 2x p95 before expanding use. No timed sleep correctness assertions. |
| Scale body / direct DB reads | Baseline n=3 p95 by nearest-rank 2.452 s, summary n=10 p95 6.010 ms across two series; single 75k session n=5 p95 161.767 ms | Query timing descriptive only. Provisional 6 s legacy EXPLAIN deadline (~2x these/retained loaded samples). Provider integer-second minimum may give a 1 s fast-read timeout: explicit granularity exception, not a 1 s performance target. |
| Backfill transaction | Full prototype materialization 4.029 s; per-heavy-session read p95 161.767 ms excludes the real worker's lock/update phases | Proposed per-session call deadline 400 ms is provisional, rounded above 2x that read sample. Code must measure the real transaction and set approximately 2x its p95. Timeout leaves invalid work; never publishes partial fields. |
| Backfill scheduling | No real worker measured. Start with up to 32 candidates/cycle and <=1 s cumulative DB work, then a cancellable 1 s pause; use SKIP LOCKED and keyset fairness | Provisional bounded settings, tune from actual transaction measurements; no readiness wait for complete fleet backfill. Tests use an injected clock and barriers. |
| Scale row | Prior reviewed fixture setup 260 s; this transcript setup 274.349 s, plus 10.628 s task preparation; old review's earlier scale test ~444 s | CP-7 provisional estimate 7 min, execution cap 14 min (~2x retained 444 s). Explain/differential/write body target <=90 s, teardown <=15 s, remainder recorded. CP-1 also contains
the inherited scale test and uses the same provisional 14 min execution cap;
CP-4/CP-6 use the 8 min integration-row cap pending Debug, not the 2 min small-row cap. A fresh uncached summary must be a real startup witness, not rebuilding the whole DB per case. |
| Impact rows / Windows | No comparable roster p95 available; integer estimates below are commissioning allocations only | CP-8–CP-31 rows provisional 4 min estimated / 8 min execution each; CP-32 4/8; native CP-34 10/20. Debug must validate or replace before Code; no green credit for skipped prerequisites. |
| Guard row | CARD-1887 selected 117 tests; no current duration attribution for this expanded union | CP-33 provisional 2 min / 4 min execution. Tool/bootstrap builds reported separately. |
| Post-land PCs | 14 method-bound cycles: 13 small controls, one scale control. Scale regeneration at each fresh process can dominate | Preliminary authoring-independent floor 13×4 + 20 = **72 min**, before incremental builds/discovery. Debug/Mutation plan must price the bound-pack reuse and own the >30-minute obligation; split supervised runs, never whole-class mutants. |

Use the existing shared database fixture and a run-scoped lazy `SessionStateScaleFixture`:
seed once, retain an immutable baseline, and use transactions or owned clones for
mutating cases. Existing pin scale and new summary scale classes must share that
baseline in CP-7's single process. Do not duplicate 3.1 million rows per method.
Migration tests use `MigrationTestDatabase.CreateAsync(predecessor)` where fresh
forward migration is the subject; this is the measured fresh-start exception, not
a downgrade of the tip database.

Record ignored `.antiphon/evidence/card-1915/<run-id>/` and checkpoint receipts:
source SHA, OS/architecture, load before each EXPLAIN, exact test/category membership,
per-test and per-row duration, container/readiness, migration, clone, preparation,
host start, body, teardown, external DB/IO/process calls and unattributed remainder.
No host starts for the focused SQL tests. Build/slot/admission waits stay separate
from execution. No active provider/production session is used as a fixture.

App graph: main change is server/database; `Antiphon.Tests` references the server,
with migration-derived messaging coverage in `Antiphon.Messaging.Tests`. Record the
actual ProjectReference transitive graph at Code time. The returned Windows classes
exercise downstream session/provider plumbing; run them only in their isolated
native lane. This plan does not claim CARD-1545 release-wide scoped credit or run
all monorepo tests. Extraction of a new standalone test project solely for these
queries would add packaging work without avoiding the required migration coverage;
retain existing projects and shared setup.

### Checkpoints

Closed ordinary list below. All non-Windows rows are **portable server/Postgres
lane** (CP-33 is its guard lane); CP-32 is portable messaging/database lane; CP-34 is
**native Windows integration lane**. No physical host is pinned. `Min` is a
conservative nonzero floor; inspect every named class in fresh TRX, not just totals.
All EstimatedMinutes are integers. Rows must not be imported/executed with the
provisional migration budgets until the Debug prerequisite above has settled them.

Run the checkpoint tool once per committed `After` group, selecting exact `--rows`
as appropriate, with `--expected-source-sha <full SHA>` and `--serial`. Use per-group
explicit timeouts from the table above; set total timeout from that group's measured
execution budget and record `totalTimeoutOverrideReason` if below importer derivation.
Bound `build.slotWaitMinutes` at 10 in the imported ignored manifest. Bootstrap any
missing tool/derived-index driver through `scripts/build-slot.ps1`, report why and
its source, and use native default outputs for direct builds. Checkpoint-created
cold outputs below belong to its runner. Wait on exit 75 with <=60-second waits
until a terminal receipt; do not end a task with owned work running.

Class/method OR is `(NameA*)|(NameB*)`, with every operand starred. Table pipes are
escaped. Suffix-collision census of existing and proposed names is recorded below;
repeat after creating new classes. Never rely on `--list-tests` as execution proof.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1915-s1/ | baseline-fixture | `/*/*/(SessionStatePinQueryTests*)\|(SessionStateProjectionTests*)/*` | V-1 foundation | all listed, 0 failed | 2 | 7 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1915-s2/ | summary-schema | `/*/*/(SessionTranscriptSummaryMigrationTests*)\|(AppDbContextModelTests*)\|(AdditiveMigrationRuleWitnessTests*)/*` | R-1 | all listed, 0 failed | 3 | 4 |
| CP-3 | S3 | tests/Antiphon.Tests -> bin-c1915-s3/ | summary-append | `/*/*/(SessionTranscriptSummaryAppendTests*)\|(SessionStateCommitTests*)/*` | V-2,V-3 | all listed, 0 failed | 2 | 2 |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-c1915-s4/ | summary-invalidation | `/*/*/(SessionTranscriptSummaryInvalidationTests*)\|(SessionStateWarmupTests*)\|(DataRetentionServiceTests*)/*` | V-4 | all listed, 0 failed | 3 | 4 |
| CP-5 | S5 | tests/Antiphon.Tests -> bin-c1915-s5/ | summary-backfill | `/*/*/SessionTranscriptSummaryBackfillTests/*` | V-5 | all listed, 0 failed | 2 | 2 |
| CP-6 | S6 | tests/Antiphon.Tests -> bin-c1915-s6/ | summary-seed | `/*/*/(SessionTranscriptSummarySeedTests*)\|(SessionStateProjectionTests*)\|(SessionStateCacheReadTests*)\|(SessionStateWarmupTests*)\|(TranscriptWorkingStateQueryTests*)\|(TranscriptHotPathQueryTests*)/*` | V-1,V-6 | all listed, 0 failed | 6 | 4 |
| CP-7 | S7 | tests/Antiphon.Tests -> bin-c1915-s7/ | summary-scale | `/*/*/(SessionTranscriptSummaryScaleTests*)\|(SessionStatePinQueryTests*)/*` | V-7,V-8 | all listed, 0 failed | 4 | 7 |
| CP-8 | S8 | tests/Antiphon.Tests -> bin-c1915-impact/ | impacted-1 | `/*/*/(AbandonedLandingReconcilerTests*)\|(AdditiveMigrationRuleWitnessTests*)\|(AgentBundleAttachmentTests*)\|(AgentChannelServiceIntegrationTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-9 | S8 | CP-8 | impacted-2 | `/*/*/(AgentControlServiceIntegrationTests*)\|(AgentSessionCancelledTurnTests*)\|(AgentSessionRuntimeTests*)\|(AgentTaskCatchUpSettlementTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-10 | S8 | CP-8 | impacted-3 | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(AgentTaskInternalDecisionMigrationTests*)\|(AgentTaskLandApprovalPersistenceTests*)\|(AgentTaskLandHeldNotificationTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-11 | S8 | CP-8 | impacted-4 | `/*/*/(AgentTaskLandMonitoringTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandingPersistenceTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-12 | S8 | CP-8 | impacted-5 | `/*/*/(AgentTaskMidTurnRefineTests*)\|(AgentTaskReplyOverlayTests*)\|(AgentTaskStandingAgentDispatchTests*)\|(AgentTaskWorktreeBaseMigrationTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-13 | S8 | CP-8 | impacted-6 | `/*/*/(AgentTuiPersistenceTests*)\|(AgentTuiSecretMigratorTests*)\|(AppDbContextModelTests*)\|(C0824RefOnlyIdentityTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-14 | S8 | CP-8 | impacted-7 | `/*/*/(C0824RefOnlySnapshotMigrationTests*)\|(C1514HostLoadMigrationTests*)\|(C1729LayeringGuardTests*)\|(CapacityAdmissionLifecycleTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| RP-15a | S8 | CP-8 | impacted-rp-15a | `/*/*/(CapacityRecoveryCompatibilityTests*)\|(Card1619ExitPersistenceTests*)/*` | R-1,R-2 | all listed, 0 failed | 292.8 | 5 |
| RP-15b | S8 | CP-8 | impacted-rp-15b | `/*/*/Card1619ExitMigrationIdTests*/*` | R-1,R-2 | all listed, 0 failed | 237.2 | 4 |
| RP-15c | S8 | CP-8 | impacted-rp-15c | `/*/*/Card1773TranscriptPromptStampMigrationTests*/*` | R-1,R-2 | all listed, 0 failed | 252.0 | 4 |
| CP-16 | S8 | CP-8 | impacted-9 | `/*/*/(CardCleanupSchedulingSchemaTests*)\|(CardFilePrivacyMigrationTests*)\|(CardPlatformTests*)\|(CardThreadEndpointTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-17 | S8 | CP-8 | impacted-10 | `/*/*/(CardVerificationPolicyTests*)\|(CardWorktreeMirrorBindingTests*)\|(ChannelBridgeTests*)\|(ChannelOutboundDeliveryTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| RP-18a | S8 | CP-8 | impacted-rp-18a | `/*/*/(ChannelOutboundDurabilitySchemaTests*)\|(ChannelOutboundMigrationTests*)\|(ChannelOutboundRetentionTests*)/*` | R-1,R-2 | all listed, 0 failed | 158.3 | 3 |
| RP-18b | S8 | CP-8 | impacted-rp-18b | `/*/*/ChannelOutboundRecoveryTests*/Process_death_preserves_ownership_at_each_boundary*` | R-1,R-2 | all listed, 0 failed | 247.2 | 4 |
| RP-18c | S8 | CP-8 | impacted-rp-18c | `/*/*/ChannelOutboundRecoveryTests*/(Recovery_preserves_settled_worker_and_publication_boundaries*)\|(Running_converter_reconciles_after_dispatcher_death*)` | R-1,R-2 | all listed, 0 failed | 244.1 | 4 |
| RP-18d | S8 | CP-8 | impacted-rp-18d | `/*/*/ChannelOutboundRecoveryTests*/(Resume_held_after_conversion_returns_to_ready*)\|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)\|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)\|(Broker_ack_before_process_death_remains_uncertain_without_replay*)\|(Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not*)\|(Expired_publishing_lease_is_uncertain_until_explicit_retry*)` | R-1,R-2 | all listed, 0 failed | 135.2 | 2 |
| RP-19a | S8 | CP-8 | impacted-rp-19a | `/*/*/CheckpointSourceApprovalTests*/*` | R-1,R-2 | all listed, 0 failed | 160.6 | 3 |
| RP-19b | S8 | CP-8 | impacted-rp-19b | `/*/*/(DataRetentionServiceTests*)\|(DelegationDispatchRecoveryBoundaryTests*)/*` | R-1,R-2 | all listed, 0 failed | 163.2 | 3 |
| RP-19c | S8 | CP-8 | impacted-rp-19c | `/*/*/DispatchBaseNotificationTests*/C508*` | R-1,R-2 | all listed, 0 failed | 258.7 | 4 |
| RP-19d | S8 | CP-8 | impacted-rp-19d | `/*/*/DispatchBaseNotificationTests*/(C1349*)\|(C540*)` | R-1,R-2 | all listed, 0 failed | 136.8 | 2 |
| CP-20 | S8 | CP-8 | impacted-13 | `/*/*/(DispatchConcurrencyMigrationTests*)\|(DurableRunnerSpillReceiptTests*)\|(ExpectationSnapshotTests*)\|(GitIgnorePreviewTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-21 | S8 | CP-8 | impacted-14 | `/*/*/(HangfireLeaseDocumentationTests*)\|(HangfirePostgreSqlStorageTests*)\|(HerdrPlacementSettingsTests*)\|(HostCleanupOrchestrationTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-22 | S8 | CP-8 | impacted-15 | `/*/*/(ImpactedClassesSelectorTests*)\|(LandRequestWriteDiagnosticTests*)\|(LegacyCheckNotePublicationTests*)\|(OutboundConversionPublicCreateTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| RP-23a | S8 | CP-8 | impacted-rp-23a | `/*/*/OutputDistillationDeliveryTests*/*` | R-1,R-2 | all listed, 0 failed | 282.8 | 5 |
| RP-23b | S8 | CP-8 | impacted-rp-23b | `/*/*/(OutputDistillationApplyRaceTests*)\|(OutputDistillationMigrationTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` | R-1,R-2 | all listed, 0 failed | 132.8 | 2 |
| RP-24a | S8 | CP-8 | impacted-rp-24a | `/*/*/(PipelineDefinitionMigrationShapeTests*)\|(PipelineDefinitionMigrationTests*)\|(PoolClaudeEffortMigrationTests*)/*` | R-1,R-2 | all listed, 0 failed | undefined | 3 |
| RP-24b | S8 | CP-8 | impacted-rp-24b | `/*/*/PipelineSpeedIntegrationTests*/C1281_WiredDeliveryRecovery*` | R-1,R-2 | all listed, 0 failed | undefined | 4 |
| RP-24c | S8 | CP-8 | impacted-rp-24c | `/*/*/PipelineSpeedIntegrationTests*/(C1281_B*)\|(C1281_Admission*)\|(C1281_WiredRoundtrip*)\|(C1288*)\|(C1289*)\|(C1300*)\|(C1303*)` | R-1,R-2 | all listed, 0 failed | undefined | 3 |
| CP-25 | S8 | CP-8 | impacted-18 | `/*/*/(PostLandMutationAdmissionTests*)\|(ProjectCascadeForeignKeyTests*)\|(RoutingCandidateParseTests*)\|(RoutingPinCandidateTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-26 | S8 | CP-8 | impacted-19 | `/*/*/(RunnerDefaultMigrationTests*)\|(RunnerDefaultPlacementTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultsWireTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-27 | S8 | CP-8 | impacted-20 | `/*/*/(RunnerSlotEndpointTests*)\|(ScopeResolverAreaTests*)\|(SeatOccupancySamplerTests*)\|(SessionRunnerEventPumpTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-28 | S8 | CP-8 | impacted-21 | `/*/*/(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionTests*)\|(SessionStateWarmupTests*)\|(SpecialistExecutionIdentityTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| RP-29a | S8 | CP-8 | impacted-rp-29a | `/*/*/(StandingSpecialistRoutingMigrationTests*)\|(StaleWorkingTranscriptWatchdogTests*)/*` | R-1,R-2 | all listed, 0 failed | undefined | 4 |
| RP-29b | S8 | CP-8 | impacted-rp-29b | `/*/*/(StandingRestartEscalationTests*)\|(StandingSessionOwnershipTests*)/*` | R-1,R-2 | all listed, 0 failed | undefined | 2 |
| CP-30 | S8 | CP-8 | impacted-23 | `/*/*/(TaskPlatformDispatchTests*)\|(TaskPlatformPersistenceTests*)\|(TestDbFixtureLifecycleTests*)\|(TranscriptHotPathMigrationTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-31 | S8 | CP-8 | impacted-24 | `/*/*/(WorkerWorkspaceDefaultMigrationTests*)\|(WorkingTranscriptRecoveryTests*)\|(WorktreeBaseSelectionTests*)\|(WorktreeDefaultCallerTests*)/*` | R-1,R-2 | all listed, 0 failed | 4 | 4 |
| CP-32 | S8 | tests/Antiphon.Messaging.Tests -> bin-c1915-messaging/ | migration-messaging | `/*/*/(InboxConversationMigrationTests*)/*` | R-1 | all listed, 0 failed | 1 | 4 |
| CP-33 | S8 | CP-8 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)\|(ReleaseImpactEnvelopeTests*)\|(TestTimingOperationLabelTests*)/*` | R-2 | all listed, 0 failed | 12 | 2 |
| CP-34 | S8 | tests/Antiphon.Tests -> bin-c1915-windows/ | native-Windows-impact | `/*/*/(GrokDelegateEndToEndTests*)\|(SessionMessageQueueGrokPtyIntegrationTests*)\|(SessionMessageQueuePtyIntegrationTests*)\|(SessionTranscriptSummaryMigrationTests*)\|(AppDbContextModelTests*)/*` | R-1,R-2 | all listed, 0 failed | 5 | 10 |

At this source there are **no suffix collisions** for the table's OR operands
(partial declarations are the same class). The 24 CP-8–CP-31 rows partition the 96
main-project prospective impacted classes, four at a time; CP-32 covers the one
messaging class. CP-34 names the three separately returned Windows classes plus the new migration
and model parity witnesses.
Static validation checked all 34 rows for nine cells, unique IDs, integer floors/
estimates, same-slice numeric build references and canonical OR operands. The
importer source was inspected; no checkpoint build or execution was performed.
No row contains more than four land classes. New-class names must be checked again
when implemented. Provisional ordinary allocation is **142 minutes**, excluding
build/admission, hence the mandatory Debug gate. Together with 420 minutes authoring
and the 72-minute preliminary PC floor this is **634 minutes**, not a measured total.

### Operational acceptance after implementation and ordinary Review

1. Settle D-1 on CARD-1915 and record the exact roster-performance Debug identity and
   revised measured budgets before commissioning Code. D-3's two-read fallback is
   part of that reviewable design, not an accidental budget regression.
2. Code runs the closed rows, regeneration prerequisites, evidence-diff, and actual
   diff impact census. At completion fetch master and require
   `git merge-tree --write-tree origin/master HEAD` to succeed without merging.
   Report full pushed SHAs, source-bound CP counts and any native/qualification debt.
3. After ordinary Review and publication, apply the additive migration through the
   normal activation owner. No restart, deployment or live SQL is authorized by this
   Plan task. Enable the reader/backfill flag only on the reviewed active source;
   verify `/api/version` against that source. Log backfill progress (valid/invalid,
   batches, rows, bounded failures) as metadata, without transcript contents.
4. On the activated server, compare same-epoch diagnostics and passive
   `pg_stat_statements` deltas with matched workload/session-size buckets; do not
   reset shared production statistics. Separate valid-summary seeds from startup or
   invalid fallback windows. Target valid-seed mean <20 ms, >50x shared-block
   reduction, and no new append/lock/error regression. Existing live measurement
   script/runbook remains the owner; capture read and write costs. A prototype's
   timing is not desktop acceptance, CPU saving or activation evidence.
5. If summary maintenance is wrong or disproportionately expensive, disable the
   reader immediately through the ordinary operator path and retain transcript
   truth. If it is the trigger write cost itself, disabling the reader alone does
   not remove that cost: commission the reviewed forward trigger-removal migration.
   Do not drop tables or rewrite historical migrations. Post-land Mutation owns the
   pending controls and actual landed-SHA restoration receipts.

## What remains untouched

No changes to `LoadPinnedIdsAsync`, open-task/queue/notification pin predicates,
transcript row contents, natural uniqueness/idempotency keys, producer normalization,
stored-sequence rebasing, runner transcript transport, public routes/DTOs, user-facing
ordering, cache revisions/epochs/gates/eviction settings, receipt/delivery verdicts,
parking/seat release, provider launching, current indexes, JIT policy, whole-session
read consumers in CARD-1886 S4/S5, nightly policy pins, production data, hosts or
Docker volumes. The summary trigger observes writes without changing runtime
append/dedupe/fallback code. Existing direct SQL working-state fallbacks continue to
read transcripts. Tests/schema/setup and the new seed option are the intended scope.

The selected design adds schema, trigger maintenance, a backfill worker/setting,
and a loader fast query with a bounded whole-batch fallback. All production code,
migrations and tests remain unmodified by this Plan task.

--- next stage ---
next: decide
handoff: Review and settle D-1's additive-summary scope and D-3's bounded two-read fallback on CARD-1915; then commission the exact migration-roster performance Debug and record measured budgets. The embedded verification design can go directly to Code after those gates; no separate TestDesign is needed.
artifact: docs/superpowers/plans/2026-11-14-card-1915-seed-transcript-aggregation-plan.md
