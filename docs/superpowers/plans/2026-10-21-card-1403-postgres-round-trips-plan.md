# CARD-1403 plan: cut Postgres round trips (CARD-1401 stage A)

Date: 2026-10-21. Plan task `7aab6803` on `feat/card-task-7aab6803`, base `3ce6287715e5d4a1e30e67ed1315eacba7a4c995`.
Card: CARD-1403 (`62fe99f4-e9ea-47be-a016-e4fc9abc94e1`), board Antiphon. Parent: CARD-1401 stage A
(`docs/superpowers/plans/2026-10-21-card-1401-postgres-to-server2-plan.md`). Stage A is parallel to the
move, not a gate for it (CARD-1401 plan, line 24).

Interim cost being cut: each command pays one desktop-to-server2 round trip, 3.8 ms LAN / 6.7 ms Tailscale,
on a pooled connection (`No Reset On Close=true` is already on, CARD-0712). Pooling removes the handshake, not
the per-command trip. This plan removes commands. It does not change schema or data (card boundary), and it
does not touch query plans of the large-result statements (see D-6).

This session did not open a database session, read a connection string, change any Postgres setting, or
restart a host. It read the live server's sanitized diagnostics route twice and the code.

## Measurement

### Live counters (this session)

`GET /api/diagnostics/session-state` on the running server (`dc47eb4833519dd734ea44c67e3d6b8faa10e14c`,
process started 2026-10-21 16:27:02Z), sampled at 20:49:28Z and again 48 s later. The route exposes the
`SessionStateCommandMetrics` interceptor (`server/Infrastructure/Data/SessionStateCommandMetrics.cs`), which
counts only commands whose text starts with a `-- session-state.<path>` tag.

| Tag (call site) | Since start (4 h 22 m) | 48 s window | Rate | Call site |
|---|---|---|---|---|
| `session-state.identity` | 34,713 | 146 | ~3.0/s | `AgentSessionRuntime.PersistTranscriptCoreAsync` UUID probe (`AgentSessionRuntime.cs:1308`) |
| `session-state.fallback` | 15,967 | 82 | ~1.7/s | `TranscriptWorkingStateQuery.ReadAsync`, reached by the static `SessionMessageQueueService.IsWorkingAsync` (cache bypass) and `StaleWorkingTranscriptRecovery` |
| `session-state.pins` | 263 | 1 | ~1/min | `SessionStateLoader.LoadPinnedIdsAsync` from `SessionStateWarmupService` (1 min `PeriodicTimer`) |
| `session-state.seed` | 80 | 1 | rare | `SessionStateLoader` cache load |
| cache `ingestCalls` | 12,377 | 46 | ~1.0/s | transcript ingest batches (12 of 46 were duplicate batches) |

Live sessions: 17 then 18. Cache: 610 hits, 1 load in the window, so the CARD-0701 store is serving its own
callers; the 1.7/s fallback is callers that do not use it.

### Desktop pg_stat_statements (task 5414c8f6, carried on the card)

Reset 07:37Z, read 14:53 BST (6 h 16 m = 22,560 s).

| Statement | Calls | Rate | Mean | Attributed call site (this plan) | Class |
|---|---|---|---|---|---|
| EXISTS TranscriptEntries, one session, after a timestamp | 36,035 | 1.60/s | 7.4 ms | `BootReplyWatch.HasModelReplySinceAsync` from `BootReplyWatchdogService.EvaluateAsync` self-heal, once per live session per 10 s supervisor tick (`SupervisionSettings.TickSeconds = 10`): 16-18 sessions / 10 s = 1.6-1.8/s | poll loop, N+1 |
| Pins select (AgentSessions + 4 EXISTS) | 374 | 1/min | 1101 ms | `LoadPinnedIdsAsync`, warmup timer | scheduled job; execution-bound, not round-trip-bound |
| TranscriptEntries sequence/kind/text/timestamp | 335 + 4,461 | 0.01 + 0.2/s | 477 / 26.5 ms | receipt and boot-turn row reads | row-bytes-bound |
| TranscriptEntries text LIKE | 198 | rare | 765 ms | receipt scans | execution-bound |

Attribution of the EXISTS statement is by shape and rate, not by query id: its predicate (session, `Timestamp
?? CreatedAt >= @clock`, model-reply kinds) is `HasModelReplySinceAsync`, and the rate matches live sessions
per tick. S0 makes the attribution exact on the live server (D-1).

### Wire tax at 6.7 ms per command

| Hot set | Commands/s | Wire wait/s | Slice | Saved commands/s |
|---|---|---|---|---|
| Boot-reply sweep EXISTS | 1.6-1.8 | 11-12 ms | S1 | ~1.5 (N per tick becomes 1) |
| Static working reads (fallback tag) | ~1.7 | 11 ms | S2 | up to ~1.7, after S0 attributes it |
| Ingest prelude (session, identity, max sequence) | ~3.0 identity + 2 × 1.0 | ~33 ms | S3 | ~2.0 |
| Total of the measured hot set | ~8.3 | ~56 ms/s | | ~5 |

These are the measured tagged sites plus the attributed EXISTS. The server's total command rate is unknown:
most queries carry no tag. S0 adds the untagged total so the target below can be checked.

## Ground truth

| The card or parent plan assumes | The code / measurement shows |
|---|---|
| The per-call tax is 72-127 ms | That is server2 to desktop. The interim direction is 3.8 / 6.7 ms (card, CARD-1401 plan) |
| The 36k-call EXISTS is "the per-poll hot path" without a named site | `BootReplyWatchdogService` self-heal runs `HasModelReplySinceAsync` for every live session with null watch columns, every 10 s tick. Healthy sessions keep null columns forever (comment at `BootReplyWatchdogService.cs:133-137`), so the cost is one command per live session per tick |
| Pins returns ~169k rows per call | `pg_stat_statements.rows` is cumulative. 169,000 / 374 = ~452 session ids per call. The 1101 ms mean is execution of the OR-of-EXISTS predicate, not row transfer and not round trips. The same caveat applies to the "~18k rows" and "~2.5k rows" TranscriptEntries figures: per-call rows must be re-derived as rows/calls before anyone sizes a transfer |
| CARD-0701 caches working state, so working reads do not hit Postgres | Only callers that go through `SessionStateStore` (`SessionMessageQueueService.ReadWorkingAsync`, `SessionMessageQueueService.cs:5401`). The static `SessionMessageQueueService.IsWorkingAsync(db, ...)` always queries: callers in `DelegateCheckProbe:338`, `SpecialistRequestService.Qualification:40,133`, `HerdrStatusCorroborationService:123,129`, `AgentSessionService:1162,1188`, `AgentSessionRuntime:1259` |
| A connection-string switch can batch | No (CARD-1401 D-8). Batching is call-site code. `Max Auto Prepare` stays 0 |
| There is a query-count guard | There is a tag counter for five session-state paths only (`SessionStateCommandMetrics`). No test asserts a command budget for a hot path |
| Transcript ingest is one write | A non-empty batch issues, before any insert: a session `StartedAt` read, one identity read per 512 UUIDs, a sequence read when rows lack UUIDs, and a `MAX(Sequence)` read (`AgentSessionRuntime.cs:1290-1331`). Duplicate batches pay the same prelude |

## Decisions

### D-1. Measure through the server's own interceptor, not the live database

Generalize the existing interceptor into a call-site counter: every command increments a bucket keyed by its
leading `-- <tag>` line (an allowlisted prefix set, `session-state.*` and new `rt.*` tags), and everything
else increments `untagged`. The diagnostics route gains `dbCommands` (total, untagged, per tag) beside the
unchanged `efReadAttempts`. Rates come from two reads of the route, as in this plan.

Why: the brief forbids changing the live database's settings, and `pg_stat_statements` resets are shared
state. The server counter needs no database access, is already deployed in shape, and keeps the
"never retain SQL or parameters" rule (only the tag string, from a fixed allowlist, becomes a key).

Rejected: EF `LogTo`/Npgsql command logging in production (volume, and SQL text in logs); a
`pg_stat_statements` reset on the desktop (shared state, and the desktop sample already exists); a scratch
restore with replayed load (no replay exists; synthetic load would not reproduce the poll mix).

### D-2. The regression guard is a deterministic command budget, not a timer

A test helper, `QueryBudget`, is a `DbCommandInterceptor` registered on a test `AppDbContext` that counts
commands (optionally only those with a given tag) inside a scope and fails with the list of tags seen when
the count exceeds the budget. Each slice adds one budget test for its hot path at a fixed fixture size, so a
change that reintroduces N+1 fails with a count, not a flaky elapsed time. The runtime counter from D-1 is
the production-side signal.

Rejected: wall-clock assertions under injected latency as the guard (flaky on shared runners); a global
"queries per request" middleware limit (would fail real requests, and most routes are not hot).

### D-3. Latency injection is evidence, the assertion is the count

The card asks for a 100 ms latency check. `QueryBudget` takes an optional per-command delay. One test per
slice runs its hot path with 100 ms injected and asserts the command count; it records elapsed time in the
test output only. Expected: the boot sweep over 18 sessions falls from ~1.9 s of injected wait to ~0.2 s.

Rejected: `tc netem` on a host link (needs root and touches shared networking); toxiproxy in front of the
test Postgres (new infrastructure for one number).

### D-4. Batch, do not cache, the boot-reply existence check

S1 replaces the per-session EXISTS in the sweep's self-heal with one set-based query for all unarmed live
sessions. The single-session `HasModelReplySinceAsync` becomes a one-element call of the batch form, so there
is one predicate source and the Grok rules-turn exclusion cannot drift between them.

Why not cache "answered since clock": the predicate excludes rows inside a Grok rules turn
(`SessionQueuedMessages.RulesPromptSequence`/`RulesTurnEndSequence`). Those columns are stamped by another
writer, so a row that qualified on one tick can be excluded on a later one. A monotonic memo is not provably
correct; a batch has the same answer as today by construction.

### D-5. Route static working reads through the store only where no transaction is relied on

S2 changes `SessionMessageQueueService.IsWorkingAsync(db, id, ct)` callers to the `SessionStateStore` verdict
when the store is enabled, the same switch `ReadWorkingAsync` already uses. A caller that reads inside a
transaction it depends on for a fresh view (for example after its own transcript write) keeps the database
read; Code lists each caller with its choice in the commit message. The CARD-0701 store is already the
production verdict for the queue, so this is not a new correctness claim. S2 starts only after S0 shows which
callers produce the 1.7/s (D-1); a caller under 0.05/s is left alone.

### D-6. Out of scope: execution-bound and row-bytes-bound statements

The pins select (1101 ms, once a minute), the TranscriptEntries text reads and the text LIKE scans cost
execution time and bytes, not round trips. Folding or caching them saves under 0.3 commands/s combined.
They need `EXPLAIN (ANALYZE, BUFFERS)` on a scratch restore (CARD-1401 C1 provides one) and index or
projection work, which may cross the card's no-schema boundary. Recommendation: file one card for the pins
predicate and one for transcript row projection after S0 lands; not part of this plan's slices.

### D-7. No prepared statements, multiplexing or connection-string change

Per CARD-1401 D-8. `Max Auto Prepare` would save a Parse trip per statement but confounds the cutover
rollback trigger; revisit after the move.

### D-8. Target

Steady state at the S0 baseline load (about 18 live sessions, idle): the measured hot set falls from ~8.3 to
~3.3 commands/s, and the server's total command rate (S0's `dbCommands.total`) falls by at least that 5/s.
Per hot path: boot sweep = 1 live-session load + at most 1 existence command per tick, independent of
session count; non-duplicate ingest batch prelude = 1 command for up to 512 UUIDs. The main read routes
(agent list, attention feed) get budgets only after S0 shows them in the top 10 by rate.

Checklist (stage rule): this plan adds or changes no session that waits for input. Nothing here parks,
releases or stops a session.

## Slices

Ordered by commands/s saved per unit of risk. Each slice is its own commit group; the failing budget test is
committed before its fix (git-common). Platform Any; tests need the shared test Postgres, so the lane is
server2 (runner default `server2`, `GET /api/runner-defaults` revision 2). No `-Runner` or `-Platform` pin.

### S0. Call-site counter and test budget helper (enables attribution and the guard)

Files:
- `server/Infrastructure/Data/SessionStateCommandMetrics.cs` -> keep the class and its `Snapshot()` keys
  unchanged; add a sibling `DbCommandMetrics` (or extend in place) counting every executing command: reader,
  scalar and non-query, sync and async, by allowlisted leading tag plus `total` and `untagged`.
- `server/Program.cs` (register beside the existing interceptor, line ~113).
- `server/Api/Endpoints/DiagnosticsEndpoints.cs` (add `dbCommands`; no SQL, no parameters).
- Tag the hot queries this plan touches: `rt.boot-reply.exists` on `HasModelReplySinceAsync`,
  `rt.ingest.session`, `rt.ingest.max-sequence`, `rt.ingest.sequence` in `PersistTranscriptCoreAsync`. The
  static `IsWorkingAsync` callers share one query, so `TranscriptWorkingStateQuery.ReadAsync` takes an
  optional caller tag suffix (`session-state.fallback.<caller>`); the existing `fallback` key in
  `efReadAttempts` keeps counting all of them by prefix.
- `tests/Antiphon.Tests/TestHelpers/QueryBudget.cs` (new): counting interceptor, optional tag filter, optional
  per-command delay (D-3).

Tests (new class `tests/Antiphon.Tests/Infrastructure/DbCommandMetricsTests.cs`):
- `C1403_counts_tagged_untagged_and_non_query_commands` — runs a tagged read, an untagged read, an
  `ExecuteSql` and a `SaveChanges` against the test database and asserts each bucket. Red witness: before the
  implementation the `untagged` and non-query counts are absent (the existing interceptor only overrides
  `ReaderExecuting*`).
- `C1403_query_budget_fails_when_exceeded` — positive control: a two-command block under a budget of one
  throws and names both tags.

Saved: none. Risk: low (counting only; existing keys unchanged).

After landing, the orchestrator reads `/api/diagnostics/session-state` twice, 60 s apart, at normal load and
records `dbCommands` rates on the card. That reading decides S2's caller list (D-5) and confirms S1's
attribution. It is a read of a sanitized route, not a database session.

### S1. Boot-reply sweep: one existence command per tick

Files:
- `server/Application/Services/BootReplyWatch.cs`: add `HasModelReplySinceBatchAsync(db,
  IReadOnlyCollection<(Guid SessionId, DateTime Clock)>, ct)` returning the set of answered ids, in one
  command (EF over the session ids, or `unnest(@ids, @clocks)` raw SQL; Code chooses, but the predicate,
  including the rules-turn exclusion and the five kinds, exists once and the single-session method calls the
  batch with one pair).
- `server/Application/Services/BootReplyWatchdogService.cs`: in `SweepAsync`, compute the unarmed sessions
  (`BootPromptSequence is null || BootReplyDueAt is null`) with their `LaunchClock`, call the batch once, and
  pass the answer into `EvaluateAsync` instead of its per-session EXISTS. Per-session error isolation stays:
  a batch failure falls back to today's per-session loop for that tick, logged once.
- `StandingBootAttentionProjection` and `StandingBootWatchObservation` loops keep the single form in this
  slice; S0's counter shows whether their rate earns a follow-up.

Tests (in `tests/Antiphon.Tests/Application/BootReplyWatchdogTests.cs`):
- `C1403_sweep_checks_unarmed_sessions_in_one_command` — five live, answered, unarmed sessions; `QueryBudget`
  filtered to `rt.boot-reply.exists` asserts exactly 1 such command per `SweepAsync`. Red at base: 5. Also runs
  with the 100 ms delay and logs elapsed (D-3).
- `C1403_batch_reply_check_matches_single_session_check` — one fixture with: a reply after the clock, a reply
  only before the clock, a reply only inside a Grok rules turn, a `TurnEnd` only, a prompt only; asserts the
  batch set equals the single-session answer for every session.
- Existing boot-reply behaviour: `BootReplyWatchdogTests` standing-boot cases named in CP-3.

Saved: ~1.5 commands/s at 16-18 live sessions; grows with session count. Risk: low (read-only, same
predicate, fallback on failure).

### S2. Static working reads through the session-state store

Starts after the S0 reading. Files: the `IsWorkingAsync(db, ...)` callers listed in Ground truth that S0
shows at or above 0.05/s, each switched to `SessionStateStore.IsWorkingAsync` when `Enabled`, except a caller
inside a transaction it depends on (D-5). `HerdrStatusCorroborationService`'s pull-before-raise re-read keeps
a database read after `CatchUpTranscriptAsync` only if the store has not been published by that catch-up;
the runtime publishes on commit (`PublishCommittedAsync`), so the store read is current. Code confirms that in
the test below, not by assertion in prose.

Tests (new class `tests/Antiphon.Tests/Application/StaticWorkingReadCacheTests.cs`):
- `C1403_static_working_read_uses_the_session_state_cache` — with the store enabled and warm, the converted
  caller's entry point issues 0 `session-state.fallback` commands. Red at base: 1 per call.
- `C1403_working_read_after_ingest_sees_the_committed_turn_end` — ingest a `TurnEnd` through the runtime,
  then the converted read returns not-working with 0 fallback commands.

Saved: up to ~1.7 commands/s. Risk: medium-low (shared verdict already in production; per-caller review).

### S3. Transcript ingest prelude in one command

Files: `server/Application/Services/AgentSessionRuntime.cs` `PersistTranscriptCoreAsync`: replace the
session `StartedAt` read, the identity read(s), the null-UUID sequence read and the `MAX(Sequence)` read with
one command (an `NpgsqlBatch` on the context's connection, or one SQL returning StartedAt, max sequence and
the matching `(Uuid, Kind)` and sequences). The 512-UUID chunking stays for larger inputs (one extra command
per further chunk). Everything after the prelude (`AdmitActionsAsync`, recovery lock and fence, save,
individual fallback) is unchanged, and the prelude stays before the recovery transaction as today, so
ordering and the ingest gate are untouched.

Tests (in `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.Persist.cs`, which already registers
`SessionStateCommandMetrics`):
- `C1403_persist_prelude_is_one_command` — a batch of 10 events with UUIDs plus 2 without, on a session with
  prior rows; `QueryBudget` filtered to `rt.ingest.*` and `session-state.identity` asserts 1 command. Red at
  base: 4.
- `C1403_persist_prelude_keeps_dedup_and_rebase` — the same batch replayed, and a relaunched tailer with
  restarted sequences: 0 duplicate rows and sequences rebased past the stored max, exactly as today.

Saved: ~2 commands/s. Risk: medium (ingest path; mitigated by the dedup/rebase test and unchanged ordering).

## Verification design

| V | Claim | Test | Red at base | Green |
|---|---|---|---|---|
| V-1 | The counter sees every command kind and the untagged total | `DbCommandMetricsTests/C1403_counts_tagged_untagged_and_non_query_commands` | non-query and untagged buckets missing | all buckets equal the fixture's commands |
| V-2 | The budget guard can fail | `DbCommandMetricsTests/C1403_query_budget_fails_when_exceeded` | n/a (positive control of the guard itself) | throws, message names both tags |
| V-3 | Boot sweep issues one existence command for N sessions | `BootReplyWatchdogTests/C1403_sweep_checks_unarmed_sessions_in_one_command` | 5 commands | 1 command |
| V-4 | Batch and single forms agree, including the rules-turn exclusion | `BootReplyWatchdogTests/C1403_batch_reply_check_matches_single_session_check` | does not compile without the batch form, so it is not a red row; V-3 is S1's red | equal sets |
| V-5 | Converted working reads use the cache | `StaticWorkingReadCacheTests/C1403_static_working_read_uses_the_session_state_cache` | 1 fallback command | 0 |
| V-6 | Cache read after ingest is current | `StaticWorkingReadCacheTests/C1403_working_read_after_ingest_sees_the_committed_turn_end` | n/a | not-working, 0 fallback |
| V-7 | Ingest prelude is one command | `AgentSessionRuntimeTests/C1403_persist_prelude_is_one_command` | 4 commands | 1 |
| V-8 | Dedup and sequence rebase unchanged | `AgentSessionRuntimeTests/C1403_persist_prelude_keeps_dedup_and_rebase` | n/a | 0 duplicates, rebased |

Minimal testing per the brief: one red witness and one narrow green per slice; no whole-assembly or
whole-class runs. Red rows run at the slice's test commit (before the fix) and are expected to fail on the
count assertion; a build error or zero executed tests is not red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S0 test commit | `tests/Antiphon.Tests -> bin-c1403-s0red/` | c1403-s0-red | `/*/Antiphon.Tests.Infrastructure/DbCommandMetricsTests/C1403_counts_tagged_untagged_and_non_query_commands` | V-1 witness | 1 executed, failed on the untagged/non-query count assertion | 1 | 6 |
| CP-2 | S0 | `tests/Antiphon.Tests -> bin-c1403-s0/` | c1403-s0 | `/*/Antiphon.Tests.Infrastructure/DbCommandMetricsTests/C1403_*` | V-1, V-2 | 2 passed, 0 failed/skipped | 2 | 6 |
| CP-3 | S1 test commit | `tests/Antiphon.Tests -> bin-c1403-s1red/` | c1403-s1-red | `/*/Antiphon.Tests.Application/BootReplyWatchdogTests/C1403_sweep_checks_unarmed_sessions_in_one_command` | V-3 witness | 1 executed, failed: 5 commands, budget 1 | 1 | 6 |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1403-s1/` | c1403-s1 | `/*/Antiphon.Tests.Application/BootReplyWatchdogTests/(C1403_*)\|(an_unanswered_standing_boot_raises_once_and_keeps_its_watch_for_escalation*)\|(a_standing_boot_stall_is_detected_without_stopping_or_driving_the_restart_ladder*)` | V-3, V-4, existing boot-watch behaviour | 4 passed, 0 failed/skipped | 4 | 8 |
| CP-5 | S2 test commit | `tests/Antiphon.Tests -> bin-c1403-s2red/` | c1403-s2-red | `/*/Antiphon.Tests.Application/StaticWorkingReadCacheTests/C1403_static_working_read_uses_the_session_state_cache` | V-5 witness | 1 executed, failed: 1 fallback command, budget 0 | 1 | 6 |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1403-s2/` | c1403-s2 | `/*/Antiphon.Tests.Application/StaticWorkingReadCacheTests/C1403_*` | V-5, V-6 | 2 passed, 0 failed/skipped | 2 | 6 |
| CP-7 | S3 test commit | `tests/Antiphon.Tests -> bin-c1403-s3red/` | c1403-s3-red | `/*/Antiphon.Tests.Application/AgentSessionRuntimeTests/C1403_persist_prelude_is_one_command` | V-7 witness | 1 executed, failed: 4 commands, budget 1 | 1 | 6 |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c1403-s3/` | c1403-s3 | `/*/Antiphon.Tests.Application/AgentSessionRuntimeTests/C1403_*` | V-7, V-8 | 2 passed, 0 failed/skipped | 2 | 8 |

If S2's caller list from the S0 reading is empty (every static caller under 0.05/s), CP-5 and CP-6 are
dropped and S2 is recorded as not needed, with the reading.

### Cost

Ordinary floor about 52 minutes of checkpoint time across four slices, each run through the checkpoint tool
(`dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this file>`) per committed slice group, on
the server2 lane.

## Rollout and observation

Master first. No RC backport: none of these fixes is a bug fix of shipped behaviour (card boundary). After
each slice is active on the desktop server (confirm `GET /api/version` SHA), read the diagnostics route twice
60 s apart and record `dbCommands.total` and the slice's tag rate on the card against D-8.
