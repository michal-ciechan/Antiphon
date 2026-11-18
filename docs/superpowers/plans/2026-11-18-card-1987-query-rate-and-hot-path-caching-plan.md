# CARD-1987 plan: measure Postgres queries per second by operation, then cache and buffer the hottest paths

Date: 2026-11-18. Plan task `70f587e0` on `feat/card-task-70f587e0`. Card CARD-1987
(`88d47f5c-37a8-4267-b33a-020f6981e365`), board Antiphon, High/Soon.

- **Master read:** `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (`git fetch origin master`, 2026-11-18).
  `refs/heads/rc/2026-11-27` is the same SHA, and `GET /api/version` on the desktop server returned
  `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (`land-v2`) on the same read. Every file:line below is
  therefore the running code. CARD-1888 S3 (`0c0a65858`) and CARD-1969 S2/S3 (`54f692d51`, `29ba0c500`)
  are ancestors.
- **Lane:** `GET /api/runner-defaults` returned `globalRunnerId=server2`, revision 2, no kind defaults.
  `GET /api/session-runners` returned `desktop` (windows, available, eligible), `server2` (linux, available,
  eligible) and `server2-temp` (not available). Checkpoints run on the global default (server2, Linux).
  They omit `-Runner` and `-Platform`.
- **Scope of this dispatch:** plan only. No code, no setting, no restart, and no SQL against any
  database. The orchestrator owns the measurements (standing authority, 2026-11-18). Mutation checks
  are waived (operator 2026-11-12). Tests are short (operator 2026-11-18).

## 1. Baseline, and what to re-measure first

**Baseline** (the orchestrator, desktop `pg_stat_statements`, 647 s, 2026-11-17 09:24:53Z-09:35:40Z,
before the CARD-1888/1969 probe fixes): 108,400 statements = **167.5 statements/s** over 455 distinct
statements.

| # | Statement (as measured, truncated) | Calls | /s | Mean |
|---|---|---:|---:|---:|
| B1 | `SELECT a."RunnerId", a."RunnerStoreId", a."RunnerCwd" FROM "AgentSessions" WHERE Id = ...` | 11,729 | 18.1 | 0.02 ms |
| B2 | `SELECT a."Id", a."AgentKind", a."BootPromptSequence", a."BootReplyDueAt", a."CapacityLaunc..." FROM "AgentSessions"` (full entity) | 8,920 | 13.8 | 0.04 ms |
| B3 | `BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED` / `COMMIT` | 3,336 / 2,699 | 5.2 / 4.2 | |
| B4 | `SELECT r."Id", r."AgentSessionId", r."AttemptNumber", r."BoardWorkflowDefinitionId"...` (`RunAttempts`, full entity) | 2,591 | 4.0 | |
| B5 | `UPDATE "AgentSessions" SET "LastSeenAt" = $1 WHERE "Id" = $2` | 2,591 | 4.0 | 0.39 ms |
| B6 | `-- session-state.identity SELECT t."Uuid", t."Kind" FROM "TranscriptEntries"` | 2,550 | 3.9 | 5.08 ms |
| B7 | `SAVEPOINT __EFSavePoint` / `RELEASE SAVEPOINT __EFSavePoint` | 2,088 each | 3.2 each | |
| B8 | `-- land-reconcile-compiled` selects (note row with `Body`/`CompletionDeliveryJson`; session scan; session `Status`/`StartedAt`) | 1,968 / 1,926 / 1,918 | 3.0 each | |
| B9 | `UPDATE "AgentTaskLandNotifications" SET "ConcurrencyToken" ...` | 1,866 | 2.9 | |
| B10 | `SELECT a."Id", a."ActiveLandingId", a."AgentId", ... FROM "AgentTasks"` (full entity) | 1,863 | 2.9 | 0.28 ms |

A pg_stat_statements row is a **statement**, not a round trip. Npgsql sends `BEGIN` with the next
command, so B3's `BEGIN` costs no trip of its own (Npgsql's documented behaviour; not measured on this
deployment). Whether `SAVEPOINT` costs its own trip in Npgsql 9.0.4 is **UNOBSERVED**. The targets below
count statements, because that is what the card measured and what `pg-qps.ps1` will report.

**Re-measure first** (the orchestrator posts a fresh pair after the AppHost moved to rc/2026-11-27 at
01:16Z). In this order, each from one window of at least 600 s, with the live-session count at both ends:

| M | What | Why it is first |
|---|---|---|
| M-0 | Total statements/s and B1-B10 calls/s | The new baseline. Every saving below is stated against the 2026-11-17 numbers and must be restated against this one. |
| M-1 | B1's rate, and the live-session count | Slice S2a's size. The stale-Working probe pulled runner transcripts through the routing client (B1 runs per routed call); CARD-1969 bounded that probe, so B1 may already be lower. |
| M-2 | The **full text** of B2 (WHERE and LIMIT), and whether B2 calls equal B4 and B5 calls | Decides whether B2 is one per-id statement (`WHERE a."Id" = $1 LIMIT $2`) shared by many sites, or a list load. The activity heartbeat (S2b) issues B2, B4 and B5 once each per write; equal B4 and B5 counts in the baseline (2,591 each) fit that. |
| M-3 | The four B8/B9 rates, and `SELECT "State", "Kind", count(*) FROM "AgentTaskLandNotifications" WHERE "State" NOT IN (<terminal>) GROUP BY 1, 2` | Which notes the 5 s land scan reconciles on every pass (Section 4, row B8). Read-only. The orchestrator runs it; this task does not. |
| M-4 | `BEGIN` minus `COMMIT`, and the `ROLLBACK` row | Explains the 1.0/s gap. Rollbacks are failures or early returns inside explicit transactions. |
| M-5 | B6 after CARD-1888 S3 | B6 belongs to CARD-1888. It is not counted as a saving here. |
| M-6 | `SELECT count(*) FROM pg_stat_statements` and `pg_stat_statements_info.dealloc` | Sizes the sampler read (D-4) and shows whether `pg_stat_statements.max=5000` evicts entries (an eviction breaks a delta). |

## 2. Ground truth

| The card or brief assumes | The code shows (master `e229fb4d3`) |
|---|---|
| There is no query-rate tool | There is no continuous rate and no general reporter. Two pieces exist. **(a)** `DbCommandMetrics` (`server/Infrastructure/Data/DbCommandMetrics.cs`, CARD-1403 S0) counts every EF command since process start by allowlisted leading tag (`session-state.`, `rt.`) plus `untagged`. It is served on `GET /api/diagnostics/session-state` as `dbCommands` (`server/Api/Endpoints/DiagnosticsEndpoints.cs:16-35`). It has no rates, no transactions and no SQL. **(b)** `scripts/measure-session-state-cache.ps1` (CARD-0701) reads `pg_stat_statements` through `docker exec psql` (`:132-134`). It classifies a fixed list of shapes, including `binding` (B1, `:121`). It is `#requires -Version 7.0` and uses `ConvertFrom-Json -AsHashtable` and `ProcessStartInfo.ArgumentList`, so it does not run on Windows PowerShell 5.1. |
| CARD-1403 cached things | CARD-1403 **batched** rather than cached: one boot-reply EXISTS per tick (S1 `d9056c10f`), a one-command ingest prelude (S3 `a550bcbd2`), the counter and the `QueryBudget` test helper (S0 `7bb6355a4`, `tests/Antiphon.Tests/TestHelpers/QueryBudget.cs`). Its D-4 rejected caching the boot-reply answer. Its S2 (static working reads through `SessionStateStore`) depended on a reading. Older caches: CARD-0701 `SessionStateStore`, CARD-0679 D-3 owner cache in the recovery pump (`PhoneHomeRecoveryPump.OwnerMatchesAsync`, `server/Application/Services/PhoneHomeRecoveryPump.cs:918-933`) and activity coalescing (`AgentSessionRuntime.ClaimActivityWrite`, `:2701-2721`, default 1,000 ms). CARD-1759/1769/1773 compiled the land-reconcile queries and park unchanged `AwaitingReceipt` notes (`AgentTaskLandNotificationHostedService.cs:174-247`). |
| B1 is "the runner lookup" at an unknown site | **Confirmed by projection.** `PhoneHomeRunnerDirectory.GetBindingAsync` (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:203-217`) is the only query that selects exactly those three columns. It has no tag and no cache. Callers: **`RoutingSessionRunnerClient.Route` (`RoutingSessionRunnerClient.cs:123-133`), on every session-addressed runner call** (Get, GetTranscript, GetSnapshot, SendInput, Kill and others; the singleton `ISessionRunnerClient`, `server/Program.cs:392`); `AgentSessionRuntime.EnsureInputTransportAvailableAsync` when no binding is passed (`AgentSessionRuntime.cs:2233`); `TaskParkPublicationService` (`:41,82,106,205,249`); the recovery pump (cached, above). The routed calls dominate by construction; the per-caller split is **UNOBSERVED** (S1c measures it). |
| The binding may change and needs write invalidation | The three columns are assigned only in new-session initializers: `AgentTaskDispatcher.cs:6376-6378` and `:6916-6918`, `AgentControlService.cs:798-802`. No update site was found (grep for assignments, `SetProperty` and raw `UPDATE` on the three columns). `PhoneHomeRecoveryPump.cs:913-916` already states "persisted before its Launch and never changes" and caches positive answers for the life of a connection. The only later change is row deletion (retention, terminal sessions older than 7 days). |
| A second server process may write `AgentSessions` | One AppHost server writes the desktop database. `Antiphon.Messaging.Service` references Npgsql, but whether it shares the `antiphon` database is **UNOBSERVED**. The CARD-1742 staging server has its own volume. Live process listing is **UNOBSERVED** from this mirror. The binding cache does not depend on this, because the binding is insert-only (D-6). |
| LastSeenAt is a hot write that needs coalescing | It is already coalesced at 1 s per session. `RecordActivityAsync` (`AgentSessionRuntime.cs:2661-2692`), called once per output chunk (`:182`), costs **three statements per write**. It loads the full `AgentSessions` entity (a B2 statement), loads the newest streaming `RunAttempts` entity (B4), then saves (B5; plus a `RunAttempts` UPDATE when a streaming attempt exists). B4 = B5 = 2,591 fits that. B5 at 4.0/s means about four sessions printing at any moment. |
| Land reconcile can skip when nothing changed | It already does for `AwaitingReceipt` notes with a parent session (CARD-1769 `RescanPark`). The residue (about 15 note reconciles per 5 s pass: 1,968 note loads / 129 passes) is notes the park does not cover. Each such reconcile bumps `ConcurrencyToken` before it reads anything (`AgentTaskLandNotificationService.cs:98`) and saves at least once. This is the delivery-receipt path (Section 5). Which states those notes are in is **UNOBSERVED** (M-3). |
| `BEGIN`/`SAVEPOINT` come from single-statement reads | `SAVEPOINT __EFSavePoint` is EF Core's automatic savepoint around a `SaveChanges` inside a **user** transaction, so B7 is 3.2 saves/s inside explicit transactions, not reads. `BEGIN` comes from `Database.BeginTransactionAsync` (214 sites in `server/`) or from a multi-statement `SaveChanges`. A single-statement `SaveChanges` opens none (EF Core 7+). No call site is attributed (S1c). One site depends on a manual savepoint (`AgentSupervisorService.cs:694,747`), and the transcript persist path continues after a failed batch save, so savepoints cannot be turned off globally (D-9). |
| The test database can host the witnesses | `TestDbFixture` runs `postgres:16-alpine` (`TestDbFixtureLifecycle.cs:27`) **without** `shared_preload_libraries=pg_stat_statements`. Witnesses that need the real view need their own `postgres:16` container (D-11). Production compose files load the library with `pg_stat_statements.track=top` and `max=5000` (`docker-compose.dev.yml:17-25`, `docker-compose.server2-postgres.yml:12-19`). The desktop image tag is `postgres:16`; the running minor (16.14 per the brief) is not readable from here. |
| There is a host-stats surface for a DB rate | `GET /api/hosts/...` (`HostStatsEndpoints.cs`) carries runner host load, not server or database signals. CARD-1889 (Review) is extending host attribution and `LiveTiming`'s slow-call line. The database rate belongs beside `dbCommands` under `/api/diagnostics` (D-3). |

## 3. Decisions

### D-1. Measure by statement and by tag, from both ends

Slice 1 adds three readers. **(a)** `scripts/pg-qps.ps1` reads two `pg_stat_statements` snapshots,
for an operator or orchestrator on demand. **(b)** A bounded in-process sampler keeps the database's own
rate for the last 24 hours. **(c)** A sampled call-site attribution names the method behind untagged
statements and transactions. (a) and (b) see every client of the database. (c) sees only this server,
but it is the only reader that can say **why** (which method).

Rejected:
- EF `LogTo` or Npgsql command logging: volume, and SQL text in logs.
- A Hangfire job that writes samples to a table: it adds writes to the database it measures, and
  durable history is not asked for.
- `pg_stat_statements_reset()` before each window: shared state, and the delta needs no reset.
- Tagging every query by hand first: 214 transaction sites and an unknown number of entity loads.
  S1c tells Code which few to tag.

### D-2. `pg-qps.ps1` is a Windows PowerShell 5.1 script with three transports and offline compare

- **Transports.** `-Container <name>` (default `antiphon-postgres`) runs `docker exec <name> psql -X -A -t -v ON_ERROR_STOP=1`.
  `-Psql` runs the local `psql` with the **inherited libpq environment** (`PGHOST`, `PGPORT`, `PGUSER`,
  `PGDATABASE`, and `PGPASSWORD` or `PGPASSFILE`). The operator fills that environment from the
  standard custody in `docs/agent-credentials.md`. The script takes no connection string or password
  parameter. It never prints, logs or writes the environment. A failed `psql` reports its exit code
  and a fixed sentence, never stderr (stderr can echo a host or user).
  `-Snapshot <out.json>` writes one snapshot. `-Compare <a.json> <b.json>` compares two snapshots
  offline.
- **Default run.** Take a snapshot, wait `-IntervalSeconds` (default 60, range 5-3600), take a second
  snapshot and print the delta.
- **Snapshot query.** One statement from `pg_stat_statements(true)` filtered to
  `dbid = current database`. It reads `queryid`, `userid`, `toplevel`, `calls`, `total_exec_time`,
  `rows` and `left(query, 400)`, plus `pg_stat_statements_info` (`stats_reset`, `dealloc`),
  `clock_timestamp()` and `pg_postmaster_start_time()`. Output is JSON through `json_agg`, as in
  `measure-session-state-cache.ps1:99-130`.
- **Grouping.**
  - The **operation tag** is the first line when it is `-- <tag>` and `<tag>` matches
    `^[a-z0-9.-]{1,64}$`. It is the same alphabet as `DbCommandMetrics.LeadingTag`, but **any** prefix
    is accepted, so `land-reconcile-compiled` counts as a tag.
  - Transaction control is grouped as `tx.begin`, `tx.commit`, `tx.rollback`, `tx.savepoint` and
    `tx.release`.
  - Everything else is `untagged`.
  - Per statement the key is `(queryid, userid, toplevel)`.
- **Output.**
  - A header: the window seconds, total statements/s, DB ms/s, rows/s, whether a reset or dealloc
    happened, and the postmaster start time.
  - **Per tag:** queries/s, ms/s, rows/s, calls and mean ms.
  - **Top 20 by calls and top 20 by total time:** the same columns plus the tag and the first
    160 characters of the normalized text.
  - `-Json` emits the same data as one object. `-NoText` drops the text column, for sharing.
- **Gaps.** A `stats_reset` change, a postmaster restart, or a `calls` decrease for any key makes the
  window **invalid** (exit 3, the reason printed, no rates). A `dealloc` increase is reported as
  `evicted=n`, and its keys count as new. Exit 0 is a valid window, 2 a usage or transport error,
  3 an invalid window.
- **5.1 rules.** ASCII only. No `-AsHashtable`, no `ArgumentList`, no ternary, `??`, `?.` or
  pipeline chains. The process is bounded with `WaitForExit(ms)` and arguments are quoted by hand
  (one fixed quoting helper). It is guarded by the existing C1698 syntax scan (D-12).
- **Privacy.** `pg_stat_statements` stores **normalized** text: constants in plannable statements
  become `$n`. EF sends values as parameters anyway. Utility statements (`SAVEPOINT`, `SET`) carry
  no data in this code base. The witness proves that a literal sent in a query does not appear in
  the output (V-2).

Rejected:
- Extending `measure-session-state-cache.ps1`: it is a CARD-0701 acceptance harness with fixed
  shapes, pwsh 7 only and immutable evidence files. A shared 5.1 SQL function would couple two
  test surfaces.
- A .NET console tool: the brief asks for a script, and Windows PowerShell 5.1 cannot load Npgsql
  without bundling.
- A connection-string parameter: it would sit in the process list and the shell history.

### D-3. The continuous sampler is a bounded `BackgroundService` served at `GET /api/diagnostics/db-rate`

- **Service.** `PgStatementRateSampler` in `server/Infrastructure/Data/`. On a `PeriodicTimer` of
  `Diagnostics:DbRate:IntervalSeconds` (default 60), it reads one snapshot through its own scope's
  `AppDbContext` connection: raw ADO, tagged `-- rt.diagnostics.db-rate`, no transaction.
- **Store.** It keeps the last snapshot and appends one **minute record**: window seconds, total
  statements/s, ms/s, rows/s, the top 20 statements by calls (queryid, tag, calls/s, ms/s, mean ms)
  and every tag's rates. The records go to a ring of `RetainMinutes` (default 1,440 = 24 h).
- **Statement text.** Text is kept once per queryid, in a dictionary capped at 2,000 entries,
  evicted with the ring. It is truncated to 160 characters.
- **Route.** `GET /api/diagnostics/db-rate?minutes=60` returns the records in range, a summary (mean
  and max statements/s over the range) and `available`/`reason`. It sits beside the existing
  `/session-state` route, which already carries `dbCommands`. `docs/ops-http.md` gains one line.
- **Settings.** `Diagnostics:DbRate:Enabled` (default **true**), `IntervalSeconds` (60, 15-3600),
  `RetainMinutes` (1,440, 60-10,080), `TopStatements` (20, 5-100), `IncludeText` (true).

Rejected:
- A row in host stats: that surface is runner host load, and CARD-1889 is changing it.
- Persisting the samples: no history across restarts was asked for. 24 h in memory covers
  "always visible", and a restart starts a fresh window.

### D-4. Sampler cost: about one statement a minute against 167.5 per second

- **Statement rate.** One read per 60 s is 0.017 statements/s, 0.01 % of the baseline.
- **Read size.** `pg_stat_statements(true)` reads the shared hash plus the query-text file. At the
  brief's 1,700 entries and about 400 bytes of text each, that is about 700 KB per read. To keep that
  cost to new ids only, a tick reads `pg_stat_statements(false)` (no text file) and fetches text only
  for queryids it has not seen (`WHERE queryid = ANY($1)`, the same view with text).
- **Steady state.** About 1,700 rows × about 60 bytes = about 100 KB/min over a pooled connection.
- **Execution time.** The mean is **UNOBSERVED**; S1b logs it at Debug and reports it in the route
  (`sampleMs`).
- **Memory.** 1,440 records × (20 statements + about 40 tags) × about 64 bytes = about 5.5 MB, plus
  at most 2,000 × 160 characters of text = about 0.6 MB.
- **Stop rule.** If S1b's measured `sampleMs` p95 exceeds 250 ms on the desktop, the interval rises
  to 300 s before any other change.

### D-5. Call-site attribution samples stack frames, 1 in N, for untagged statements and transactions only

- **What is sampled.** `DbCallSiteSampler` is a `DbCommandInterceptor` plus `DbTransactionInterceptor`,
  registered beside `DbCommandMetrics`. It sees one in `Diagnostics:DbRate:CallSiteSampleEvery`
  (default 20) of: untagged commands, `TransactionStarted`, and `CreatingSavepoint`.
- **Key.** For each sampled event it walks `new StackTrace(false)` to the first frame whose type is
  in `Antiphon.Server` and outside `Antiphon.Server.Infrastructure.Data` and the EF/Npgsql/System
  namespaces. The key is `Type.Method`, with an async state machine mapped to its outer method name.
  Counts are kept per (kind, key).
- **Route.** `/db-rate` shows `callSites` as estimated rates (count × N ÷ uptime, and per window when
  sampled twice), plus `unattributed` (no frame found, for example after an `await` that resumed on
  the pool). The key is code identity, never data.
- **Cost.** About 8.4 stack walks/s at 167.5 statements/s with N=20, roughly 0.4 ms CPU per second.
  That is an estimate; S1c measures it.
- **Why.** The brief forbids guessing call sites. 9.8/s of B2 (Section 4) and all of B3/B7 have no
  tag, and their text cannot separate callers. A sampled stack names the method without editing 214
  sites first.

Rejected:
- EF `TagWithCallSite()` on every query: hundreds of edits, and file-path comments.
- Always-on stack capture: 167 walks/s for no extra information.
- `Activity` scopes per caller: call-site edits again.

### D-6. Runner binding cache: positive answers only, 30 s TTL, routing path only

`RoutingSessionRunnerClient.Route` caches `Local` and `Remote` bindings per session id for
`PhoneHomeRunner:RoutingBindingCacheSeconds` (default 30, 0 disables). Rules:

- **`Missing` is never cached.** A session row committed after a first miss is seen at once.
- **Capacity.** 4,096 entries; on overflow the cache is cleared (simplest bound; the expected size
  is the live-session count, about 20).
- **TTL.** Expiry is by `TimeProvider`.
- **Invalidation.** No write path exists after insert (Section 2). The TTL bounds staleness against
  a future writer, another process, or a deleted row. A deleted row is seen as `Missing` at most
  30 s late, and only for a terminal session older than 7 days, which nothing routes to.
- **Not cached.** `ISessionRunnerDirectory.GetBindingAsync` itself stays uncached. That keeps
  `TaskParkPublicationService`'s re-read fence (`:82`, `binding != re-read`), the recovery pump (it
  has its own cache) and `EnsureInputTransportAvailableAsync` exactly as today.
- **Tag.** The read gains `TagWith("rt.runner.binding")`, so `DbCommandMetrics` and `pg-qps` count it.
- **Default off without a clock.** The constructor keeps its one-argument form, and without a
  `TimeProvider` the cache is off. The many tests that construct the client directly with
  `new RoutingSessionRunnerClient(directory)` (ten classes, for example `PhoneHomeSessionRoutingTests`,
  `MultiRunnerRecoveryTests`) keep today's behaviour. DI passes `TimeProvider` and the setting.

**Delivery guard.** The binding chooses a transport. It is never delivery evidence. A routed
`SendInput` still lands only on the runner the row names. A stale binding can only be stale for a
deleted row, and that call then fails at the runner instead of with `NotFoundException`.

Rejected:
- Caching inside the directory: it would change the park fence's meaning.
- Event invalidation: there are no events to hook.
- An unbounded lifetime: deletion and any future rebinding would never be seen.

### D-7. Activity heartbeat: one tagged statement per write, written at most every 5 s

`RecordActivityAsync` replaces the two entity loads and the tracked save with **one** command,
tagged `-- rt.activity.touch`:

```sql
WITH s AS (UPDATE "AgentSessions" SET "LastSeenAt" = @now WHERE "Id" = @id)
UPDATE "RunAttempts" SET "LastEventAt" = @now
WHERE "Id" = (SELECT "Id" FROM "RunAttempts"
              WHERE "AgentSessionId" = @id AND "Phase" = @streaming AND "CompletedAt" IS NULL
              ORDER BY "AttemptNumber" DESC LIMIT 1)
```

Code writes the enum parameter the way the model maps `Phase`.

**Semantics.** The values and conditions are the same as today: `LastSeenAt = now`, and the newest
open streaming attempt gets `LastEventAt = now`. The one difference is that a same-value write is
no longer an EF no-op (harmless).

**Interval.** The default of `AgentSessions:ActivityWriteMinIntervalMs` moves from 1,000 to **5,000**
(operator decision Q-2). The staleness bound becomes 5 s. Every reader measures in much larger units:

| Reader | Unit |
|---|---|
| `RunAttemptStallDetector` | `StallTimeoutMs` 300,000 on `LastEventAt` |
| Orchestrator Starting grace | 300 s (`OrchestratorSettings.cs:13`, `OrchestratorService.cs:1071`) |
| Reconciliation `Stopping` retry | 60 s (`SessionReconciliationSettings.cs:41`, `SessionReconciliationService.cs:960`) |
| Ghost-seat idle | 10 min (`GhostSeatRelease.cs:12`, `AgentTaskDispatcher.PoolSupervision.cs:321`) |
| Attention leaks, retention | Minutes to days |
| `BoardService`, `AgentService`, `OrchestratorService:374` | Display only |

**No trailing flush.** The write stays leading-edge, as today. A trailing write-behind would add a
timer per session to move a display value by at most 5 s. On shutdown, at most the last 5 s of
`LastSeenAt` is not written, and no decision reads it at that resolution.

**Delivery guard.** `LastSeenAt` and `LastEventAt` are liveness heartbeats. The delivery verdict is
the transcript-confirmed `UserPrompt` (`docs/session-runtime-invariants.md`). TurnEnd and recovery
read transcript rows and the session-state store, not these columns. No read is cached here: this
removes reads that only existed to feed a tracked save.

Rejected:
- `ExecuteUpdateAsync` twice: two statements.
- Keeping 1 s and fixing only the statement count: about 4/s remain.
- A per-process write-behind queue that flushes every N seconds for all sessions: it saves the same
  statements with more machinery and a shutdown hazard.

### D-8. The land-reconcile residue is measured here and fixed under its own card

B8+B9 total about 11.9 statements/s (7 % of the baseline), but they are the land **receipt** path.
`ReconcileAsync` decides whether a note's message was delivered, by transcript receipt. The park
(CARD-1769) is the established safe skip, and widening it means proving that a skipped note cannot
miss a receipt. That needs its own plan and review. This card: M-3 and S1c attribute the residue (which
states and kinds are reconciled every pass, and from which method). The orchestrator files the
follow-up card with those numbers. Recommendation Q-4.

Rejected: a version stamp on the note scan in this card. That is the same correctness claim as the
park, and it is not provable inside this plan's budget.

### D-9. Transactions and savepoints: attribute first, change per site later

- **Why not a global switch.** EF's automatic savepoints can be turned off per context
  (`Database.AutoSavepointsEnabled = false`). A global switch is unsafe:
  - `AgentSupervisorService.cs:694,747` rolls back to a manual savepoint.
  - Any caller that catches a failed `SaveChanges` and continues the transaction needs the
    automatic savepoint. In Postgres a failed statement aborts the transaction.
  - `EnsureCreated`/execution-strategy retries also replay through it.
- **What S1c gives.** The `TransactionStarted`/`CreatingSavepoint` call sites with rates.
- **The rule for a later slice.** A site qualifies for `AutoSavepointsEnabled = false` only when it
  has one `SaveChanges` inside its transaction and no catch-and-continue. A site qualifies for
  dropping its explicit transaction only when it is read-only and needs no snapshot consistency
  across statements.
- **Candidates by code shape** (rates **UNOBSERVED**): `AgentSessionRuntime.cs:251` (exit event),
  `:1609` (persist recovery transaction, conditional), `:1803` (conditional lock);
  `SessionMessageQueueService.cs:307,874,1082,2981` (conditional); `AgentTaskDispatcher.cs:1516`
  (conditional). Every one of these holds a row lock or writes several rows, so none is a
  "single-statement read-only" path as the brief supposed.
- **Effect.** No saving is claimed from B3/B7 in this plan.

### D-10. Target

| Change | Baseline statements/s | After (estimate) | Saved/s | Basis |
|---|---:|---:|---:|---|
| S2a binding cache (B1) | 18.1 | ≤ 0.7 | **~17.4** | About 20 routed sessions / 30 s TTL. The session count is from CARD-1403's 16-18 live sessions; M-1 restates it. |
| S2b activity heartbeat (B2 share, B4, B5) | 4.0 + 4.0 + 4.0 = 12.0 | 4.0 writes/s × (1/5) × 1 statement = 0.8 | **~11.2** | B4 = B5 = 2,591 (one write each); 4 continuously printing sessions at a 1 s interval become 0.8 writes/s at 5 s. |
| **Committed slices** | | | **~28.6 = 17 % of 167.5** | |
| S2c (conditional) the top attributed B2 site | up to 9.8 | | UNOBSERVED | Needs S1c. Same rules (positive-only or insert-only data, TTL, no delivery evidence). |
| Follow-up card: land reconcile (D-8) | 11.9 | | UNOBSERVED | Separate plan. |

**The brief's 30 % (about 50 statements/s) is not reached by the committed slices alone.** It needs S2c or
the land-reconcile card, or both. B6 (3.9/s) falls under CARD-1888 and is not counted here. That is
operator decision Q-1.

### D-11. Witness database: one throwaway `postgres:16` with `pg_stat_statements`, shared per test session

- **Fixture.** `PgStatStatementsContainer` (new, `tests/Antiphon.Tests/TestHelpers/`) uses Testcontainers
  `PostgreSqlBuilder` with image `postgres:16` (the production major and tag) and command
  `-c shared_preload_libraries=pg_stat_statements -c pg_stat_statements.track=top -c pg_stat_statements.max=5000`
  (the compose flags). It runs `CREATE EXTENSION pg_stat_statements` and is shared once per test
  session (`ClassDataSource<...>(Shared = SharedType.PerTestSession)`).
- **Lifecycle.** It uses the same owner labels and orphan sweep as `TestDbContainerOwnership`, so a
  crashed run is reclaimed.
- **Isolation.** Each test uses its own database (`CREATE DATABASE c1987_<guid>`) and its own tag
  (`rt.c1987.<guid8>`), so concurrent tests never share a delta.
- **Unavailable path.** The "extension unavailable" witness (V-6) uses the ordinary `TestDbFixture`
  (no preload), which is the natural negative control.

Rejected:
- Shadowing `pg_stat_statements` with a CTE, as `MeasureSessionStateCacheScriptTests.cs:235` does: it
  proves the SQL, not the real view's columns, normalization or reset behaviour on 16. It remains a
  good unit technique for the offline compare math, which V-3 does from JSON fixtures without any
  database.

### D-12. 5.1 guard: add `scripts/pg-qps.ps1` to the C1698 scan list

`scripts/test-toolchain-floor.ps1` `Test-C1698_WindowsPowerShellSyntax` (`:319-330`) already parses a
file list and refuses pwsh-7-only tokens. Adding one path is the smallest guard with one source of
truth. The scan's expected count moves from 18 to 19, and one expected name is added
(`ToolchainFloorWarningTests.cs:116-131`). A real `powershell.exe` parse stays a desktop check
(Rollout R-2).

## 4. Hot paths, one by one

| Stmt | Call site | Why that often | CARD-1403 / earlier | Smallest safe change | Staleness and invalidation | Must never be cached | Witness | Est. saved/s |
|---|---|---|---|---|---|---|---|---:|
| B1 | `PhoneHomeRunnerDirectory.GetBindingAsync` `:203-217`, through `RoutingSessionRunnerClient.Route` `:123-133` | Per routed runner call. Every Get/GetTranscript/GetSnapshot/SendInput/Kill/Observe through the singleton routing client reads the binding first, for local sessions too | Nothing on this path. The recovery pump's owner cache (CARD-0679 D-3) covers only pump events | S2a, D-6 | 30 s TTL, positive-only; insert-only columns | `Missing`; the park fence's re-read | V-10, V-11, V-12 | ~17.4 |
| B2 (part) | `AgentSessionRuntime.RecordActivityAsync` `:2673` | Per output chunk, coalesced to 1 per session per 1 s | CARD-0679 D-3 coalescing | S2b, D-7 | 5 s write interval | n/a (no read cached) | V-13, V-14 | ~4.0 + see B4/B5 |
| B2 (rest, ~9.8/s) | **UNOBSERVED** (full `AgentSessions` entity loads; the truncated text cannot separate the sites; M-2 and S1c) | UNOBSERVED | | S2c after S1c | | Any read that feeds delivery, TurnEnd, recovery or status decisions | S2c's own | UNOBSERVED |
| B3 | 214 `BeginTransactionAsync` sites plus multi-statement saves | UNOBSERVED | | None in this plan (D-9) | | | S1c (V-9) attributes | 0 claimed |
| B4 | `RecordActivityAsync` `:2677-2682` | Same write as B2's part | | S2b | | | V-13 | (in S2b) |
| B5 | `RecordActivityAsync` `:2687` | Same write | | S2b | | | V-13 | (in S2b) |
| B6 | `TranscriptIngestPrelude`/identity chunks (`AgentSessionRuntime.cs` persist path) | Whole-snapshot ingest | CARD-1403 S3; **CARD-1888 S3 landed** | none here | | | | (CARD-1888) |
| B7 | EF automatic savepoints around `SaveChanges` inside user transactions | UNOBSERVED | | None in this plan (D-9) | | | V-9 | 0 claimed |
| B8 | `AgentTaskLandNotificationService.ReconcileAsync` via `LandNotificationReconcileQueries` (`NoteById`, `SessionScanById`, `SessionStatusById`/`SessionStartedAtById`), from the 5 s hosted pass | About 15 un-parked notes per pass | CARD-1759/1769/1773 | None here (D-8); follow-up card | | **Everything**: receipt path | M-3, V-9 | 0 claimed |
| B9 | `ReconcileAsync` `:98` bump plus save | Same | | Same | | Same | | 0 claimed |
| B10 | Full `AgentTasks` entity loads: UNOBSERVED; `GET /api/agent-tasks/{id}` polling is one candidate | | | CARD-1972 owns the polling | | Task status where a decision reads it | | 0 claimed |

**Never cached by this card** (the delivery-evidence guard; Code and Review check every slice against
this list):

- transcript rows, and anything derived from them (receipts, the UserPrompt delivery verdict, TurnEnd,
  working/idle and prompt spans);
- `SessionQueuedMessages` state;
- land-notification state and its `ConcurrencyToken`;
- session `Status`, `StartedAt` (the generation), exit evidence;
- recovery attempts and runtime-action claims;
- task status, attempt and binding fields.

S2a caches only the insert-only runner binding. S2b caches nothing; it removes reads.

## 5. Slices

Each slice is its own commit group. The failing witness is committed before its fix (git-common).
Order: smallest first. S1a-S1c are independent of S2a-S2b, and the four may be built in parallel
Code tasks. S2c exists only if S1c names a site at or above 2 statements/s.

### S1a. `scripts/pg-qps.ps1` (D-2)

Files:
- new `scripts/pg-qps.ps1`
- `scripts/test-toolchain-floor.ps1` (one path in the C1698 list)
- `tests/Antiphon.Tests/Scripts/ToolchainFloorWarningTests.cs` (count 18 to 19, one name)
- new `tests/Antiphon.Tests/Scripts/PgQpsScriptTests.cs` (`[Category("Integration")]`,
  `[ParallelLimiter<ProcessSpawnLimit>]`)
- new `tests/Antiphon.Tests/TestHelpers/PgStatStatementsContainer.cs` (D-11)
- new `tests/Antiphon.Tests/Scripts/Fixtures/pg-qps/*.json` (offline snapshots)
- `docs/ops-http.md` (one line under database diagnostics)
- `docs/logs.md` (one pointer)

Tests:
- `C1987_live_window_counts_a_tagged_workload`: real `postgres:16` with the extension. The script runs
  with `-Container <fixture container> -IntervalSeconds 5 -Json` while the test issues 40
  `-- rt.c1987.<g>` statements. Asserts `calls = 40` for the tag and statements/s = 40 ÷ window within
  ±5 %.
- `C1987_output_never_contains_a_sent_literal`: the workload includes a literal string. Neither the
  `-Json` nor the text output contains it.
- `C1987_compare_reports_rates_and_top_lists`: offline. Two fixture snapshots give the expected per-tag
  rates, top-20 ordering by calls and by total time, `tx.*` grouping, and `untagged`.
- `C1987_compare_refuses_a_reset_window`: offline, `stats_reset` changed: exit 3,
  `reason=stats_reset`, no rates. A `calls` decrease is the same. A `dealloc` increase gives exit 0
  and `evicted=n`.
- `C1987_transport_failure_prints_no_connection_detail`: `-Psql` with an unreachable `PGHOST` and a
  sentinel `PGPASSWORD`: exit 2, and the output contains neither the sentinel nor the host.

### S1b. Continuous sampler and `GET /api/diagnostics/db-rate` (D-3, D-4)

Files:
- new `server/Infrastructure/Data/PgStatementRateSampler.cs` (sampler, ring, snapshot math)
- new `server/Application/Settings/DbRateSettings.cs` with a validator
- `server/Program.cs` (register the settings and the hosted service)
- `server/Api/Endpoints/DiagnosticsEndpoints.cs` (new `MapGet("/db-rate")`; `/session-state` unchanged)
- `server/appsettings.json` (no change: the defaults live in code)
- `docs/ops-http.md`

Tests: new `tests/Antiphon.Tests/Infrastructure/PgStatementRateSamplerTests.cs`:
- `C1987_two_ticks_report_the_tagged_rate`: real `postgres:16` with the extension; a `FakeTimeProvider`
  drives the ticks directly (`TickAsync`).
- `C1987_reset_between_ticks_is_a_gap_not_a_negative_rate`: `pg_stat_statements_reset()` on the
  throwaway database between ticks.
- `C1987_missing_extension_reports_unavailable_without_throwing`: the ordinary `TestDbFixture`
  database, no preload. `available=false`, `reason=pg_stat_statements_unavailable`, and the next
  tick tries once again, with no tight loop.
- `C1987_ring_keeps_retain_minutes_records`: pure; 1,500 synthetic ticks keep 1,440.
- `C1987_route_returns_records_without_parameter_text`: route shape; the text is at most 160
  characters and the sent literal is absent.

### S1c. Call-site attribution (D-5)

Files:
- new `server/Infrastructure/Data/DbCallSiteSampler.cs`
- `server/Program.cs` (add the interceptor beside `DbCommandMetrics`)
- `DiagnosticsEndpoints.cs` (`callSites` in `/db-rate`)
- `DbRateSettings.cs` (`CallSiteSampleEvery`)

Tests: new `tests/Antiphon.Tests/Infrastructure/DbCallSiteSamplerTests.cs`:
- `C1987_untagged_reads_are_attributed_to_their_method`: N=1, 25 untagged reads from a named test
  helper method. The key is that method.
- `C1987_transactions_and_savepoints_are_attributed`: a `BeginTransactionAsync` plus `SaveChanges`
  inside it. `tx.begin` and `tx.savepoint` both point at the method.
- `C1987_tagged_commands_are_not_sampled`.

### S2a. Routing binding cache (D-6)

Files:
- `server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs` (cache, optional
  `TimeProvider` and settings constructor parameters)
- `PhoneHomeRunnerDirectory.cs` (`TagWith("rt.runner.binding")` on the `:206-210` read; no other
  change)
- `server/Application/Settings/PhoneHomeRunnerSettings.cs` (`RoutingBindingCacheSeconds`, default 30,
  validator 0-300)
- `server/Program.cs:392` (factory registration passing `TimeProvider` and the options)

Tests: new `tests/Antiphon.Tests/Application/RoutingBindingCacheTests.cs`:
- `C1987_repeated_routed_calls_read_the_binding_once`: ten `GetAsync` calls for one remote session and
  ten for one local session. `PhoneHomeRunnerDirectory.BindingLookups` delta = 2. **Red at base: 20.**
- `C1987_missing_binding_is_not_cached`: a route to an absent id throws `NotFoundException`; the row is
  inserted; the next route succeeds without advancing the clock.
- `C1987_binding_is_re_read_after_the_ttl`: advance 31 s, and the lookups delta grows by 1.
- `C1987_park_fence_still_reads_the_database`: `TaskParkPublicationService`'s re-read after a row
  delete sees `Missing` inside the TTL.

**Program-booted tests.** Tests that resolve `ISessionRunnerClient` from a DI container and then change
an existing session's binding mid-test would see the cache. Code finds them with:

```
grep -l "GetRequiredService<ISessionRunnerClient>" tests/**/*.cs
```

intersected with the binding-write list in Section 7. It lists them in the S2a commit and adds their
classes to CP-9's filter if any exist. Its size is **UNOBSERVED**. If the row would exceed 10 minutes,
it is split.

### S2b. Activity heartbeat (D-7)

Files:
- `server/Application/Services/AgentSessionRuntime.cs` (`RecordActivityAsync` `:2661-2692` only;
  `ClaimActivityWrite` unchanged; lines last touched by `23763fa84`/`796457cf8`, not by
  CARD-1888/1969)
- `server/Application/Settings/AgentSessionSettings.cs:88` (default 5,000, comment)

Tests in `tests/Antiphon.Tests/Application/AgentSessionRuntimeActivityTests.cs`:
- `C1987_activity_write_is_one_command`: `QueryBudget` around one `ObserveOutputAsync` that triggers a
  write. **Red at base: 3 commands** (session load, attempt load, update). Budget 1, tag `rt.activity.touch`.
- `C1987_activity_write_updates_the_streaming_attempt`: an open streaming attempt gets
  `LastEventAt = now`; a completed attempt and a non-streaming attempt are untouched.
- `C1987_default_interval_is_five_seconds`: default settings; writes at t=0, nothing at t=4.9 s, a
  write at t=5.0 s. **Red at base: a write at 4.9 s.**
- The existing `Output_bursts_write_LastSeenAt_at_most_once_per_interval` is rewritten: it counts
  `rt.activity.touch` commands with `QueryBudget` instead of `SaveChangesInterceptor` (a raw update
  is not a tracked save). It pins the interval to 1,000 ms explicitly, so its 1,001 ms step still
  means one interval.

### S2c. (conditional) The top attributed `AgentSessions` entity load

Only if S1c names one method at or above 2 statements/s after S2b is active. The design follows the
same rules as S2a/S2b:
- prefer removing a read over caching it (projection, or reuse of an already-loaded row);
- cache only insert-only or monotonic data, with a TTL;
- never the guard list.

The plan for it is an amendment to this file, reviewed before Code. Its rows are not in the table
below.

## 6. Verification design

| V | Claim | Test | Red at base | Green |
|---|---|---|---|---|
| V-1 | The live window counts a tagged workload | `PgQpsScriptTests/C1987_live_window_counts_a_tagged_workload` | n/a (new script) | calls 40, rate ±5 % |
| V-2 | No literal leaks | `PgQpsScriptTests/C1987_output_never_contains_a_sent_literal` | n/a | literal absent |
| V-3 | Offline rate math and top lists | `PgQpsScriptTests/C1987_compare_reports_rates_and_top_lists` | n/a | equal to the fixture expectation |
| V-4 | A reset window is refused | `PgQpsScriptTests/C1987_compare_refuses_a_reset_window` | n/a | exit 3 |
| V-5 | A transport failure prints no connection detail | `PgQpsScriptTests/C1987_transport_failure_prints_no_connection_detail` | n/a | exit 2, sentinel absent |
| V-5b | 5.1 syntax | `ToolchainFloorWarningTests/C1698_WindowsPowerShellSyntax` | 18 of 19 expected lines (the new name missing) | 19 |
| V-6 | Sampler: rates, reset gap, missing extension, ring bound, route | `PgStatementRateSamplerTests/C1987_*` (5) | n/a (new) | 5 passed |
| V-9 | Call sites attributed, tagged commands skipped | `DbCallSiteSamplerTests/C1987_*` (3) | n/a (new) | 3 passed |
| V-10 | Routed calls read the binding once | `RoutingBindingCacheTests/C1987_repeated_routed_calls_read_the_binding_once` | 20 lookups | 2 |
| V-11 | `Missing` is not cached; TTL re-read | `RoutingBindingCacheTests/C1987_missing_binding_is_not_cached`, `.../C1987_binding_is_re_read_after_the_ttl` | n/a | pass |
| V-12 | The park fence is unchanged | `RoutingBindingCacheTests/C1987_park_fence_still_reads_the_database` | n/a | pass |
| V-13 | One command per heartbeat; attempt semantics kept | `AgentSessionRuntimeActivityTests/C1987_activity_write_is_one_command`, `.../C1987_activity_write_updates_the_streaming_attempt` | 3 commands | 1 |
| V-14 | Default interval 5 s; burst behaviour kept | `AgentSessionRuntimeActivityTests/C1987_default_interval_is_five_seconds`, `.../Output_bursts_write_LastSeenAt_at_most_once_per_interval` | write at 4.9 s | none at 4.9 s, one at 5.0 s |

A red row is expected to fail on its count assertion. A build error, zero executed tests or a
fixture error is not red. Red rows run once at their test commit and are not re-run after they go
green.

### Checkpoints

Lane: the runner-defaults global host (server2, Linux). Omit `-Runner` and `-Platform`. One isolated
build per row. Run through `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this file>`,
one run per `After` group, then `wait` until the exit is not 75. CP-1 and CP-6 depend on Docker on
the runner (`postgres:16` pull); a pull failure is reported as not run, never as red.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1a | `tests/Antiphon.Tests -> bin-c1987-s1a/` | c1987-s1a | `/*/*/PgQpsScriptTests/C1987_*` | V-1..V-5 | 5 passed, 0 failed/skipped | 5 | 15 |
| CP-2 | S1a | CP-1 | c1987-s1a-ps51 | `/*/*/ToolchainFloorWarningTests/C1698_WindowsPowerShellSyntax` | V-5b | 1 passed | 1 | 8 |
| CP-3 | S1b | `tests/Antiphon.Tests -> bin-c1987-s1b/` | c1987-s1b | `/*/*/PgStatementRateSamplerTests/C1987_*` | V-6 | 5 passed, 0 failed/skipped | 5 | 10 |
| CP-4 | S1c | `tests/Antiphon.Tests -> bin-c1987-s1c/` | c1987-s1c | `/*/*/DbCallSiteSamplerTests/C1987_*` | V-9 | 3 passed, 0 failed/skipped | 3 | 10 |
| CP-5 | S2a-red | `tests/Antiphon.Tests -> bin-c1987-s2a-red/` | c1987-s2a-red | `/*/*/RoutingBindingCacheTests/C1987_repeated_routed_calls_read_the_binding_once` | V-10 witness | 1 executed, failed: 20 lookups, expected 2 | 1 | 10 |
| CP-6 | S2a | `tests/Antiphon.Tests -> bin-c1987-s2a/` | c1987-s2a | `/*/*/RoutingBindingCacheTests/C1987_*` | V-10..V-12 | 4 passed, 0 failed/skipped | 4 | 10 |
| CP-7 | S2a | CP-6 | c1987-s2a-routing | `/*/*/(PhoneHomeSessionRoutingTests*)\|(TaskParkPublicationTests*)/*` | existing routing and park-fence behaviour | all executed passed, 0 failed/skipped; Code records the count | 1 | 10 |
| CP-8 | S2b-red | `tests/Antiphon.Tests -> bin-c1987-s2b-red/` | c1987-s2b-red | `/*/*/AgentSessionRuntimeActivityTests/(C1987_activity_write_is_one_command*)\|(C1987_default_interval_is_five_seconds*)` | V-13/V-14 witnesses | 2 executed, both failed on the count/interval assertion | 2 | 10 |
| CP-9 | S2b | `tests/Antiphon.Tests -> bin-c1987-s2b/` | c1987-s2b | `/*/*/AgentSessionRuntimeActivityTests/*` | V-13, V-14, the rewritten existing method | 4 passed, 0 failed/skipped | 4 | 10 |

Filter notes:
- Every OR operand is parenthesized and ends in `*` (CARD-0403).
- `C1987_` appears only in this card's new methods. `PhoneHomeSessionRoutingTests*` and
  `TaskParkPublicationTests*` have no longer-named sibling classes in `tests/Antiphon.Tests`; Code
  re-checks with `grep -rhoE "class (PhoneHomeSessionRoutingTests|TaskParkPublicationTests)\w*"`
  before import.
- CP-7's class sizes are **UNOBSERVED**. If either class exceeds the 10-minute row, Code narrows it
  to the routed-call methods and records the cut.
- Code runs `import --plan` on this file before the first run (memory: a table can refuse on import).

Import (plan task 70f587e0, 2026-11-18, at this file): `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <this file> --out .antiphon/c1987-import-2.yaml` through `build-slot.ps1` printed `imported 9 rows` with **0 warnings** (the first import warned that CP-2 and CP-7 duplicated a same-After build; they now reuse CP-1 and CP-6).

## Performance budget

- **Selection.** 9 rows, 7 cold `Antiphon.Tests` builds (CP-2 reuses CP-1, CP-7 reuses CP-6). Estimates are 15 + 8 + 10 × 7 = **93 min**
  of deadline input across seven `After` groups. These are deadlines, not expected durations. No row
  is expected near 30 minutes; any that passes 30 gets a Debug task with its timing. A cold
  `Antiphon.Tests` build on server2 is about 5-6 min (recent plans); the test bodies are seconds.
- **Phases.**
  - **Container start** (CP-1, CP-3, CP-6): `postgres:16` plus extension, once per test session
    (D-11). Readiness p95 is **UNOBSERVED**. Provisional bound: 60 s, about 2x a plausible 30 s
    cold start. The image pull on server2 is **UNOBSERVED**; the CARD-1974 cutover witness pulls
    the same tag.
  - **Database per test:** one `CREATE DATABASE` (sub-second).
  - **Migrations:** none for the throwaway (the tests issue their own tagged statements).
    `TestDbFixture` owns its container and migrations once per assembly (unchanged), used by V-6's
    negative control and by S2a/S2b.
  - **Teardown:** fixture dispose with the owner-label sweep.
  - **Build and slot wait:** reported on the `CHECKPOINT` line, not in test bodies.
- **Per test.**
  - V-1/V-2 wait the script's 5 s interval: about 8 s each.
  - Offline compare tests: under 2 s each (one pwsh child).
  - Sampler tests: FakeTimeProvider-driven, about 1 s each.
  - S2a/S2b: one session seed each, about 1-3 s.
  - **No new `[Timeout]`.** If Code adds one, it is about 2x the measured TRX p95, provisional 30 s
    (60 s for V-1) until a p95 exists. Code records per-test durations from the TRX.
- **Child processes.** The script tests spawn `pwsh` and `docker exec`. They are bounded by the
  script's own `WaitForExit` (interval + 30 s) and carry `ParallelLimiter<ProcessSpawnLimit>`.
- **Production.**
  - S1b: one read per minute (D-4).
  - S1c: about 0.4 ms CPU per second (D-5).
  - S2a: one dictionary lookup per routed call.
  - S2b: one statement per heartbeat instead of three.
  - **Stop rules:** S1b `sampleMs` p95 over 250 ms, or S1c CPU over 1 % of one core, raises the
    interval or N before anything else.

## 7. Existing tests affected

Impacted-classes table (CARD-1782): this plan changes no migration, public DTO, operation label,
remote script function or statement budget. It adds a diagnostics route and a tag. The affected
existing tests are:

| Class / method | Why | Change |
|---|---|---|
| `ToolchainFloorWarningTests/C1698_WindowsPowerShellSyntax` | Expected count and names (D-12) | 18 to 19, plus one name |
| `AgentSessionRuntimeActivityTests/Output_bursts_write_LastSeenAt_at_most_once_per_interval` | Counted tracked saves; S2b writes raw | Counts `rt.activity.touch`; pins 1,000 ms |
| Ten classes that call `new RoutingSessionRunnerClient(directory)` (`CodexCliDeliveryWorld`, `TaskParkRunnerIdentityTests`, `TerminalRunnerSeatReleaseTests`, `PhoneHomeSessionRoutingTests`, `PhoneHomeStandingLaunchTests`, `PhoneHomeRollingRunnerDrainTests`, `PhoneHomeRollingRunnerTests`, `DelegationDispatchRecoveryBoundaryTests.AbsentEvidence`, `MultiRunnerRecoveryTests`, `PhoneHomeStrandedQueueTests`) | Constructor | None: the one-argument form keeps the cache off (D-6) |
| DI-resolved routing clients whose test rebinds a session mid-test | Would see the cache | UNOBSERVED; found by S2a's grep and added to CP-7 |
| `PhoneHomeEventPumpTests` (`BindingLookups` assertions at `:252-268`) | Counts directory lookups | Unaffected: the pump calls the directory, not the routing client |
| Tests that assert `LastSeenAt` after output with the default interval | 1 s to 5 s default | Code greps `ObserveOutputAsync` plus `LastSeenAt` assertions; at base only `AgentSessionRuntimeActivityTests` sets the interval; others are **UNOBSERVED** and listed in the S2b commit |

## 8. Collisions, order, rollout

**Collisions:**
- **CARD-1888 and CARD-1969** (landed): their lines in `AgentSessionRuntime.cs` (persist, prelude,
  stale-Working probe) and `TranscriptPromptSpan.cs` are not touched. S2b edits only
  `RecordActivityAsync` (`:2661-2692`), whose lines are older (`23763fa84`, `796457cf8`).
- **CARD-1889** (Review; plans `DbCommandMetrics.LeadingTag`, `LiveTiming`, `Program.cs`): this plan
  does not edit `DbCommandMetrics.cs` or `LiveTiming.cs`. Both plans add registrations in
  `Program.cs`; a textual merge only. If CARD-1889 lands first and moves the tag allowlist into
  `Antiphon.Observability`, S1c reads it from there.
- **CARD-1984 and CARD-1893:** test/compose only, no overlap.
- **CARD-1972:** owns `GET /api/agent-tasks/{id}` polling (B10). Not touched.
- **CARD-1915** (summary table, flag off): not touched.

**Order:** S1a, S1b, S2a, S2b can run as parallel Code tasks (disjoint files except `Program.cs`). S1c
follows S1b (it shares the route and settings). S2c waits for S1c's reading on the desktop.

**Rollout:**
- **R-1.** Master first, then an rc port of S1a-S2b. These are performance and diagnostics changes,
  not bug fixes. Porting them to the current rc is the orchestrator's call under the standing rule
  (reviewed rc ports). There is no migration. Activation is an AppHost restart
  (`scripts/restart-apphost.ps1` from the main checkout), confirmed by `GET /api/version`.
- **R-2.** After S1a lands, run `powershell.exe -NoProfile -File scripts\pg-qps.ps1 -IntervalSeconds 60`
  once on the desktop. That is the real 5.1 proof; the orchestrator does it, not this task.
- **R-3.** After activation, read `/api/diagnostics/db-rate?minutes=60` twice, an hour apart.
  Record on the card: total statements/s, `rt.runner.binding` and `rt.activity.touch` rates against
  D-10, and the S1c top call sites. That reading decides S2c and the D-8 follow-up card.

**Checklist (stage rule):** this plan adds or changes no session that waits for input. Nothing here
parks, releases or stops a session.

## 9. Operator decisions (yes/no, with a recommendation)

| Q | Decision | Recommendation |
|---|---|---|
| Q-1 | Accept that the committed slices reach about 17 % (28.6 of 167.5 statements/s), with the 30 % target depending on S2c and the land-reconcile follow-up card? | **Yes.** The other big rows are delivery-receipt or unattributed. Cutting them blind would break the brief's "no behaviour change". |
| Q-2 | Raise `AgentSessions:ActivityWriteMinIntervalMs` from 1,000 to 5,000 ms? | **Yes.** Every reader works in minutes or 60+ seconds (D-7). Without it, S2b saves 8.0/s instead of 11.2/s. |
| Q-3 | Run the continuous sampler (S1b) and the call-site attribution (S1c) on by default? | **Yes**, both. One read a minute; about 0.4 ms CPU per second. The settings can turn either off without a deploy of code. |
| Q-4 | File a separate card for the land-reconcile residue (about 11.9 statements/s) once M-3 and S1c attribute it? | **Yes.** It is the receipt path and needs its own proof (D-8). |
| Q-5 | Routing binding cache TTL 30 s? | **Yes.** The binding is insert-only. 30 s bounds a deleted row and any future writer. Raising it saves little more (about 20 sessions / TTL). |

---

## Decisions

The plan is written under these stated defaults. Each can be overruled without changing the rest.

- D-1 to D-12 in Section 3.
- Q-1 to Q-5 are taken as their recommendations until the operator answers.
