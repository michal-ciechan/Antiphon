# CARD-1969: attribution of the whole-session transcript reads behind Postgres CPU

Date: 2026-11-17. Investigate task c747c0c4. Read-only. No SQL was run against any database.

- Source read: `origin/master` **7901e9ba537b757740c2913aa2211d513b0b31f2**.
- The desktop server reports `GET /api/version` = `870a93d21f2b18c85a8b0905ab689b28e3099813`
  (`rc/2026-11-24` tip). `git diff 870a93d21 7901e9ba5` is empty for every file cited below
  (AgentSessionRuntime, StaleWorkingTranscriptRecovery, StaleWorkingTranscriptSettings,
  TranscriptPromptSpan, TaskReportTurnSelector, AgentTaskDispatcher, PolicyRefreshService,
  TranscriptIngestPrelude, SessionStateLoader, LandNoteReceipt, CheckCompactionContinuationService,
  appsettings.json). The attribution therefore holds for the running server.
- Measurement input: the orchestrator's pg_stat_statements delta on desktop Postgres,
  2026-11-17 09:24:53Z to 09:35:40Z (647 s), as quoted in the brief. That data was not re-measured here.

## 1. Verdict

| # | Statement (as measured) | Code | Confidence |
|---|---|---|---|
| 1 | full-row `SELECT t."Id", t."Sequence", t."Kind", t."Uuid", t."ParentUuid", t."Timestamp", t."Role", t."Text", ...` | `AgentSessionRuntime.ReadRecoveryObservationAsync`, `server/Application/Services/AgentSessionRuntime.cs:1176-1188` (projection `RecoveryRow`, `:1155-1159`) | **Confirmed by shape.** It is the only projection in `server/` and `src/` whose select list starts `Id, Sequence, Kind, Uuid, ParentUuid`. An entity read would also select `AgentSessionId`. `GetTranscriptAsync`'s DTO starts at `Sequence`. |
| 2 | `-- session-state.seed WITH open_tasks AS MATERIALIZED` | `SessionStateLoader.LoadAsync`, `server/Infrastructure/Data/SessionStateLoader.cs:39-97` | Confirmed (tag) |
| 3 | `SELECT t."Sequence", t."Text", t."Timestamp", t."Kind"` | `TranscriptPromptSpan.LoadAsync`, `server/Application/Services/TranscriptPromptSpan.cs:88-94` (`PromptRow`) | Confirmed (unique projection) |
| 4 | `SELECT t."Timestamp", t."CreatedAt" ... WHERE t."AgentSessionId" = $1` | `PolicyRefreshService.HasBeenIdleLongEnoughAsync`, `server/Application/Services/PolicyRefreshService.cs:529-541` | Confirmed (unique projection) |
| 5 | `-- session-state.identity SELECT t."Uuid", t."Kind"` | Second and later 512-UUID chunks in `PersistTranscriptCoreAsync`, `AgentSessionRuntime.cs:1449-1457` | Confirmed (tag + shape) |
| 6 | `-- session-state.identity SELECT $4 AS "Part", s."StartedAt"` | `TranscriptIngestPrelude.ReadAsync`, `server/Infrastructure/Data/TranscriptIngestPrelude.cs:22-43,76-78`. `0 AS "Part"` is normalised to `$4`. | Confirmed (tag + shape) |
| 7 | `SELECT t."Sequence", t."Text" FROM "TranscriptEntries"` | **One of four sites** (section 3.7). The truncated text cannot tell them apart. | NOT attributed: UNOBSERVED |

**The top statement is the CARD-1205 S4 stale-Working checked recovery.**
`StaleWorkingTranscriptRecovery` probes every local, Running session whose transcript reads as Working.
Each probe reads the **entire** session transcript, every column including `Text` and `ToolInput`,
between one and four times. The read has no row cap, no `Sequence` window and no tag.
Transcript retention removes a session only when its newest row is older than
`TranscriptRetentionDays` (7). See `DataRetentionService.cs:345-372` and `RetentionSettings.cs:14`.
A live session is therefore never trimmed.

## 2. Table

Window 647 s. A CPU share is `calls × mean / 647 s`. A percentage is of the measured 0.54 cores.

| # | file:line | Caller and trigger | Frequency (code) | Measured calls | Rows/call | Columns selected → used | Exec share | Covered by |
|---|---|---|---|---:|---:|---|---:|---|
| 1 | `AgentSessionRuntime.cs:1182-1187` | `StaleWorkingTranscriptHostedService` → `StaleWorkingTranscriptRecovery.TickAsync`/`Admit`/`ProbeAsync` (`StaleWorkingTranscriptRecovery.cs:52,162-189,192-201`) → `ProbeWorkingTranscriptAsync` (`AgentSessionRuntime.cs:1046`): reads `before` `:1062`, `current` `:1095`, `fenced` `:1534` (inside `PersistTranscriptCoreAsync`, under `LockAgentSessionRowAsync` `:1528`), `after` `:1124` | Per tracked Working session: about every 120 s after a non-Unknown outcome (`IntervalSeconds`). After an Unknown outcome: every pass, but no faster than 15 s since the last attempt (`PollSeconds`; the host loop waits 15 s after each pass). Reads per probe depend on the outcome (section 3.1). | 19 (1.76/min) | 413,635 | all 25 columns → see section 3.1 | 99.7 s = **0.154 core, 28.6 %** (lower bound, section 4) | **UNCOVERED.** CARD-1888 S2 changes how `:1110` persists, not this read. |
| 2 | `SessionStateLoader.cs:97` | `SessionStateStore.LoadAsync` (`SessionStateStore.cs:199-205`): cold or faulted cache entries in a read, and `Lease.ReconcileAsync` (`:304`, e.g. `NeedsReload` after a persist, `AgentSessionRuntime.cs:1382`) | Cache-miss driven. The per-trigger split is UNOBSERVED. The CARD-1915 plan records the same gap. | 26 | n/a | 9 transcript laterals per session | 48.4 s = 0.075 core, 13.9 % | **CARD-1915** (summary table; flag `SessionState:DurableTranscriptSummaryEnabled` default off; activation is a separate commissioning). CARD-1933 is the advisory on its invalidation. |
| 3 | `TranscriptPromptSpan.cs:88-94` | (a) `AgentTaskDispatcher.SettleDeferredReportsAsync` (`AgentTaskDispatcher.cs:4890-4940`) on **every dispatcher tick**: for each open (Dispatched/Working) task with a session, `TaskReportTurnSelector.SelectAsync` (`TaskReportTurnSelector.cs:64`) runs until one is selected. (b) Settlement `ExtractMarkedTurnAsync`: `SelectAsync` plus a second `LoadPromptsInSpanAsync` (`AgentTaskReplyService.cs:3357,3412`). (c) Delivery watchdog `FailNeverStartedAsync` (`AgentTaskDispatcher.cs:2511`). (d) `CardTaskSettlement.cs:261`. (e) `LateFailedReportForwarder.cs:99`. (f) `HasTurnPromptAfterAsync` (`ApiErrorRecoveryService.cs:302`, `AgentTaskReplyService.cs:1972,1977`) | (a) `Delegation:PollIntervalSeconds` default **5 s** (`DelegationSettings.cs:28`; no override in `server/appsettings.json`; a desktop override is UNOBSERVED). 609/647 s = 0.94/s, about 4.7 per 5 s tick. That fits roughly five open sessions, but the split between callers is UNOBSERVED. | 609 | 971 | Sequence, Text, Timestamp, Kind → all four are used (section 3.3) | 45.1 s = 0.070 core, 12.9 % | **UNCOVERED for the DB read.** CARD-1910 (floor index) and CARD-1914 (span TrimStart) cut in-process CPU over the same rows. Neither reads fewer rows. CARD-1969 names this read. |
| 4 | `PolicyRefreshService.cs:532-535` | `AgentSupervisorHostedService` (`:46,120-123`, every 1 min) → `SweepAsync` → per standing non-pool agent with a Running persistent session → `ActAsync` → only when `PolicyDrift.HasDrift` → `PrepareToActAsync` → `EvaluateGatesAsync` twice (`:247,254`). Each evaluation runs this read even when `working` is already true (gates are evaluated eagerly, `:500-505`). | Up to 2 per drifted standing agent per minute. Measured 4.6/min. | 50 | 87,078 | Timestamp, CreatedAt → only `max(Timestamp ?? CreatedAt)` | 16.4 s = 0.025 core, 4.7 % | **CARD-1888 S4**, committed `4fd6137fae24a30aecb6af6e958438bdd31f1f4b` on `feat/card-task-6ea96bcb`, not landed: `MaxAsync` in SQL. One row is shipped, but the session's rows are still visited, because no index serves `max(COALESCE(...))`. |
| 5 | `AgentSessionRuntime.cs:1449-1457` | Whole-snapshot ingest from nine call sites (CARD-1888 plan §1), including `:1110` of statement 1's probe | 2,550 / 107 preludes = 23.8 extra chunks per prelude, about 12,700 UUIDs per snapshot (inference) | 2,550 | 629 | Uuid, Kind → both | 13.0 s = 0.020 core, 3.7 % | **CARD-1888 S2+S3** (prefix memo; in Code) |
| 6 | `TranscriptIngestPrelude.cs:76-78` | Same nine sites. One prelude per persisted batch that carries UUIDs. | 107 | 107 | 4,316 | Part, StartedAt, max(Sequence), Uuid, Kind → all | 11.7 s = 0.018 core, 3.3 % | **CARD-1888 S3** (stored-prefix check in this command). The 4,316 rows/call is above the 512-key cap plus kinds, so most rows are probably part 2: null-UUID sequences already stored. Inference, UNOBSERVED. |
| 7 | one of: `LandNoteReceipt.cs:59-66` (`FirstReceiptAsync`), `CheckCompactionContinuationService.cs:1098-1101` (CompactBoundary), `:1106-1112` (interrupt-prefix prompts), `:1124-1131` (UserPrompt after the open-prompt start; whole session when `start == long.MinValue`) | receipt scans (land notes, provider-wait, pipeline handoff, expectation snapshot, dispatcher `:5630`) or the Check-compaction fact load (cached by `TranscriptStampAsync` fingerprint, `:1054-1063`) | UNOBSERVED | 24 | 5,205 | Sequence, Text → both | 5.8 s = 0.009 core, 1.7 % | UNOBSERVED until the full statement text is read (section 3.7) |

Statements 1 to 7 account for 240 s of the window's 349 s of statement time (68.8 %).

## 3. Per-statement detail

### 3.1 Statement 1: who runs it about 1.75 times a minute, and why every row and column

**Caller.** `StaleWorkingTranscriptHostedService` loops: `TickAsync`, then a delay of `PollSeconds`
(15 s; `server/Infrastructure/Supervision/StaleWorkingTranscriptHostedService.cs`).
`TickAsync` reconciles and discovers tracked sessions in pages of 128. A tracked session is
`Status == Running`, has a local runner and no RunnerStoreId/RunnerCwd, and
`TranscriptWorkingStateQuery` reads it as busy. `Admit` then starts up to 4 probes.
`Due()` (`StaleWorkingTranscriptRecovery.cs:160`) is true when the session has never succeeded,
or when `IntervalSeconds` (120) have passed since the last success. An Unknown outcome never
sets `LastSuccess` (`:216`), so such a session is re-admitted on every pass once 15 s have
passed since its last attempt (`:170`).

**Reads per probe, by outcome** (`AgentSessionRuntime.cs:1046-1152`):

| Outcome reason | Statement-1 reads |
|---|---:|
| `ownership_unavailable` | 1 |
| `session_mismatch`, `generation_mismatch`, `incomplete_snapshot`, `frontier_missing`, `activity_frontier_absent` | 1 |
| `ownership_changed`, `frontier_changed`, `persist_failure_retained`, `runner_working` (CheckedWorking) | 2 |
| fence rejection inside persist (`ownership_changed_at_commit`, `frontier_changed_at_commit`) | 3 |
| persist path to the end (`persist_uncertain`, `ownership_changed_after_persist`, `committed_working`, `publication_working`, `committed_idle`) | 4 |
| `cancelled` / `observation_failed` (10 s `ProbeTimeoutSeconds` budget) | 0 to 4 recorded; a cancelled statement is not counted (section 4) |

**Fit to 1.76/min (hypotheses, not measured).** Suppose one Working session of about 414,000 rows
returns `runner_working` every 120 s. That is 2 reads per 2 min, or 1/min. Two such sessions, or one
session whose probes end Unknown and retry every 15 to 30 s with one counted read each, give 2 to 4/min.
A 5.2 s mean read plus a full runner snapshot pull (`GetTranscriptAsync`, `:1071`) leaves little of the
10 s budget. A `cancelled` outcome is therefore plausible, but UNOBSERVED. The server log settles it:
section 6, M-1.

**What the probe uses from the rows:**

- `Rows.LastOrDefault()?.Sequence`: the stored maximum (`:1052,1063,1096,1129`). One value.
- `frontier = before.Rows.LastOrDefault(RecoveryActivity)` (`:1082`): the newest activity row. It needs
  `Kind`, `Text` and `Timestamp` of the rows scanned back from the end. Then
  `RecoveryIdentityMatches(frontier, e)` (`:1195-1203`) compares **all 24 data columns of that one row**
  against the runner snapshot.
- `current.Rows.SequenceEqual(before.Rows)` (`:1102`) and `fenced.Rows.SequenceEqual(expectedRecovery.Rows)`
  (`:1540`): **whole-revision equality**. Record equality covers every column of every row. This is the
  only use that needs every row and every column. It guards against any change between the two reads
  by a writer outside the lease. Per the comment at `:1080`, "T13 protects the whole revision, including
  annotations". It sits alongside the lease's `ServerEpoch`/`Revision`/`ResetEpoch` check at `:1101-1102`.
- `after.Rows`: only the `Id` set, to count `addedCount`, and the last `Sequence` (`:1124-1130`).
  Both feed the result and the log line. Ownership comes from the session row, not from transcript rows.

So the read ships `Text`/`ToolInput` for all rows of the session to answer three things:
- the stored maximum;
- the identity of one frontier row;
- "did anything at all change between two reads".

The `fenced` read also runs inside a transaction that holds the `AgentSessions` row lock
(`LockAgentSessionRowAsync`, `:1528`). For a session of about 414,000 rows, that holds the session-row lock for the
read's duration, about 5 s mean on the measured data. Lock waits are UNOBSERVED.

### 3.2 Statement 2 (seed)

Each requested session runs nine `LATERAL`s: count and max effective timestamp over all its rows,
plus seeks for last row, end, end timestamp, activity, TurnEnd, TurnTitle and UserPrompt.
`count(*)` and `max(COALESCE(...))` visit every row of the session. CARD-1915 measured that the
aggregate stays transcript-bound without its summary table. It is already planned and partly landed
(`AddSessionTranscriptSummaries` migration on master; feature flag off). Nothing new here.

### 3.3 Statement 3 (prompt span): rows and columns

`TranscriptPromptSpan.LoadAsync` reads **every** `UserPrompt`/`QueuedUserPrompt` row of the session.
It uses `IX_TranscriptEntries_AgentSessionId_Kind_Sequence` for the filter but returns `Text`.
The `Timestamp > dispatchedAt` window is applied in C# (`:96-98`), not in SQL.

What the callers use:

- `Sequence`: ordering, and the delimiters `LastBefore` / next raw prompt.
- `Kind`: the queued-only log and the delimiters.
- `Timestamp`: the dispatch window.
- `Text`: housekeeping classification (`IsHousekeepingPrompt`, `TryReadLocalCommandName`,
  `IsAllowlistedPrompt`, `IsTaskNotificationPrompt`) and marker matching.

`RawPrompts` (all rows, including pre-dispatch rows) is consumed by `TaskReportTurnSelector`.
It is the delimiter set, and it supplies the pre-dispatch candidate that the time check rejects
(`TranscriptPromptSpan.cs:69-74`). A warm-pool or reused session inherits all earlier prompts,
so rows per call grow with the session's lifetime: 971 now, 1,503 in the 44 h CARD-1888 sample.

The selector's sibling read of all `TurnEnd` rows (`TaskReportTurnSelector.cs:83-87`,
`BoundaryRow`) is also unbounded. It is not in the reported top 7, so its share is UNOBSERVED.

### 3.4 Statement 4 (policy idle)

Only `max(Timestamp ?? CreatedAt)` is used. The session-state snapshot already carries
`NewestEffectiveTimestamp` (`SessionStateLoader.cs:43,53-54`). CARD-1888 S4 moves the max into SQL.

### 3.5 and 3.6 Statements 5 and 6 (ingest identity)

These are re-probes of already-stored UUIDs and sequences when whole runner snapshots are replayed.
That is the CARD-1888 diagnosis and fix. Statement 1's probe adds to it: each probe that reaches
`:1110` ingests the whole runner snapshot again.

### 3.7 Statement 7: how to tell the four candidates apart

The four candidate sites have different WHERE clauses.

- **LandNoteReceipt:** `t."Text" IS NOT NULL`, `Kind = 'UserPrompt'` (or `IN` with `QueuedUserPrompt`),
  then either `Sequence > $n` or `Timestamp >= $n`, and `ORDER BY t."Sequence"`.
- **Compaction CompactBoundary:** `Kind = 'CompactBoundary'`, with no ORDER BY.
- **Interrupt prompts:** `strpos`/`LIKE` on `Text` (EF's `Contains` translation).
- **Compaction prompts:** `Kind = 'UserPrompt'`, an optional `Sequence > $n`, no ORDER BY, and no `Text IS NOT NULL`.

The full `query` column of that queryid identifies the site.

### Related whole-session read not in the top 7

`CheckCompactionContinuationService.TranscriptStampAsync` (`:1067-1077`) runs count, max and
`SUM(length(Text))` over the whole session. Its share is UNOBSERVED in this window.

## 4. Caveats on the CPU shares

- pg_stat_statements `total_exec_time` is the leader backend's elapsed time. The CPU of parallel
  workers is not added. The live sample showed one SELECT with two parallel workers at about 72 to 75 %
  each. If that was statement 1 (UNOBSERVED; the sampled backend's query text was not attributed), its
  real CPU is up to about 3× its 0.154 core.
- A statement cancelled by the 10 s probe budget never completes, so pg_stat_statements does not count it.
  Its CPU is still spent. Statement 1's true call count can exceed 19.
- The server also materialises about 414,000 `RecoveryRow`s, with all text, per read, and compares two
  such arrays by value (`:1102`, `:1540`). Server-side allocation and CPU for this are UNOBSERVED.
  CARD-1910/1914 traces predate this measurement.
- Which sessions are largest is UNOBSERVED; no SQL was allowed. A mean of about 414,000 rows per call
  against the 1.69M-row table recorded on 2026-11-13 (CARD-1888) means the probed session or sessions
  hold a large fraction of the table. Long standing orchestrator or Check sessions are the likely
  candidates, but that is inference.

## 5. Plan sketch for the uncovered reads (for the Plan stage; not designed here)

**Invariants every option must keep.**
- Transcript-confirmed UserPrompt is the delivery verdict (`docs/session-runtime-invariants.md` §Input
  uncertainty, lines 1-20, and receipt rules throughout). A cheaper read must never hide a stored prompt.
  It must never treat a prompt as absent because it lies outside a window, unless the window is proven
  to contain every candidate.
- CARD-1205 S4 (`session-runtime-invariants.md:343-370`): Unknown retains debt. A checked recovery must
  still reject any change of the revision between the reads. The CARD-1888 plan records that production
  deletes exist (retention, cascade), so "rows are only appended" must not be assumed. A middle delete
  has to be detected (CARD-1888 W-3, W-5, W-6 pattern).
- Any index or summary migration is a **HUMAN-ONLY gate**. The CARD-1915 decision accepted additive
  migrations for that card only; a new one needs its own decision.

**Statement 1 (largest, uncovered).** Options, cheapest first:

- (a) **Revision stamp in SQL instead of rows.**
  - Replace the `before`/`current`/`fenced` row arrays with an aggregate computed by Postgres:
    `count(*)`, `max("Sequence")`, `min("Sequence")`, the `Id` at max, and a digest over
    `(Id, Sequence, Kind, md5(coalesce(Text,'')) ...)` or over `xmin`.
  - Read the frontier row alone with `ORDER BY Sequence DESC` and the activity predicate, `LIMIT` small.
  - The server is still O(rows) but ships one row. Pros: no migration, keeps whole-revision detection.
    Con: CPU is still O(rows) in Postgres.
- (b) **Bounded read using the CARD-1888 prefix argument.**
  - Compare `count(*) FILTER (Sequence <= S)`, the first row `Id` and the row `Id` at `S`, as in CARD-1888 S3.
  - Read full rows only above `S` (the previous probe's stored max).
  - Coordinate with CARD-1888 S3, which adds the same checks to the prelude.
- (c) **Use the durable summary (CARD-1915)** as the revision stamp once activated. This depends on
  CARD-1915 activation and on CARD-1933 (EF batch appends invalidate the summary).
- (d) **Avoid the probe for sessions the live stream already keeps fresh.** This is a scheduling change
  in `StaleWorkingTranscriptRecovery`. It changes CARD-1205 S4 semantics and needs the T18-T35 witnesses.

Separately: move the `after` read to a count/max, since only telemetry consumes it. Bound the `fenced`
read's lock hold.

**Statement 3 (uncovered for rows read).**
- Push `Timestamp > dispatchedAt OR Timestamp IS NULL` into SQL for `TurnPrompts`.
- Keep a bounded `RawPrompts` that still holds the delimiters the selector needs, for example the newest
  prompt at or before dispatch plus everything after. Prove with a witness that the selector's result is
  identical on long inherited histories.
- Alternatively, memo per `(session, max prompt Sequence)` within one dispatcher tick, because
  `SettleDeferredReportsAsync` re-reads the same span for every open task of a session.
- Text stays selected, because classification needs it. The CARD-1914 span work already removed the copies.
- Witness: a counting `DbCommandInterceptor`, as in the CARD-1888 S1 `IdentityProbeCounter`. Rows
  materialised per call must not grow with inherited history length. The delivery-evidence focus must
  be in its own Review round, as CARD-1969 requires.

**Statement 4:** land CARD-1888 S4. Optionally read `NewestEffectiveTimestamp` from `SessionStateStore`
and skip the idle read when `working` is already true. That second change is a gate-order change and
needs its own witness.

## 6. What the orchestrator should measure next

- **M-1 (statement 1 cadence and outcome).**
  - Source: the desktop server Serilog for the same window (`scripts/logs.ps1 -Source desktop-server`).
  - Count `Stale Working probe due` and `Stale Working probe result` lines by `SessionId` and `Reason`,
    with `pre`, `duration`. Also count `probe unknown` warnings.
  - Expect counted reads ≈ Σ(reads per outcome) from section 3.1. `Reason=cancelled` or `probe_fault`
    confirms the timeout hypothesis.
  - The `SessionId`s name the large sessions without any SQL.
- **M-2 (statement 1 full CPU).** Run `SELECT pid, leader_pid, query_id, state, query FROM pg_stat_activity`
  sampled during a burst. This ties the 72-75 % parallel workers to statement 1's queryid. Per queryid,
  pg_stat_statements also gives `shared_blks_read/hit`, `temp_blks_*` and `jit_*`.
- **M-3 (statement 7).** Read the full `query` text of statement 7's queryid and apply section 3.7.
  Do the same for the unlisted `TaskReportTurnSelector` TurnEnd read and the `TranscriptStampAsync`
  aggregate, to price them.
- **M-4 (sizes).** Session sizes by `AgentSessionId`. This is one read-only aggregate, off-peak. Do not
  run it in the hot window; it is a full-table pass. Alternatively, read the `pre` sequence in M-1's log
  lines, which is free.
- **M-5 (config).** Check whether the desktop overrides `Delegation:PollIntervalSeconds` or the
  `StaleWorkingTranscript` section (environment variables or appsettings on `C:\src\Antiphon\server`).
  The cadences above assume the code defaults.
- **M-6 (after each deploy).**
  - Take pg_stat_statements deltas per queryid over a similar 10 min window: calls, rows/call and mean.
  - Statement 1: rows/call should drop from about 414,000 to O(1) or O(new rows).
  - Statement 3: rows/call should stop tracking session lifetime.
  - Statement 4: rows/call = 1 after CARD-1888 S4.
  - `auto_explain` (`log_min_duration = 2s`, `log_analyze` off) on a scratch copy only, never production
    without an operator decision.
- **Feature-flagged caller stack, if M-1 is inconclusive.** Add `TagWith("rt.recovery.observe")` to
  `ReadRecoveryObservationAsync` and `TagWith("prompt-span")` to `TranscriptPromptSpan`, so
  pg_stat_statements names the caller directly. This is a code change, so it is for the Plan stage.

## Not done, noted

- No SQL, no production log read (desktop files are not reachable from the server2 runner), and no
  build or test.
- Fix idea, one line: replace the CARD-1205 probe's whole-row revision arrays with a SQL revision stamp
  plus a bounded tail read, using the CARD-1888 prefix checks.
