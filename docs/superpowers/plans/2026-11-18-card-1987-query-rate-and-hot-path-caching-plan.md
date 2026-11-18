# CARD-1987 plan: measure Postgres queries per second by operation, then cut the hottest round trips

Date: 2026-11-18. Plan task `fa6c783e` (Opus) on `feat/card-task-fa6c783e`.
Card: CARD-1987 (`88d47f5c-37a8-4267-b33a-020f6981e365`), board Antiphon, High/Soon.
Source read: `origin/master` **e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4** (also the branch base).

This session ran no SQL against any database, changed no setting and restarted nothing. It read the
code, the cards (CARD-1987, 1403, 1889, 1972, 0718), the CARD-1403, 1889 and 1969 plans, the
2026-11-17 transcript-attribution and Postgres-move notes, `GET /api/runner-defaults` and
`GET /api/session-runners`. All rates below come from the orchestrator's baseline (B-0). Anything
not read in code or measured is marked **UNOBSERVED**.

## 0. Baseline and what to re-measure first

**B-0 (orchestrator, desktop `pg_stat_statements`, 647 s, 2026-11-17 09:24:53Z-09:35:40Z, before
CARD-1888 and CARD-1969 were active):** 108,400 statements = **167.5 qps** over 455 distinct
statements. The server's own `dbCommands` counter (CARD-1403 S0) read 143.6-176.6 commands/s on
the same day, and 94.6 % of them carried no call-site tag (move-status note, section 2).

The orchestrator will post a post-fix pair of snapshots (AppHost on `rc/2026-11-27` since 01:16Z,
with CARD-1888 and CARD-1969 S1/S2). Read these first, in this order. Each one decides a slice:

| M | What to read from the fresh pair (read-only, same two-snapshot method) | Decides |
|---|---|---|
| M-1 | Total qps, and calls/s for the ten B-0 statements below, by `queryid`. Is `session-state.identity` (B-0 #6) now under 1/s (CARD-1888)? | The denominator for the 30 % target (D-10) |
| M-2 | The **full** text (not truncated) of B-0 #2, the full-row `AgentSessions` select. Its `WHERE`, `LIMIT` and any join say which call sites it can be | S3 candidate list |
| M-3 | Read-only, on the desktop DB: `SELECT "State","Kind",("QueueMessageId" IS NULL) AS q_null,("ParentSessionId" IS NULL) AS p_null,count(*) FROM "AgentTaskLandNotifications" WHERE "State" NOT IN (3,4,6,7) GROUP BY 1,2,3,4` (Confirmed=3, NotRequired=4, Canceled=6, LegacyUnverified=7; `LandingEnums.cs:22`) | Whether S4 exists (section 5, S3-S5) |
| M-4 | `ROLLBACK` calls/s and the `RELEASE SAVEPOINT`/`ROLLBACK TO SAVEPOINT` split | S5 scope (section 5, S3-S5) |

## 1. Ground truth

| The card or brief assumes | The code at e229fb4d3 shows |
|---|---|
| There is no queries-per-second number anywhere | The server already counts **every** command it sends (`DbCommandMetrics`, `server/Infrastructure/Data/DbCommandMetrics.cs:16-142`, CARD-1403 S0), by allowlisted leading tag (`session-state.`, `rt.`), plus `total` and `untagged`. It is exposed as `dbCommands` on `GET /api/diagnostics/session-state` (`DiagnosticsEndpoints.cs:16-35`). It has no history, no rate (two reads by hand), and no DB time. It does not see transaction control (`BEGIN`, `COMMIT`, `SAVEPOINT`), because those go through `DbTransaction`, not `DbCommandInterceptor` |
| Statement #1 (`RunnerId, RunnerStoreId, RunnerCwd` by `Id`, 18.1/s) is "the AgentSessionRuntime runner lookup" | Its only exact projection is `PhoneHomeRunnerDirectory.GetBindingAsync` (`PhoneHomeRunnerDirectory.cs:203-217`). `AgentTaskDispatcher.cs:6886` also selects `DefinitionName`, so it is a different statement. The hot caller is `RoutingSessionRunnerClient.Route` (`RoutingSessionRunnerClient.cs:123-133`), which reads the binding from the database on **every** per-session runner call (get, snapshot, buffer, transcript, observe-seat, observe-compaction, input, kill...). The recovery pump already caches its own lookups per connection (`PhoneHomeRecoveryPump.cs:912-931`, CARD-0679 D-3). The per-caller split of the 18.1/s is **UNOBSERVED** (the statement is untagged) |
| The binding can change while a session lives | No writer changes it. It is set on insert (`AgentTaskDispatcher.cs:6378`, `:6918`, `AgentControlService.cs:801`). The check constraint makes it all-null or all-set (`AppDbContext.cs:1629-1631`). The CARD-0679 D-3 comment says "persisted before its Launch and never changes". The only other change is deletion by retention (`DataRetentionService.cs:290`), for sessions idle 7 days |
| `UPDATE AgentSessions SET LastSeenAt` (4.0/s) is a heartbeat | It is `AgentSessionRuntime.RecordActivityAsync` (`AgentSessionRuntime.cs:2661-2690`), on every output chunk (`:181-182`) and already throttled to once per session per second (`ActivityWriteMinIntervalMs` = 1,000, `AgentSessionSettings.cs:83-89`; `ClaimActivityWrite` `:2701-2720`). One claimed write costs **three** commands: a tracked full-row `AgentSessions` load (that is part of B-0 #2), the streaming `RunAttempts` load (that is B-0 #4: `r.Id, r.AgentSessionId, r.AttemptNumber, r.BoardWorkflowDefinitionId...`, 2,591 calls, the same count as the `UPDATE`), then the save. When a streaming attempt exists the save has two `UPDATE`s, so EF also sends `BEGIN`/`COMMIT` |
| B-0 #2 (full-row `AgentSessions`, 13.8/s) is one call site | It is the shape of any tracked or untracked `AgentSessions.FirstOrDefault/Single/Find` without a projection. 4.0/s of it is `RecordActivityAsync` (same 2,591 count). The other ~9.8/s is **UNOBSERVED** (M-2) |
| Land reconcile needs a "skip when nothing changed" version stamp | It has one. CARD-1769's rescan park (`AgentTaskLandNotificationHostedService.cs:360-420`) skips an `AwaitingReceipt` note while the session status/start/max-prompt-sequence and the queue row's status/attempts/verdict/canceled-at are unchanged. B-0 still shows ~2.9 reconciles/s (four statements of ~1,900 calls each). The due page returns every non-terminal note with a `QueueMessageId`, whatever its `NextAttemptAt` (`LandNotificationReconcileQueries.cs:90-102`), and only `AwaitingReceipt` notes can park. Which states the ~15 notes reconciled per 5 s pass are in is **UNOBSERVED** (M-3). Each reconcile writes `note.ConcurrencyToken = Guid.NewGuid()` before it decides anything (`AgentTaskLandNotificationService.cs:98`): that is B-0 #9 |
| B-0 #8 "AgentTasks Body/CompletionDeliveryJson" and "AgentTaskSessions" | No tagged compiled query reads `AgentTasks`' body. `Body` and `CompletionDeliveryJson` are `AgentTaskLandNotification` columns, loaded whole by `NoteById` (`LandNotificationReconcileQueries.cs:36-39`). The `AgentSessions` status/start select is `SessionScanById` (`:41-46`). The statement called "AgentTaskSessions" is **UNOBSERVED** (the truncated text cannot tell) |
| `session-state.identity` (3.9/s, 629 rows/call) is a cache candidate | It is the transcript ingest identity chunk (`AgentSessionRuntime.cs:1449-1457`, `TranscriptIngestPrelude.cs`). CARD-1888 S2+S3 (landed `3c3137f04`, `0c0a65858`) target exactly this. This plan does not touch it; M-1 re-measures it |
| EF `BEGIN`/`SAVEPOINT` chatter can be turned off for read-only paths | A read-only single query never opens a transaction in EF Core 9 (9.0.21). `BEGIN`/`COMMIT` come from explicit `BeginTransactionAsync` (214 call lines in `server/`, migrations excluded) and from `SaveChanges` with two or more statements. `SAVEPOINT`/`RELEASE` come only from `SaveChanges` inside an explicit transaction (EF auto-savepoints). Turning savepoints off is unsafe where code catches a failed save and keeps using the transaction (for example the 23505 fallbacks). No caller can be named from `pg_stat_statements`, because these statements carry no text. Attribution is **UNOBSERVED** |
| Delivery evidence could be cached by accident | The binding cache (S1) caches routing only, never an answer from a runner. The activity change (S2) writes the same two columns. Neither reads a transcript, a queue verdict, a receipt or a TurnEnd |
| There is a second server process writing the same database | **UNOBSERVED.** The desktop AppHost server is the production writer. The server2 staging stack uses its own Postgres (`docker-compose.server2-staging-server.yml`). S1 does not depend on the answer, because no process writes the binding after insert |
| Production Postgres is 16.14 | The repo pins only the major: dev compose `postgres:16`, server2 compose takes a digest from an untracked env file, `TestDbFixture` uses `postgres:16-alpine` **without** `shared_preload_libraries`. The production minor is from the brief (not re-read) |

## 2. Decisions

- **D-1. Two measurement tools, one SQL.** A read-only script for an operator or orchestrator
  (`scripts/pg-qps.ps1`) and an in-process sampler (`DbRateSampler`) both run the same snapshot
  query, kept in one file (`server/Infrastructure/Diagnostics/PgStatStatementsSnapshot.sql`,
  embedded in the server and read from the checkout by the script). One source means the two can
  never count different things. Rejected: a script-only tool (the number would still be taken by
  hand), and a sampler-only tool (unusable when the server is down or mid-move).
- **D-2. The sampler is a bounded in-memory `BackgroundService`, not a Hangfire job.** It wakes
  every 60 s, reads `pg_stat_statements(false)` (counters only, no text file read) for the current
  database, diffs it against the previous read, and keeps 1,440 minute records (24 h) in a ring.
  It writes nothing to the database. This follows CARD-0718 (no per-sample DB writes). Rejected:
  Hangfire (its own storage queries cost more than the sample, and it would persist rows); a
  database table of samples (writes to measure reads).
- **D-3. Text is fetched only for new query ids, and it is redacted.** On a new `queryid` the
  sampler makes one extra `pg_stat_statements(true)` read, keeps a label of at most 120 characters,
  and caches it (cap 2,000 ids). `pg_stat_statements` normalises constants to `$n`, so plannable
  statements carry no parameter values. **Utility statements are not normalised.** A label that
  matches `(?i)\b(password|alter\s+(role|user)|create\s+(role|user)|connection|secret|token)\b` is
  replaced by `redacted-utility`. The script applies the same rule. The label is the leading
  `-- <tag>` when it is `[a-z0-9.-]{1,64}`, else the normalised text. Transaction control
  (`BEGIN`, `COMMIT`, `ROLLBACK`, `SAVEPOINT`, `RELEASE`) is grouped under `tx.<verb>`.
- **D-4. Surface: `GET /api/diagnostics/db-rate`.** Next to the existing `/api/diagnostics/session-state`
  (which keeps its shape). Query `minutes` (1-1,440, default 60). The body has `rollups` (1, 5, 15,
  60 min: qps, DB ms/s, rows/s), a per-minute series, `top` (ten by calls, five by total time, with
  label and per-second rates), `byTag`, `gaps` (reason per missing minute: `unavailable`,
  `extension-missing`, `stats-reset`, `dealloc`) and the in-process `dbCommands` total/untagged rate
  for the same minutes. The host-stats surface (`/api/hosts`) stays the runner's; the database is not
  a host. Rejected: adding fields to `session-state` (an unrelated contract), and a SignalR stream.
- **D-5. One attempt per tick, a hard command timeout, no retry.** A failed tick records a gap.
  This matches `docs/resilience.md` ("What stays a single attempt"). The tick's command is tagged
  `-- rt.diag.pgss` so `dbCommands` counts it. Command timeout 5 s.
- **D-6. Cost.** One command per minute is 0.017 qps, 0.01 % of 167.5 qps. The view holds at most
  `pg_stat_statements.max` = 5,000 entries (both composes); the brief cites about 1,700. At about
  50 bytes per counter row that is about 85 KB per read, about 1.4 KB/s. Its execution time is
  **UNOBSERVED** here; the witness records it (W-5). Memory: 1,440 records of about 1 KB plus 2,000
  labels of at most 120 chars is under 3 MB.
- **D-7. Attribute the untagged 94.6 % in-process by sampled call site (M3).** A new
  `DbCallSiteMetrics` interceptor (command **and** transaction interceptor) counts every
  `BEGIN`/`COMMIT`/`ROLLBACK`/savepoint-create/savepoint-release, and on one command in 64 (and one
  transaction event in 16) captures `new StackTrace(false)` and records the first frame in an
  `Antiphon.` namespace outside `Infrastructure.Data`, demangled from the async state machine
  (`<RecordActivityAsync>d__12.MoveNext` -> `AgentSessionRuntime.RecordActivityAsync`). Keys are code
  identifiers, never data. Cap 256 keys, then `overflow`. A frame lost to an async hop is `unknown`,
  and its share is reported, so the coverage is visible. Cost at 167 qps: about 2.6 stack captures/s,
  well under 1 ms of CPU per second (per-capture cost **UNOBSERVED**, W-9 records it). Why: tags
  were added one call site at a time for a year and still cover 5.4 %. Rejected: `TagWith` on every
  query (hundreds of edits), and EF `LogTo` (SQL text in logs). Operator decision Q-2.
- **D-8. Cache only what no one writes, and only on the routing hot path (S1).** The binding cache
  sits in `RoutingSessionRunnerClient.Route`, not in `PhoneHomeRunnerDirectory.GetBindingAsync`.
  `TaskParkPublicationService` re-reads the binding to compare it (`TaskParkPublicationService.cs:82`)
  and the recovery pump and `EnsureInputTransportAvailableAsync` keep their reads, so their
  semantics do not change. `Missing` is never cached (a row can commit just after a first call).
  `Local` and `Remote` are cached for 60 s per session id, cap 4,096 entries. `Resolve(runnerId)`
  still runs on every call, so runner availability, drain and socket checks stay live. Staleness:
  none for a live session (immutable binding). The only stale case is a session deleted by
  retention inside the 60 s, which then routes to its old runner instead of throwing
  `NotFoundException`; retention deletes only sessions idle 7 days, which no poll reaches.
- **D-9. Single-statement activity write (S2).** `RecordActivityAsync` becomes one tagged
  statement (`-- rt.activity.write`): a data-modifying CTE that sets `AgentSessions.LastSeenAt` by id
  and the newest streaming attempt's `LastEventAt` (same predicate as today:
  `AgentSessionId = @id AND Phase = StreamingTurn AND CompletedAt IS NULL ORDER BY AttemptNumber
  DESC LIMIT 1`). One statement is atomic, so no `BEGIN`/`COMMIT`. Same columns, same value, same
  throttle. Neither entity has a concurrency token and `AppDbContext.SaveChangesAsync`'s hooks
  touch neither (`AppDbContext.cs:147-190`), so nothing that the tracked save did is lost.
  Rejected: raising `ActivityWriteMinIntervalMs` to 5 s in the same slice (a behaviour change; Q-3).
- **D-10. Target.** At the B-0 mix, S1 + S2 remove about 25.6 qps (15 %). The 30 % target needs
  S3-S5, which are gated on M-2..M-4 and on M3's call sites. This plan does not claim them. After
  S2 is active, the orchestrator re-reads M-1 and the db-rate route; S3+ go to a plan amendment
  with measured call sites.
- **D-11. No migration, no schema, no setting changed by this plan.** New settings carry defaults in
  code (`DbRateSampler:Enabled` = true, `IntervalSeconds` = 60, `RetentionMinutes` = 1,440). Q-1.

## 3. Delivery-evidence guard (never cached, never coalesced)

Nothing in this plan may cache or delay: transcript rows and anything derived from them
(UserPrompt receipts, TurnEnd, prompt spans, working-state fallbacks); `SessionQueuedMessages`
status, attempts, verdicts; land-notification state and its `ConcurrencyToken` claim; runner
answers (get, snapshot, buffer, transcript, absence evidence); session `Status`/`StartedAt`
(generation fences); `RunAttempts.Phase`. S1 caches only the three immutable routing columns. S2
changes how two timestamps are written, not when or to what. S4/S5 (if they happen) must name the
reader of every skipped write. Transcript-confirmed UserPrompt remains the delivery verdict
(`docs/session-runtime-invariants.md`).

## 4. Slice 1: measure

### M1. `scripts/pg-qps.ps1` (read-only reporter)

- `#requires -Version 5.1`, ASCII only, no PowerShell 7 syntax (`??`, `?.`, `? :`, `&&`/`||`
  chains, `-AsHashtable`, `ForEach-Object -Parallel`, `ProcessStartInfo.ArgumentList`).
- Parameters: `-Seconds` (10-3,600, default 60); exactly one source:
  `-Container <name>` (default `antiphon-postgres`; `docker exec -i <c> psql -X -A -t -U <user> -d <db>`),
  or `-ConnectionEnv <VARNAME>` (the name of an environment variable holding an Npgsql or libpq
  connection string, for example one exported from the standard secret path in
  `docs/agent-credentials.md`). The script parses it in memory and passes it to a local `psql`
  only as `PGHOST`/`PGPORT`/`PGUSER`/`PGDATABASE`/`PGPASSWORD` child environment variables, never
  on a command line and never printed; any error text is cut to a fixed message.
  `-Database` (default `antiphon`), `-User` (default `antiphon`), `-Top` (default 20), `-Json`.
- Reads `pg_stat_statements_info` (`dealloc`, `stats_reset`) and the shared snapshot SQL twice,
  `-Seconds` apart, filtered to the current database. Never calls `pg_stat_statements_reset`.
- Output: a header (window seconds, total qps, DB ms/s, rows/s); per operation (tag or `tx.<verb>`
  or `untagged`) qps, ms/s, rows/s, calls, mean ms; top `-Top` statements by calls and by total
  time with the redacted 120-char label. `-Json` writes one object with the same fields.
- Exit codes: 0 report; 2 bad arguments; 3 database unreachable or extension not loaded; 4 stats
  reset or `dealloc` grew during the window (report printed, marked partial).
- Files: `scripts/pg-qps.ps1` (new), `server/Infrastructure/Diagnostics/PgStatStatementsSnapshot.sql`
  (new), `scripts/test-pg-qps.ps1` (new witness harness), `docs/logs.md` (one paragraph: how to take
  a qps reading).

### M2. `DbRateSampler` and `GET /api/diagnostics/db-rate`

- `server/Infrastructure/Diagnostics/DbRateSampler.cs` (new): `BackgroundService` plus a pure
  `DbRateWindow` (delta, reset detection, ring, rollups, labels). The tick opens one `AppDbContext`
  scope, runs the embedded SQL once (plus one text read for new ids), and calls the pure core.
- `server/Infrastructure/Diagnostics/DbRateSettings.cs` (new), registered in `server/Program.cs`.
- `server/Api/Endpoints/DiagnosticsEndpoints.cs`: add the `db-rate` route (D-4).
- `server/Antiphon.Server.csproj`: embed the `.sql` file.
- `docs/ops-http.md`: one row for the route. `docs/antiphon-api.md`: route map line.

### M3. `DbCallSiteMetrics` (transaction counters and sampled call sites)

- `server/Infrastructure/Data/DbCallSiteMetrics.cs` (new): `DbCommandInterceptor` +
  `IDbTransactionInterceptor` (D-7). A new file, so it does not collide with CARD-1889's edit of
  `DbCommandMetrics.cs`.
- `server/Program.cs`: add it to `AddInterceptors` next to `DbCommandMetrics` (`Program.cs:131-135`).
- `DiagnosticsEndpoints.cs`: `db-rate` adds `callSites` (top 30 by sampled share, with `unknown`
  share) and `transactions` (begin/commit/rollback/savepoint/release per second).

## 5. Slice 2: cache and buffer, one hot path per slice

### Per-statement table (B-0 rates)

| # | Statement | Call site | Why that often | CARD-1403 or later already | Change here | Staleness / invalidation | Saved qps (est.) |
|---|---|---|---|---|---|---|---|
| 1 | `RunnerId, RunnerStoreId, RunnerCwd` by Id, 18.1/s | `RoutingSessionRunnerClient.Route` -> `PhoneHomeRunnerDirectory.GetBindingAsync` (`RoutingSessionRunnerClient.cs:123`, `PhoneHomeRunnerDirectory.cs:203-217`) | One read per runner call per session (polls of get/snapshot/buffer/observe by supervisors, watchdogs and UI) | Pump caches per connection (CARD-0679 D-3); `Route` does not | **S1**: 60 s cache in `Route` (D-8) | Immutable after insert; `Missing` never cached; 60 s bound covers only retention deletes | ~17.6 (if `Route` is ~all of it; S1's tag and M3 confirm) |
| 2 | Full-row `AgentSessions`, 13.8/s | 4.0/s `RecordActivityAsync` (`AgentSessionRuntime.cs:2672`); ~9.8/s **UNOBSERVED** | Per claimed activity write; the rest per M-2/M3 | none | **S2** removes the 4.0/s; **S3** candidates after M-2/M3 | S2: none (same write) | 4.0 (S2) |
| 3 | `BEGIN` 5.2/s, `COMMIT` 4.2/s | ~170 explicit transactions plus multi-statement saves | **UNOBSERVED** | none | S2 removes the activity pair when an attempt streams; rest **S5** after M3 | n/a | UNOBSERVED share of 4.0 x 2 |
| 4 | `RunAttempts` streaming select, 4.0/s | `RecordActivityAsync` (`:2676-2681`) | Per claimed activity write | none | **S2** folds it into the one statement | none | 4.0 |
| 5 | `UPDATE AgentSessions SET LastSeenAt`, 4.0/s | `RecordActivityAsync` save | Per claimed activity write (1 s throttle) | Throttle 1 s (CARD-0679 D-3) | **S2**: the update stays, as the one statement | none | 0 (it is the remaining 1) |
| 6 | `session-state.identity`, 3.9/s | Ingest identity chunks (`AgentSessionRuntime.cs:1449-1457`) | Whole-snapshot re-ingest | CARD-1888 S2+S3 landed | none (CARD-1888 owns it) | n/a | re-measure (M-1) |
| 7 | `SAVEPOINT`/`RELEASE` 3.2/s each | `SaveChanges` inside explicit transactions | **UNOBSERVED** | none | **S5** after M3/M-4: `AutoSavepointsEnabled = false` per named call site whose code never catches a failed save and continues | n/a | up to 6.4 |
| 8 | Reconcile selects, ~3.0/s x 3 | `AgentTaskLandNotificationService.ReconcileAsync` via the 5 s hosted pass | ~15 non-parked notes per pass | CARD-1759 compiled, CARD-1769 rescan park, CARD-1773 one pull per pass | **S4** only if M-3 shows a non-`AwaitingReceipt` state with a `QueueMessageId` dominating | to be designed with the note-state reader list; delivery-adjacent | up to ~11.6 incl. #9 |
| 9 | `UPDATE AgentTaskLandNotifications SET ConcurrencyToken`, 2.9/s | `ReconcileAsync` `:98`, before it decides | Claim write on every reconcile | none | part of S4 (the claim is a fence: not removed without a fence reader list) | n/a | in #8 |
| 10 | `AgentTasks` full row, 2.9/s | **UNOBSERVED** | **UNOBSERVED** | none | S3 candidate after M3 | n/a | UNOBSERVED |

S1 + S2 = 17.6 + 4.0 + 4.0 = **~25.6 qps (15 % of 167.5)**, plus the activity `BEGIN`/`COMMIT`
pair whose count is **UNOBSERVED**. Wire time saved at 1.6-1.9 ms RTT: about 41-49 ms of waiting
per second, summed across concurrent paths.

### S1. Runner binding cache on the routing path

- `server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs`: a private
  `ConcurrentDictionary<Guid, (SessionRunnerBinding Binding, DateTimeOffset At)>`, TTL 60 s on an
  injected `TimeProvider` (constructor gains an optional `TimeProvider? clock = null`, so the
  existing `new RoutingSessionRunnerClient(directory)` call sites compile), cap 4,096 (on overflow,
  clear entries older than the TTL, then clear all). `Missing` is not stored. `Resolve` unchanged.
- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:208`: add
  `.TagWith("rt.runner-binding")` so `dbCommands` shows the rate before and after.
- `server/Program.cs:392`: unchanged registration (singleton); the clock resolves from DI.
- Tests (new class `tests/Antiphon.Tests/Infrastructure/RoutingBindingCacheTests.cs`, stub
  directory counting `GetBindingAsync`, `FakeTimeProvider`, no database):
  - `C1987_route_reads_the_binding_once_per_ttl`: five `GetAsync` calls on one session within 60 s
    read the binding once; a sixth after 61 s reads again. **Red at base: 5 reads.**
  - `C1987_missing_binding_is_never_cached`: first call `Missing` throws `NotFoundException`; the
    stub then returns `Local`; the next call routes local.
  - `C1987_resolve_runs_on_every_call`: a cached `Remote` whose runner becomes unavailable throws
    `ServiceUnavailableException` on the next call (no cached client).

### S2. Activity write in one statement

- `server/Application/Services/AgentSessionRuntime.cs` `RecordActivityAsync` (`:2661-2690`)
  only: replace the two loads and the save with one `ExecuteSqlInterpolatedAsync` of the D-9 CTE,
  tagged `-- rt.activity.write`. The claim, the warning log and the method signature stay. This
  range is outside the CARD-1888 (`:1449-1540`, prelude) and CARD-1969 (`:1046-1240`, probe) lines.
- Tests in `tests/Antiphon.Tests/Application/AgentSessionRuntimeActivityTests.cs`:
  - `C1987_activity_write_is_one_command`: `QueryBudget` around one claimed `ObserveOutputAsync`
    on a session with a streaming attempt asserts 1 command and no transaction (a
    `DbTransactionInterceptor` counter in the test). **Red at base: 3 commands and 1 transaction.**
  - `C1987_activity_write_sets_both_timestamps`: `LastSeenAt` and the newest streaming attempt's
    `LastEventAt` equal the clock; an older streaming attempt and a completed one are unchanged.
  - Existing `Output_bursts_write_LastSeenAt_at_most_once_per_interval` counts EF saves with a
    `SaveChangesInterceptor`, which an `ExecuteSql` statement never triggers. It moves to counting
    `rt.activity.write` commands with `QueryBudget` (same expected 2). Existing test affected.

### S3-S5 (not in this plan's checkpoints)

- **S3** full-row `AgentSessions`/`AgentTasks` loads: projection or `AsNoTracking` at the call sites
  M-2/M3 name. Saves round trips only where two loads collapse; otherwise it saves bytes, not qps.
- **S4** land reconcile: only if M-3 shows the due page is dominated by a non-parkable state. The
  design must list every reader of the claim token and of each skipped field first.
- **S5** savepoints: per named call site after M3, never globally (D-7, Ground truth).

Each comes back as a plan amendment with its measured call site, reader list and witness.

## 6. Existing tests affected

- Migrations: none. Public DTOs: none changed. Routes: one added (`GET /api/diagnostics/db-rate`);
  `/session-state` unchanged. The `contracts` impacted row matches `DiagnosticsEndpoints.cs`
  (`ContractSnapshotTests`); `ContractSnapshotTests.cs` names no `diagnostics` route (grep), so no regeneration is
  expected. Operation labels: new tags `rt.runner-binding`, `rt.activity.write`, `rt.diag.pgss`;
  none renamed. Statement budgets: `Output_bursts_write_LastSeenAt_at_most_once_per_interval`
  changes its counter (S2). Remote script functions: none.
- `scripts/impacted-classes.ps1 -FileList` over this plan's files (run at e229fb4d3): 70 classes,
  driven by `PhoneHomeRunnerDirectory.cs` (23), `AgentSessionRuntime.cs` (22),
  `AgentSessionSettings.cs` (21; this plan does not edit it), `Program.cs` (9),
  `DbCommandMetrics.cs` (2; not edited) and `DiagnosticsEndpoints.cs` (`contracts`, 1). Code runs
  `pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range origin/master..HEAD -Json` per slice
  and reports it; the Merge runs the union. The checkpoint rows below stay short by the operator's
  2026-11-18 rule and do not run those classes.

## 7. Verification design

| W | Claim | Where | Red at base | Green |
|---|---|---|---|---|
| W-1 | Script counts a known workload by tag | `test-pg-qps.ps1`: throwaway `postgres:16` with `shared_preload_libraries=pg_stat_statements`, `track=top`; 50 x `-- rt.witness.a` + 20 x `-- rt.witness.b` inside the window | n/a (new script) | `rt.witness.a` calls 50, `.b` 20 in `-Json` |
| W-2 | Transaction control is grouped | same: 10 `BEGIN`/`COMMIT` pairs | n/a | `tx.begin` 10, `tx.commit` 10 |
| W-3 | Redaction | same: one `ALTER ROLE ... PASSWORD '<random>'` before the window and one inside | n/a | the random value appears in no output byte; label `redacted-utility` |
| W-4 | Reset is detected | same: `pg_stat_statements_reset()` run by the harness mid-window | n/a | exit 4, `partial: true` |
| W-5 | Version, cost, 5.1 syntax | same: `server_version_num / 10000 = 16` (prints full version); records snapshot ms; the script file is ASCII and its AST has no ternary, null-coalescing, null-conditional or pipeline-chain node | n/a | all hold |
| W-6 | Sampler delta and rollups | `DbRateSamplerTests/C1987_window_*` (pure, synthetic snapshots) | n/a | qps/ms/rows per minute and 1/5/15/60 rollups exact |
| W-7 | Reset/dealloc gap | `DbRateSamplerTests/C1987_reset_*` | n/a | minute recorded as gap, no negative rate |
| W-8 | Bounds | `DbRateSamplerTests/C1987_bounds_*` | n/a | ring 1,440, labels 2,000, redaction applied |
| W-9 | Call-site sampler | `DbCallSiteMetricsTests/C1987_*`: a known method issuing 128 commands (sample 1/1 in test mode) is the top key; transaction events counted | n/a | key `DbCallSiteMetricsTests.<method>`; counts exact |
| W-10 | Binding read once per TTL | `RoutingBindingCacheTests/C1987_route_reads_the_binding_once_per_ttl` | **5 reads, budget 1** | 1 then 2 |
| W-11 | Missing not cached; Resolve live | `RoutingBindingCacheTests/C1987_missing_*`, `C1987_resolve_*` | n/a | as section 5 |
| W-12 | Activity write is one command, no transaction | `AgentSessionRuntimeActivityTests/C1987_activity_write_is_one_command` | **3 commands, 1 transaction** | 1, 0 |
| W-13 | Same values written | `AgentSessionRuntimeActivityTests/C1987_activity_write_sets_both_timestamps` | n/a | exact |

The `test-pg-qps.ps1` harness follows `test-cutover-postgres-verify.ps1` (CARD-1974): container
name `c1987-<16 hex>`, `--tmpfs` data, random password in the child environment only, a refusal if
any mount names `antiphon_pgdata` or `antiphon-server2`, removal in `finally`. It prints
`WITNESS passed=<n> failed=<n>`. The sampler's SQL is exercised against real Postgres by W-1..W-5
(the same file); the C# tests stay database-free, because `TestDbFixture`'s `postgres:16-alpine`
does not preload the extension.

### Checkpoints

Lane: the runner-defaults global host (`server2`, linux; `GET /api/runner-defaults` revision 2).
Omit `-Runner` and `-Platform`. Export `ANTIPHON_LINUX_COMPILER_POLICY=0` before every dotnet
command. One isolated build per `After` group, one `--serial` run per group, no `--row-timeout`.
Red rows run once at their test commit and are not re-run after the fix.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | M1 | n/a | c1987-pgqps | `pwsh -NoProfile -File scripts/test-pg-qps.ps1` | W-1..W-5 | WITNESS passed=5 failed=0, exit 0 | n/a | 15 |
| CP-2 | M3 | `tests/Antiphon.Tests -> bin-c1987-m/` | c1987-m | `/*/*/(DbRateSamplerTests*)\|(DbCallSiteMetricsTests*)/*` | W-6..W-9 | 0 failed/skipped; executed = the count Code declares in its report (at least one per W) | 4 | 15 |
| CP-3 | S1 test commit | `tests/Antiphon.Tests -> bin-c1987-s1red/` | c1987-s1-red | `/*/*/RoutingBindingCacheTests/C1987_route_reads_the_binding_once_per_ttl` | W-10 red | 1 executed, failed on the read count (5, budget 1) | 1 | 15 |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1987-s1/` | c1987-s1 | `/*/*/(RoutingBindingCacheTests*)\|(PhoneHomeSessionRoutingTests*)/*` | W-10, W-11, existing routing | 8 executed (3 + 5), 0 failed/skipped | 8 | 15 |
| CP-5 | S2 test commit | `tests/Antiphon.Tests -> bin-c1987-s2red/` | c1987-s2-red | `/*/*/AgentSessionRuntimeActivityTests/C1987_activity_write_is_one_command` | W-12 red | 1 executed, failed on the command count (3, budget 1) | 1 | 15 |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1987-s2/` | c1987-s2 | `/*/*/(AgentSessionRuntimeActivityTests*)\|(RunAttemptStallDetectorTests*)/*` | W-12, W-13, the existing burst test, the stall detector | 4 executed (3 + 1), 0 failed/skipped | 4 | 15 |

Every OR operand has parentheses and a trailing `*`. Suffix check at e229fb4d3: no class in
`tests/` starts with `PhoneHomeSessionRoutingTests`, `RunAttemptStallDetectorTests`,
`AgentSessionRuntimeActivityTests` other than itself (grep of `class <name>\w*`); the three new
class names do not exist yet and none is a prefix of another. Code runs
`dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan <this file> --out .antiphon/c1987-import.yaml`
before the first run and records the result. CP-2's `Min` is a floor; Code sets the declared count.

## Performance budget

- **Selection:** 6 rows, five cold `Antiphon.Tests` builds (CP-2..CP-6) and one command row.
  Ordinary floor 6 x 15 = **90 min** of estimates across six runs; estimates are deadline inputs.
  No row is expected over 30 min; one that exceeds it gets a Debug task with its timing. Row
  deadlines are the tool's derived `3 x 15 = 45 min`.
- **Per-test:** W-6..W-13 are database-free or one isolated schema (`AgentSessionRuntimeActivityTests`
  uses `BridgeQueueHarness` on `TestDbFixture`); per-test p95 is **UNOBSERVED** (new tests). No new
  `[Timeout]`; if Code adds one, use about 2x the measured TRX p95, provisional ceiling 30 s.
- **CP-1 phases:** image pull (cached after the first run; **UNOBSERVED** cold), container start
  and `pg_isready` (CARD-1974's harness waits up to 90 s), a 10 s measurement window per witness
  (the harness passes `-Seconds 10`), teardown. Provisional total 3-5 min. One container shared by
  W-1..W-5 (reset in W-4 runs last). The harness prints phase timings.
- **Shared setup:** `TestDbFixture` once per assembly (unchanged); W-6..W-11 need none.
- **Production cost:** sampler 1 command/min plus text reads for new ids (D-6); call-site sampler
  about 2.6 captures/s (D-7); S1 and S2 remove commands. Code reports W-5's snapshot ms and W-9's
  per-capture microseconds.

## 8. Collisions, order, rollout

- **Order:** M1 -> M2 -> M3 (measure first; M3 is what attributes S3-S5), then S1, then S2. S1
  and S2 do not depend on M2/M3 and may run in parallel Code tasks (different files).
- **CARD-1888 / CARD-1969** (landed on master; `AgentSessionRuntime.cs` `:1046-1240`, `:1449-1540`,
  `TranscriptPromptSpan.cs`): not touched. S2 edits only `:2661-2690`.
- **CARD-1889** (Review): edits `DbCommandMetrics.cs`, `LiveTiming.cs`, `DbCommandMetricsTests`.
  This plan adds new files instead and edits `Program.cs` near the same registration lines
  (`:131-135`); if both land, the second rebases on a two-line context. Code checks
  `git merge-tree` against the landed tip.
- **CARD-1984, CARD-1893:** test/compose only; no overlap.
- **CARD-1972** (request attribution): complementary; M3's call sites will show the DB side of the
  `GET /api/agent-tasks/{id}` polling.
- **Rollout:** master first, then an rc port (the orchestrator decides which rc), then the desktop
  AppHost restart from the main checkout (`scripts/restart-apphost.ps1`, orchestrator autonomy) and
  a `GET /api/version` SHA check. No migration, no runner restart, no server2 roll.
- **Observation after activation:** read `GET /api/diagnostics/db-rate?minutes=15` twice, 10 min
  apart; record total qps, `rt.runner-binding` rate (expected about 0.3/s), `rt.activity.write`
  (expected about the old 4.0/s, one command each), `callSites` top 10 and `unknown` share, and
  the transaction rates. Post that on the card; it seeds the S3-S5 amendment.

Checklist (stage rule): this plan adds or changes no session that waits for input. Nothing here
parks, releases or stops a session.

## 9. Operator decisions (yes/no, with recommendation)

- **Q-1.** Run the 60 s `pg_stat_statements` sampler in the production server by default
  (one command a minute, no writes, in-memory 24 h)? **Yes.**
- **Q-2.** Sample one command in 64 for its call site with a stack trace (code identifiers only)?
  **Yes**: it is the only cheap way to attribute the 94.6 % untagged load and the transaction chatter.
- **Q-3.** Raise `AgentSession:ActivityWriteMinIntervalMs` from 1,000 to 5,000 after S2 (would cut
  up to a further ~3 qps; every reader of `LastSeenAt`/`LastEventAt` measures in minutes: stall
  300 s, Starting grace 300 s)? **Not now**: decide on the post-S2 measurement.
- **Q-4.** Accept that S1 + S2 reach about 15 %, and that the 30 % target waits for the measured
  S3-S5 amendment? **Yes.**

## Decisions under stated defaults

- D-n above are this plan's defaults. Where the operator has not answered Q-1..Q-4, Code builds
  with the recommended answer (Q-3 stays at 1,000 ms).
- Mutation checks are waived (operator 2026-11-12). Tests are short rows only (operator 2026-11-18).

## Import

Read-only import at the plan commit's parent tree (e229fb4d3 + this file), `ANTIPHON_LINUX_COMPILER_POLICY=0`:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1987-plan-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-18-card-1987-query-rate-and-hot-path-caching-plan.md --out .antiphon/c1987-import.yaml`

Slot waited 15 s, held 15 s. Exit 0, "imported 6 rows", no refusal. Five builds
(`bin-c1987-m`, `-s1red`, `-s1`, `-s2red`, `-s2`), no reuse. `minExecuted` -/4/1/8/1/4,
`timeoutMinutes` 45 for every row. Filters copied verbatim (the `\|` escapes become `|`).

--- next stage ---
next: review
handoff: Grok plan review of CARD-1987 (measure qps: pg-qps.ps1 + 60 s pg_stat_statements sampler + sampled call sites; S1 runner-binding route cache 60 s; S2 one-statement activity write; ~15 % cut, S3-S5 gated on M-1..M-4); then Code M1-M3, S1, S2 on server2.
artifact: docs/superpowers/plans/2026-11-18-card-1987-query-rate-and-hot-path-caching-plan.md
