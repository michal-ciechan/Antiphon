# CARD-1969 slice 2 plan: the prompt-span read must not re-read a whole session

Date: 2026-11-17. Plan task 2048f2d0 (Grok). Plan only: no code, no setting change, no restart and no SQL
against any production database.

- Source read: `origin/master` **7901e9ba537b757740c2913aa2211d513b0b31f2** (also this branch's base).
- Attribution note: `origin/feat/card-task-c747c0c4` commit `c684172fc9ba93b161b87b73faf006599ca34ef4`,
  sections 3.3 and 5 (statement 3). No SQL was run there or here.
- Slice 1 plan: `origin/feat/card-task-8626ac28` tip `804edf5f8015ad823988fc35e8c544b8bc386b03`.
  Its plan review is `origin/feat/card-task-39f3b245` at `57d7a637e5f2273ff746f7a0dcd083a0b1a70bb7`
  (the clean review commit; corrected in amendment 1, A-5). That review asked for no plan amendment.
- CARD-1888 plan: not on this master. Read from `origin/feat/card-task-d1556676` at
  `57563753c7544c45661cc68619b203d87a2684ec` (`docs/superpowers/plans/2026-11-17-card-1888-transcript-ingest-probe-plan.md`).
  F-2 is taken: the prompt span is split out of that card. Code tip
  `origin/feat/card-task-6ea96bcb` = `cd222da3c684fa3b049502fd0ce956c77b7a9a0c` edits
  `AgentSessionRuntime.cs`, `TranscriptIngestPrelude.cs`, `TranscriptIngestPrefix.cs` and
  `PolicyRefreshService.cs`. It does not edit `TranscriptPromptSpan.cs`.
- Delivery verdict: `docs/session-runtime-invariants.md` (a complete normalized `UserPrompt` remains the
  delivery verdict, around the CARD-0778/0861 paragraph). A cheaper read must not hide a stored prompt
  and must not decide from a stale prefix.
- Runner defaults read this session: `GET /api/runner-defaults` revision 2, `globalRunnerId` `server2`.
  `GET /api/session-runners`: `desktop` windows dispatch-eligible, `server2` linux dispatch-eligible,
  `server2-temp` retired. Checkpoints name that lane and omit `-Runner` and `-Platform`.

### Amendment 1 (plan review round 1, `origin/feat/card-task-9dbbe5bf` at `c9d2bcded61269cfcd33cb38029cfd3437d0f396`)

Amendment task 52484483 (Claude Opus), 2026-11-17. Diff-only from `241f89463977945e43d7e8414edd5221501ba531`.
Re-read: `origin/master` **57563753c7544c45661cc68619b203d87a2684ec**. `git diff 7901e9ba5 57563753c`
touches none of `TranscriptPromptSpan.cs`, `TaskReportTurnSelector.cs`, `AgentTaskDispatcher.cs`,
`TranscriptPromptSpanTests.cs` or `docs/session-runtime-invariants.md`. Line numbers below stay valid.

| Finding | Where it is answered |
|---|---|
| B-1 stamp and rows from different snapshots | §3 "The stored stamp is derived from the stored rows", "Miss path"; D-2, D-11; W-7 |
| B-2 cache concurrency and memory | §3 "Cache shape"; D-7, D-12, D-13; W-8, W-9; §8 M-7; F-5 |
| A-1 stamp ids scan every prompt entry | §3 "Stamp" (one `LIMIT 1` lateral per kind); §6 Performance budget; §8 |
| A-2 in-place `Kind` / `Timestamp` edits too | D-4, D-8, F-1; CP-5 and CP-6 run the nine classes that edit prompt rows in place |
| A-3 CP-4 runs 13 | CP-4 `minExecuted` 13 |
| A-4 W-4 wording | W-4 asserts by sequence and text (`PromptRow` keeps no `Id`) |
| A-5 slice 1 review ref | Header line above |
| A-6 owner doc and database key | S3 doc paragraph in `docs/session-runtime-invariants.md` (D-14); cache key includes the connection string (D-12) |

One more defect found while amending: the original S1 was "no production change", but its
witnesses called `TranscriptPromptSpan.ClearCacheForTests`, which does not exist at base, so the
CP-1 build could not compile. S1 now adds an **inert** cache seam (stores nothing, changes no
read) so the red witnesses compile and stay red for the right reason (§4).

## 1. What the read returns, and what each caller uses

`TranscriptPromptSpan.LoadAsync` (`server/Application/Services/TranscriptPromptSpan.cs:80-120`) does
**not** return "the rows from one UserPrompt to the end" and **not** "the last prompt". The SQL at
`:88-94` materialises **every** `UserPrompt` and `QueuedUserPrompt` row of the session
(`Sequence`, `Text`, `Timestamp`, `Kind`), ordered by `Sequence`. The time window is applied in C#
(`:96-98`), not in SQL.

`Result` has three lists:

| List | Contents | Built |
|---|---|---|
| `RawPrompts` | Every prompt row of the session, including pre-dispatch and housekeeping | The SQL list, unfiltered (`:119`) |
| `TurnPrompts` | Rows with `Timestamp == null` or `Timestamp > dispatchedAt`, minus housekeeping and the provider allowlist | C# over that list (`:96-98`, `:115-116`) |
| `Notifications` | In-window rows whose text is a task-notification | C# (`:117-118`) |

`HasTurnPromptSinceAsync` (`:128-130`) is `TurnPrompts.Count > 0`. `HasTurnPromptAfterAsync`
(`:133-136`) loads with `dispatchedAt: null` (no time cut) and asks whether any `TurnPrompt` has
`Sequence > afterSequence`. `IsHousekeepingPrompt` (`:151-155`) is pure; it does not read the database.

A command-name set is collected from the **in-window** rows (`:102-105`) and is an input to
housekeeping. Rules-refresh ids are a separate `SessionQueuedMessages` read (`:107-112`). Neither is
statement 3. `TaskReportTurnSelector.SelectAsync` reads the session kind and the rules ids **again**
(`TaskReportTurnSelector.cs:59-73`) and then uses only `span.RawPrompts` (`:65`). It does not read
`TurnPrompts` or `Notifications`.

### Callers

| Caller | Line | What it uses from the result | How often |
|---|---|---|---|
| `TaskReportTurnSelector.SelectAsync` | `TaskReportTurnSelector.cs:64` | `RawPrompts` only: `LastBefore` (`:604`), the real-prompt index (`:627`), `NextSequence` (`:413`). Pre-dispatch rows are barriers. `OwnRealPrompt` (`:352`) rejects a non-time-eligible owner, but only after `NewestBetween` has already chosen that owner. | See the two rows below |
| Deferred-report sweep | `AgentTaskDispatcher.cs:484` calls `SettleDeferredReportsAsync` (`:4890`); per open Dispatched/Working task it calls `SelectAsync` at `:4940` until one task selects | The selection (`Kind`, boundary, owner sequences). Not the prompt texts themselves. | Every dispatcher tick. `Delegation:PollIntervalSeconds` default **5** (`DelegationSettings.cs:28`). No override in `server/appsettings.json`. A desktop override is **UNOBSERVED**. Attribution: 609 calls / 647 s ≈ 56/min, about 4.7 per tick, consistent with one load per open session per tick. The split across callers is **UNOBSERVED**. |
| Settlement | `AgentTaskReplyService.ExtractMarkedTurnAsync` `:3357` (`SelectAsync`) and `:3412` (`LoadPromptsInSpanAsync`, `:3623-3625`) | First load: `RawPrompts` via the selector. Second load: `Notifications` only (`ResolveSubagentWaitAsync` `:3588`). `TurnPrompts` of the second load are unused. | Once per settlement (turn end, and each sweep re-hand). Not every tick. |
| Delivery watchdog | `AgentTaskDispatcher.FailNeverStartedAsync` `:2511-2520` | `TurnPrompts.Count` and whether every remaining row is `QueuedUserPrompt`. `RawPrompts` unused. `dispatchedAt` is `task.DispatchedAt`, not the resume clock. | Every tick, but the query is only Dispatched tasks older than `DeliveryFailTimeoutMinutes` (default **10**, `DelegationSettings.cs:475`). |
| Closed-card settlement | `CardTaskSettlement.HasStartedAsync` `:261` | `TurnPrompts.Count > 0`, and only when the task has no delegation brief row (`:258-259`). | Card close / closed-card sweep. Rate **UNOBSERVED**. Not the 5 s tick. |
| API-error resume | `ApiErrorRecoveryService.FireOneAsync` `:302` | `HasTurnPromptAfterAsync`: any real prompt with `Sequence > stub`. | When a recovery is due. Rate **UNOBSERVED**. |
| API-error turn | `AgentTaskReplyService.HandleApiErrorTurnAsync` `:1972` then `:1977` | `:1972` is the bool. `:1977` loads `dispatchedAt: null` and takes `TurnPrompts.First` with `Sequence > stub` (`:1978`). | During settlement of an API-error stub. Rate **UNOBSERVED**. |
| Late failed report | `LateFailedReportForwarder.ReadReportAsync` `:99` | `SelectAsync`, so `RawPrompts`, up to 32 windows. | On a failure notice. Rate **UNOBSERVED**. |
| `HasTurnPromptSinceAsync` | definition `:128` | `TurnPrompts.Count > 0` | **No production caller.** Comment at `DelegationSettings.cs:458` and `AgentTaskDispatcher.cs:2509`. Tests call it. |
| `BootReplyWatch` | `:268` | `IsHousekeepingPrompt` on rows it already loaded | No span query. |

`RawPrompts` is the list the selector walks. Dropping a pre-dispatch real prompt changes
`NewestBetween`: an older time-eligible brief can become the owner of a housekeeping boundary that
today returns null (`OwnRealPrompt` `:352`). Dropping a null-timestamp row drops a `TurnPrompt`
(`:96-98` keeps null timestamps). Both are delivery evidence. The existing pins are
`a_queued_row_enqueued_before_dispatch_is_excluded_even_with_a_later_sequence` and
`the_s24_timestamp_skew_does_not_reorder_the_span` (`TranscriptPromptSpanTests.cs:44`, `:75`):
sequence order and timestamp order disagree, so "rows with `Sequence >= anchor`" is not the
time window.

### Does a stored sequence anchor match today's answer?

**Not by itself.** Reading `Sequence >=` the task's prompt sequence, or `Sequence >=` the last
known span start, is a different set from `RawPrompts` whenever any prompt sits below that anchor.
That includes the pre-dispatch delimiter, a null-timestamp row at a lower sequence
(`TranscriptPromptSpan.cs:47-49`, `:96-98`), and any real pre-dispatch barrier the walk can still
reach. Those calls fail open: a hidden prompt can change `Selected` / `Uncorrelated` / `None`.

**Yes, when the anchor is the max sequence of a previous full read whose prefix is still the same
rows.** The decision then runs on `cached prefix ++ newly read tail`, which equals today's list
when all of these hold:

- the cached list was produced by today's full projection;
- `count(*)` of prompt rows with `Sequence <= anchor` equals the cached count;
- the lowest prompt id is unchanged;
- the id at `Sequence == anchor` equals the cached top id;
- the tail is exactly the prompt rows with `Sequence > anchor`, read in the same snapshot as that
  proof (one explicit `REPEATABLE READ` transaction, §3 "Miss path").

The cached prefix's own stamp is computed from the cached rows, never from a separate statement
(B-1, D-11), so the cached list and the stamp it is compared against can never come from two
different snapshots.

**Fail closed to today's full read** when any proof is missing:

- no cache entry (first sight, process restart, eviction, idle expiry, a session over the
  per-entry ceiling);
- the caller already has a database transaction (do not nest one, and do not publish a cache entry
  from that snapshot);
- anchor row gone, prefix count changed, first id changed, or the id at the anchor sequence changed
  (delete, rebase, whole-session replace);
- the tail's row count is not the stamp's count delta, or its last id is not the stamp's top id.

An in-place update of a stored prompt row's `Text`, `Timestamp`, or a `Kind` change between
`UserPrompt` and `QueuedUserPrompt`, does **not** change this stamp. That hole is D-4. (A `Kind`
change between a prompt kind and a non-prompt kind changes `Count` and is seen.)

## 2. Ground truth

| Card or brief assumes | Code at 7901e9ba5 | Consequence |
|---|---|---|
| The span is a prompt window, maybe the last prompt | SQL returns every prompt row of the session (`:88-94`). `RawPrompts` keeps them all. | A sequence cut without a proven prefix hides barriers. §1. |
| The sweep is the hot caller | `SettleDeferredReportsAsync` calls `SelectAsync` every tick per open task (`:484`, `:4940`). 609/647 s fits that. Other callers are real and share `LoadAsync`. | The cache sits inside `LoadAsync`, so every caller gets it. The sweep is not rewritten. |
| CARD-1910 and CARD-1914 already shrank the read | They cut in-process CPU (floor index, `TrimStart`). `LoadAsync` still materialises every prompt row. | This slice is the row cut. Do not redo those cards. |
| A stamp of count, first id, max sequence and top id sees every change that matters | It sees append, middle delete, top-row replace and whole-session delete. It does not see an in-place `Text` or `Timestamp` update, or a `UserPrompt` / `QueuedUserPrompt` swap. | D-2 and D-4. Same hole CARD-1888 recorded. |
| One statement is one snapshot, so a stamp read beside the rows cannot tear | Under Postgres `READ COMMITTED` (the Npgsql default; no retrying strategy is configured) each statement has its own snapshot. A rows statement followed by a stamp statement can pair rows without a prompt with a stamp that includes it (review B-1). | The stored stamp is derived from the stored rows (D-11). The miss path runs in one `REPEATABLE READ` transaction (precedent: `DispatchConcurrencySettingsService.cs:128`). W-7. |
| `LoadAsync` is called from one place at a time | It runs concurrently from the dispatcher tick (sweep, watchdog), settlement, card settlement, API-error recovery and the late-failure forwarder, each on its own `DbContext` (review B-2). | A lock-guarded store with immutable entries replaced whole (D-12). W-8. |
| Rows are only appended | Production inserts rebase above the stored max (`AgentSessionRuntime.cs:1481`; the only `TranscriptEntries.Add` sites are `:1513`, `:1612`, `:1648`). Production deletes found here: whole-session `ExecuteDeleteAsync` (`DataRetentionService.cs:372`) and the session-row cascade (CARD-1888 plan §2). No `ExecuteUpdate` on `TranscriptEntries` under `server/`. | Prefix proof is valid for production writers. Re-grep gate in S3. Nine test classes edit prompt rows in place (`Text` or `Kind`); D-8 lists them and CP-5/CP-6 run them. |
| The 5 s stamp read must be cheap | **UNOBSERVED** milliseconds. The statement does not read `Text`. The id lookups must be one `LIMIT 1` per kind (A-1). | §3, §8. Measure after deploy. Do not invent a p95. |
| Skipping the sweep when nothing was appended is safe | The sweep exists because a text-less turn never re-triggers settlement (`AgentTaskDispatcher.cs:4861-4888`). A `TurnEnd` is not a prompt, so a prompt stamp does not move. Grace is a wall clock. | Do not skip the sweep (D-5). |
| A covering index or summary table is required | `IX_TranscriptEntries_AgentSessionId_Kind_Sequence` (CARD-1773, `AppDbContext.cs:1751-1755`) already serves the kind filter and the count. With `Kind IN (…)` it cannot return rows in `Sequence` order, so `ORDER BY "Sequence" LIMIT 1` over both kinds scans every prompt entry (review A-1, not EXPLAINed). One `Kind = '…' ORDER BY "Sequence" LIMIT 1` per kind is a one-row index scan each, plus one heap fetch for `Id`. Text leaves the hot path. | No migration (D-6). |
| Cached rows are cheap to keep | Today the prompt rows are transient per call. Retained, they are briefs and reports of up to 14,400 characters each, 971 rows per call now and 1,503 in the 44 h sample (attribution 3.3). Average prompt `Text` length is **UNOBSERVED**. Review's unmeasured envelope: 1,000 × 4,000 chars × 2 bytes = 8 MB per session, about 2 GB at 256 sessions. | A character bound per entry and in total, an idle expiry, and eviction that full-reads (D-13). M-7 measures it. |

## 3. Design

### Chosen: cache the full prompt list with ids, reuse it when the live stamp matches the stamp derived from the cached rows, append a tail only when the prefix is proven

Inside `TranscriptPromptSpan.LoadAsync` only. No caller signature change. The C# filter
(`TurnPrompts`, `Notifications`, housekeeping, allowlist) stays as it is and runs on every call,
so a new rules-refresh id or a different `dispatchedAt` cannot be served a stale classification.
The cache stores the unfiltered prompt rows (the SQL list), not `Result`.

**The stored stamp is derived from the stored rows (B-1, D-11).** Every projection that fills
the cache also returns `Id` (an internal `Guid[] Ids` parallel to the rows; `PromptRow` keeps its
four fields because `TaskReportTurnSelectorTests.cs:49`, `:148` construct it). A cache entry's
stamp is computed in C# from the rows it holds, never read by a second statement:

- `Count` = number of rows;
- `FirstId` = `Ids[0]`;
- `MaxSequence` = `Rows[^1].Sequence` (the list is ordered by `Sequence`, which is unique per
  session: `AppDbContext.cs:1747-1749`);
- `TopId` = `Ids[^1]`;
- empty list: `(0, null, null, null)`.

So the cached list and its stamp always describe one snapshot: the snapshot of the statement
that produced those rows (or of the `REPEATABLE READ` transaction that produced prefix and
tail). A prompt that commits after that statement is not in the rows **and not in the stamp**,
so the next call's stamp differs and the call misses. That closes B-1 by construction, whatever
the isolation level of the full read.

**Stamp**, one statement, tag `-- prompt-span.stamp`, the only statement on a hit. Written as raw
SQL (`db.Database.SqlQuery<…>`), shape fixed for A-1:

```sql
-- prompt-span.stamp
SELECT c."Count",
       fu."Sequence" AS "FirstUserSeq",   fu."Id" AS "FirstUserId",
       fq."Sequence" AS "FirstQueuedSeq", fq."Id" AS "FirstQueuedId",
       tu."Sequence" AS "TopUserSeq",     tu."Id" AS "TopUserId",
       tq."Sequence" AS "TopQueuedSeq",   tq."Id" AS "TopQueuedId"
FROM (SELECT count(*) AS "Count" FROM "TranscriptEntries"
      WHERE "AgentSessionId" = @s AND "Kind" IN ('UserPrompt', 'QueuedUserPrompt')) c
LEFT JOIN LATERAL (SELECT "Sequence", "Id" FROM "TranscriptEntries"
      WHERE "AgentSessionId" = @s AND "Kind" = 'UserPrompt'
      ORDER BY "Sequence" ASC LIMIT 1) fu ON true
LEFT JOIN LATERAL (… "Kind" = 'QueuedUserPrompt' ORDER BY "Sequence" ASC LIMIT 1) fq ON true
LEFT JOIN LATERAL (… "Kind" = 'UserPrompt' ORDER BY "Sequence" DESC LIMIT 1) tu ON true
LEFT JOIN LATERAL (… "Kind" = 'QueuedUserPrompt' ORDER BY "Sequence" DESC LIMIT 1) tq ON true
```

The kind literals come from `TranscriptKinds` constants as parameters, not inlined strings, if
Code finds that the planner still picks the per-kind index range (Code checks one `EXPLAIN` in the
test database; production `EXPLAIN` is not run). C# takes `FirstId` from the lower first sequence
and `TopId` / `MaxSequence` from the higher top sequence. Each lateral is a one-row index scan on
`IX_TranscriptEntries_AgentSessionId_Kind_Sequence` plus one heap fetch for `Id`; the four are
one statement and one snapshot. `count(*)` stays O(prompt index entries) with heap visits for
pages that are not all-visible. The statement never reads `Text`.

**Hit.** A cache entry exists for the key and its derived stamp equals this stamp. Return the
cached rows. Prompt-text rows read: 0. No transaction is opened on a hit.

**Miss path (stamp differs, entry exists).** Open one explicit transaction,
`db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)` (precedent:
`DispatchConcurrencySettingsService.cs:128`). In Postgres `REPEATABLE READ` every statement of the
transaction sees the snapshot taken at its first statement, and a read-only transaction cannot
fail with a serialization error. Inside it:

1. `-- prompt-span.proof`: the stamp statement plus two proof columns for the cached anchor
   `m = cached.MaxSequence`: prompt-kind `count(*)` with `"Sequence" <= m`, and the prompt-kind `Id`
   with `"Sequence" = m` (served by the unique `(AgentSessionId, Sequence)` index).
2. Tail allowed when `FirstId == cached.FirstId`, the `<= m` count equals `cached.Count`, the id at
   `m` equals `cached.TopId`, and the new `MaxSequence > m`. Then `-- prompt-span.tail`: prompt rows
   with `"Sequence" > m` (with `Id`), ordered by `Sequence`. Accept the tail only when its row count
   equals `stamp.Count - cached.Count` and its last id equals `stamp.TopId`. Store
   `cached ++ tail`, whose stamp is derived from those rows (step "derived" above).
3. Otherwise `-- prompt-span.read`: today's full projection (with `Id`) in the same transaction.
   Store it.
4. Commit (read-only; nothing written).

The anchor of a tail is the last cached row's own sequence, so a tail can never re-read a row the
cached list already holds (no duplicate) and cannot skip one that committed before the
transaction's snapshot (no gap).

**Cold path (no entry).** `-- prompt-span.read` alone, no transaction (one statement is one
snapshot, and the stamp is derived from its rows). Store it.

**Ambient transaction.** If `db.Database.CurrentTransaction` is already set, run today's full
projection and do not read or write the cache. Do not open a nested transaction.

**Cache shape (B-2, D-12, D-13).** A nested `internal sealed class PromptCache` inside
`TranscriptPromptSpan.cs` (no new file):

- **Key:** `(db.Database.GetConnectionString(), sessionId)`. The connection string is held in
  memory only and is never logged or returned; it keeps hosts or test fixtures on different
  databases or search paths from sharing entries (A-6).
- **Entry:** one immutable object `Entry(Stamp, ImmutableArray<PromptRow> Rows, ImmutableArray<Guid>
  Ids, long Chars)`. It is created whole and replaced whole; nothing mutates it after
  construction. `RawPrompts` returns `Rows` itself (an `ImmutableArray` is an
  `IReadOnlyList<PromptRow>` and cannot be cast back to a writable array).
- **Store:** `Dictionary<Key, LinkedListNode<Slot>>` plus an LRU `LinkedList<Slot>`, where `Slot`
  holds the key, the current `Entry` reference and `LastUsedMs`. Every read and write of the
  dictionary, the list, `LastUsedMs` and the running character total is under one
  `lock (_gate)`. No database I/O, no `await`, and no allocation proportional to the rows runs
  under the lock: `Lookup` is a dictionary hit plus a move-to-head; `Store` swaps one reference
  and evicts from the tail.
- **Last writer wins.** Two concurrent misses on one session each store a self-consistent entry
  (its stamp is derived from its rows). If an older read overwrites a newer one, the next call
  misses and takes the tail or full-read path. Nothing is served that the stamp does not match.
- **Bounds (labelled UNOBSERVED; chosen, not measured):** `Chars` = sum of `Text.Length` plus 48
  per row (a stand-in for the row object, `Kind` string and `Guid`; not measured). Per-entry
  ceiling **4,000,000** chars (about 8 MB of UTF-16 text): a list over it is returned to the
  caller and **not stored**, so that session keeps today's full read on every call (no gain, no
  regression). Total bound **32,000,000** chars (about 64 MB of text plus the 48-per-row
  stand-in); entry cap **256**. A store that crosses either bound evicts least-recently-used
  entries from the tail until both hold.
- **Idle expiry:** an entry not used for **120 s** (24 ticks at the 5 s default) is dropped at the
  next `Lookup` or `Store`. The deferred-report sweep loads every Dispatched/Working task's session
  on every tick, so an open task's entry stays live; when its last open task ends nothing reloads
  it and it expires. That is how "cleared when the task ends" is enforced without a hook in the
  six terminal writers (D-13). "Session reloads" (a re-ingest, rebase, whole-session delete) are
  stamp changes: the miss path replaces the entry whole. A process restart empties the cache.
- **Eviction, expiry and the ceiling all fail closed:** the next call is the cold path, today's
  full projection. W-9 pins each one.
- **Seams:** `LoadAsync(db, sessionId, dispatchedAt, ct)` calls
  `LoadAsync(db, sessionId, dispatchedAt, PromptCache.Shared, ct)`; the internal overload, the
  `PromptCache(maxEntries, maxEntryChars, maxTotalChars, idle, Func<long> nowMs)` constructor and
  `Forget(db, sessionId)` / `Snapshot()` (entries, chars) are internal for tests. `Shared` uses the
  numbers above and `Environment.TickCount64`. Not a setting.

**Sweep.** `SettleDeferredReportsAsync` stays on the 5 s tick. On a stamp hit it still selects,
still evaluates the grace clocks, and still honours `ReportSweepRehandSeconds`. It stops shipping
prompt text when the prompt rows have not changed.

### Why a hit or a proven tail cannot hide a prompt

The list the selector and the watchdog classify is either today's full projection or a cached
full projection plus a tail of rows that did not exist at the anchor, and the stamp it is
matched against is derived from exactly that list. Append changes `max` and the top id, so the
next call cannot hit. A prompt committed between two statements of one call is either in the
rows (and therefore in the derived stamp) or absent from both, so the next call misses (W-7). A
middle delete changes `count` and fails the prefix proof. A top row deleted and reinserted at the
same sequence changes the id at the anchor. A whole-session delete changes count, first id and
top id. A null-timestamp row and a pre-dispatch barrier stay in the cached prefix, so `LastBefore`
and `NewestBetween` see them. `TurnPrompts` is still the C# time filter over that complete list.
Concurrent callers see whole immutable entries only (W-8). An entry that is evicted, expired or
over the ceiling is a cold full read (W-9).

### Rejected

| Alternative | Why rejected |
|---|---|
| Read `Sequence >=` the task prompt, or the last span start, with no cached prefix | Not `RawPrompts`. Hides pre-dispatch barriers and a lower-sequence null timestamp. §1. |
| Push `Timestamp > dispatchedAt OR Timestamp IS NULL` into SQL and stop | That is `TurnPrompts`, not `RawPrompts`. The selector needs the pre-dispatch rows. It also re-reads the whole post-dispatch history every tick, which is the 971-row cost on a long session. |
| Cache invalidated only when this process appends | A delete, another process, or a restart does not append. The stamp is the check. An append hook is not required once every call reads the stamp. |
| Store the stamp read by its own statement next to the rows (the original §3) | Two `READ COMMITTED` statements are two snapshots: rows without a prompt can be stored under a stamp that includes it, and every later call hits (review B-1). Derived stamps cost nothing extra. |
| Run every call (including hits) in a `REPEATABLE READ` transaction | A hit is one statement, already one snapshot. A transaction would add `BEGIN`/`COMMIT` round trips on the 5 s path for no gain. Only the miss path, which has two statements, needs it. |
| Concurrent dictionary with no lock | Size accounting and LRU order across a dictionary and a list need one atomic step; `ConcurrentDictionary` alone cannot evict by total characters without a race on the total. The lock covers O(1) work only. |
| Evict on task end via a hook in each terminal writer | No single task-end seam exists (settlement, card settlement, watchdog fail, cancel, land paths), the callers are on the must-not-touch list, and a missed writer leaks. The idle expiry cannot leak. |
| Detect in-place edits with `xmin` (any `UPDATE` writes a new tuple version) | Would close the D-4 hole for every column, but needs a heap visit for every prompt row on every hit (O(prompt rows) heap, not index-only). Recorded as a follow-up option, not this slice (F-1). |
| Skip `SettleDeferredReportsAsync` when the prompt stamp is unchanged | A new `TurnEnd` and the final-message / subagent grace are not prompt rows (`:4861-4888`). Settlement would wait for the next prompt. D-5. |
| Also cache the selector and skip its `TurnEnd` read (`TaskReportTurnSelector.cs:83-87`) | That read is not statement 3. Its cost is **UNOBSERVED** (not in the attribution top 7). A second stamp is a later slice. |
| `md5` / `string_agg` over prompt `Text` | Still visits TOAST for every prompt. That is the CPU this slice removes from the 5 s path. |
| CARD-1888 `IngestPrefix` or the CARD-1915 summary | Different question and lifetime. The summary flag is off; CARD-1933 says EF batch appends invalidate it; the summary has no prompt text. |
| Covering index or a summary table | Human-only migration, and the existing kind index already serves the stamp. D-6. |
| Share slice 1's recovery stamp type | Slice 1 compares two observations a few seconds apart. This cache lives across ticks and stores prompt text. No shared file. |

## 4. Slices

| Slice | Files | Content |
|---|---|---|
| S1 | `tests/Antiphon.Tests/Application/TranscriptPromptSpanTests.cs`; `server/Application/Services/TranscriptPromptSpan.cs` (inert seam only) | W-1..W-9 below. Nested `PromptTextReadCounter : DbCommandInterceptor`, `InsertAfterPromptReadInterceptor` (W-7) and a `CreateContext(params IInterceptor[])` used only by the new tests. Production: the `PromptCache` class with its constructor, `Shared`, `Forget`, `Snapshot` and the cache-taking `LoadAsync` overload, all **inert**: `Lookup` returns null, `Store` stores nothing, and `LoadAsync` runs today's projection unchanged (no `Id`, no tag). Today's behaviour, so W-1/W-3 are red for the right reason and every witness compiles. |
| S2, deliberately incomplete, never landed alone | `TranscriptPromptSpan.cs` | The lock-guarded store with whole-entry replacement, keyed as §3. Stamp is `max(Sequence)` only, read by its **own statement after the rows** (tag `prompt-span.stamp`), the torn shape B-1 describes. Equal max: return the cache. Any other max, a missing entry, or an open transaction: today's full read, then the separate max read, then store. No count, no ids, no proof, no tail, no `REPEATABLE READ`, and the bounds and idle expiry are accepted by the constructor but **ignored**. |
| S3 | `TranscriptPromptSpan.cs`; `docs/session-runtime-invariants.md` (one paragraph, D-14); test-only `Forget` calls in any CP-5/CP-6 class that goes red only because of D-4 (D-8) | Derived stamps with `Id`, the A-1 stamp statement, the `REPEATABLE READ` miss path with proof and tail, the full-read fallback, the transaction bypass, the bounds, the idle expiry, and the S3 re-grep gate. |

S2 makes W-1 and W-2 green and leaves W-3, W-4, W-5, W-7 and W-9 red, so those are not stubs (D-9).

### Must NOT touch

- `AgentSessionRuntime.cs`, `TranscriptIngestPrelude.cs`, `TranscriptIngestPrefix.cs`,
  `PolicyRefreshService.cs`: CARD-1888 Code `6ea96bcb` (tip `cd222da3c`).
- `AgentSessionRuntime.cs` `:1062-1540`, `StaleWorkingTranscriptRecovery.cs`, and slice 1's
  `TranscriptRecoveryRead.cs`: CARD-1969 slice 1.
- `docs/session-runtime-invariants.md` §"Stale Working transcript recovery (CARD-1205 S4)"
  (line 343): slice 1's S3 paragraph goes there. This slice's paragraph goes in a new
  `### Prompt-span cache (CARD-1969 S2)` after §"Report attribution across housekeeping turns
  (CARD-0714)" (line 1608), about 1,260 lines away, so the two edits do not overlap.
- `SessionStateLoader.cs`, `SessionTranscriptSummaries`, CARD-1915 triggers: summary laterals.
- `TaskReportTurnSelector.cs`, `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`,
  `CardTaskSettlement.cs`, `ApiErrorRecoveryService.cs`, `LateFailedReportForwarder.cs`:
  callers stay. The behaviour change is inside `LoadAsync`.
- `TranscriptKinds.cs` (CARD-1914) and the CARD-1910 floor index.
- `server/Migrations/**`, `server/appsettings*.json`: no migration, no index, no setting.
- `scripts/impacted-classes.ps1`, `tests/impacted-classes.json`, `tests/derived-impact-index.json`.
  The new tests are methods on the existing class. `impacted-classes.ps1 -Path
  server/Application/Services/TranscriptPromptSpan.cs,tests/Antiphon.Tests/Application/TranscriptPromptSpanTests.cs -Json`
  at 7901e9ba5 returned `kinds: []`, `classes: []`.

### Existing tests affected

- Migrations, public DTOs, routes, remote script functions: none.
- Operation labels and statement budgets: none. No `QueryBudget` test mentions the span.
  Four new SQL comments: `prompt-span.stamp`, `prompt-span.proof`, `prompt-span.tail`,
  `prompt-span.read`.
- Behaviour that must stay green: the 9 existing `TranscriptPromptSpanTests` methods;
  `TaskReportTurnSelectorTests` (2 methods, 2 cases); `TaskReportHousekeepingTests` (10 methods,
  11 cases: `C714_grok_shape_on_other_provider_is_real_prompt` has two `[Arguments]`,
  `TaskReportHousekeepingTests.cs:87-90`), so CP-4 is 13 cases (A-3).
  `HasTurnPromptSinceAsync` / `HasTurnPromptAfterAsync` keep their answers because they call
  `LoadAsync`.
- Tests that edit stored prompt rows in place (D-8, A-2), run in CP-5 and CP-6:

| Class | Method (cases) | Edit |
|---|---|---|
| `C0824RefOnlyReceiptTests` | `Settlement_note_reaches_caller_and_recovers_handoffs` (1) | `Text` and `Kind` of the accepted prompt, then restore (`:146-159`) |
| `TerminalRunnerSeatReleaseTests` | `Parent_receipt_rejects_ack_stale_or_partial_prompt` (1), `Answer_receipt_rejects_ack_stale_or_partial_prompt` (1) | `Kind` to `QueuedUserPrompt` / `AssistantText` (`:297`, `:496`) |
| `PreDispatchFailureDeliveryTests` | `Pre_dispatch_receipt_requires_complete_user_prompt` (1) | `Text` cut, then restored (`:352`, `:357`) |
| `StandingBootAttentionTests` | `C1156_Standing_row_makes_no_delivery_claim` (2) | `Text` cut to a prefix (`:585`) |
| `PermissionWaitTests` (`PermissionWaitDeliveryTests.cs`) | `C1518_NoteRecoveryRetainsOneQueueRow` (1) | `Kind` swap and `Text` (`:145-155`) |
| `BlockedTaskParkDeliveryTests` | `C1065_FullAnswerRequiresNewSessionUserPrompt` (1) | `Kind` (`:76-78`) |
| `DataRetentionServiceTests` | `C768_CompleteBatchReceiptPurgesEveryMappedMember` (2) | `Text` clipped, then restored (`:470-477`) |
| `TaskInputWeakArmBoundaryTests` | `Boundary_matches_the_base_outcome` (17) | every row's `Text` to null (`:97`) |
| `TaskInputAttentionCostTests` | `Single_query_boundary_matches_frozen_builder` (11), `Results_are_byte_identical_to_original_builder` (17), `Weak_arm_reads_a_bounded_window` (3) | prompt `Text` by sequence (`:173`, `:328`, `:477`) |

  Most of these assert a delivery receipt or a task-input read, which are not `LoadAsync`
  callers; whether any of them reaches `LoadAsync` between its edit and a later read is
  **UNOBSERVED** (not traced here). Running them is cheaper than tracing them, so CP-5/CP-6 run
  every edit-bearing method at the tip. Excluded with reason: `PermissionWaitObservationTests`
  (edits move rows between prompt and non-prompt kinds, which changes `Count` and is seen),
  `ChannelOutboundRetentionTests:175` (`AssistantText` only), and the `CreatedAt` edits (not in
  the projection).

## 5. Decisions

- **D-1.** Cache the unfiltered prompt rows inside `LoadAsync`. Reuse them when the stamp matches.
  Append a tail only when the prefix proof holds. Otherwise today's full read. Reason: that list
  is what `RawPrompts`, `TurnPrompts` and `Notifications` are computed from, for every caller. §3.
- **D-2.** The stamp is `(Count, FirstId, MaxSequence, TopId)` of the prompt-kind rows. The live
  stamp is one statement (four one-row laterals and a count, A-1). The cached stamp is derived
  from the cached rows (D-11). Reason: count sees a middle delete; the id at the anchor sees a
  same-sequence replace; max sees an append; and neither side of the comparison can tear.
- **D-3.** Fail closed to the full read when the cache, the prefix proof, or an ambient
  transaction is missing, and on eviction, expiry or the per-entry ceiling. Reason: a wrong
  anchor hides a prompt (§1). The full read is today's behaviour, so the fallback cannot be a new
  miss.
- **D-4.** In-place updates of a stored prompt row are not detected: `Text`, `Timestamp`, and a
  `Kind` swap between `UserPrompt` and `QueuedUserPrompt` (A-2). A stale `Kind` would reach the
  watchdog's queued-only branch (`AgentTaskDispatcher.cs:2511-2520`) and the selector. No
  production statement does any of these (§2, review check 1). The S3 re-grep is the gate: any new
  `ExecuteUpdate` of any column, tracked edit of a loaded `TranscriptEntry` followed by
  `SaveChanges`, `ExecuteDelete` of fewer than all of a session's rows, raw
  `UPDATE`/`DELETE`/`INSERT` on `TranscriptEntries`, or an insert whose `Sequence` is not rebased
  above the stored max, under `server/`, stops the slice (report blocked). Whole-session
  `ExecuteDeleteAsync` at `DataRetentionService.cs:372` stays listed and is detected by the stamp
  (count and ids change).
- **D-5.** The deferred-report sweep still runs every tick. Only the prompt-text read is skipped
  on a hit. Reason: grace and a prompt-less `TurnEnd` are the sweep's job (`:4861-4888`).
- **D-6.** No migration and no new index. Reason: CARD-1773's kind index serves the count and the
  per-kind `LIMIT 1` laterals. A covering index would still be a human-only gate and is not what
  makes the hit cheap.
- **D-7.** Process-local cache, bounded by entries (256), characters per entry (4,000,000) and in
  total (32,000,000), and idle time (120 s). Not a setting. Reason: the hot set is the open
  sessions; the bounds cap the retained text; every eviction is a cold full read. Numbers are
  chosen, not measured (UNOBSERVED); M-7 checks them.
- **D-8.** The counters and interceptors are private nested test classes, and the witnesses use
  private `PromptCache` instances (except W-1's `select` case, which goes through
  `TaskReportTurnSelector` and therefore `PromptCache.Shared`, on a fresh session id). Reason: no
  new test file, so `tests/derived-impact-index.json` stays untouched, and no witness can be
  disturbed by another test's entries. A test that edits a stored prompt row in place and then
  reaches `LoadAsync` sees D-4; if a CP-5/CP-6 method goes red at the tip and is green at the
  base commit, Code confirms the red is D-4 (the method's edit is followed by a `LoadAsync` on
  that session), adds `TranscriptPromptSpan.PromptCache.Shared.Forget(db, sessionId)` right after
  the edit in that test, and names it in the report. Do not weaken an assertion.
- **D-9.** S2 is committed red for W-3, W-4, W-5, W-7 and W-9. S2 and S3 land in one Merge, never
  separately. An rc port carries them the same way. Reason: mutation checks are waived (operator
  2026-11-12); this is the CARD-1888 / slice 1 precedent.
- **D-10.** No new metric or route. After deploy, `pg_stat_statements`, the four tags and the
  process memory are the measurement (§8).
- **D-11.** The stored stamp is derived from the stored rows; the miss path's proof and tail run
  in one `REPEATABLE READ` transaction; a hit is one statement with no transaction (B-1).
- **D-12.** One lock guards the dictionary, the LRU list, the last-used times and the character
  total; entries are immutable and replaced whole; no I/O under the lock; last writer wins; the
  key includes the connection string (B-2, A-6).
- **D-13.** "Cleared when a task ends" is the 120 s idle expiry, not a hook in the terminal
  writers (§3 Rejected). "Session reloads" are stamp changes that replace the entry whole.
- **D-14.** S3 adds `### Prompt-span cache (CARD-1969 S2)` to `docs/session-runtime-invariants.md`
  after §"Report attribution across housekeeping turns (CARD-0714)": the cache is process-local,
  derived-stamp, fail-closed to the full read; the accepted D-4 hole (`Text`, `Timestamp`,
  `UserPrompt`/`QueuedUserPrompt` swap) and its re-grep gate; the bounds and idle expiry; and that
  a transcript-confirmed `UserPrompt` remains the delivery verdict (A-6).

## 6. Verification design

Witnesses are new methods on `TranscriptPromptSpanTests` (Integration, shared `TestDbFixture`).
`SeedPrompts(n)` writes `n` `UserPrompt` rows with distinct text and ids, timestamps after
dispatch, plus 2,000 `AssistantText` rows that the prompt projection must not return.
`PromptTextReadCounter` counts reader rows whose command text contains `TranscriptEntries`,
`"Text"` and `"Timestamp"`. `Reset()` runs between loads. The counter is read before any later
assertion query. Every witness except W-1 `select` builds its own
`new TranscriptPromptSpan.PromptCache(…)` and calls the cache-taking `LoadAsync` overload; W-9
passes small bounds and a fake clock.

`InsertAfterPromptReadInterceptor` (W-7): on `DataReaderClosingAsync` of the **first** command the
counter matches (at base: today's projection; at S2 and S3: the `prompt-span.read` statement),
it inserts one `UserPrompt` at the session's stored `max(Sequence) + 1` through a **second**
context and connection, then disarms. Under `READ COMMITTED` that row is outside the projection's
snapshot and inside any later statement's.

| ID | Test (cases) | Assertion | At 7901e9ba5 / S1 | At S2 | At S3 | Why, from the code |
|---|---|---|---|---|---|---|
| W-1 | `C1969s2_unchanged_second_read_materialises_no_prompt_text` (`(400,"load")`, `(1600,"load")`, `(400,"select")`) | Second prompt-text row count is 0. First count is `n`, not `n + 2000`. `select` calls `TaskReportTurnSelector.SelectAsync` twice with an in-memory `AgentTask`. | **Red.** Second count is `n`. | Green | Green | `:88-94` runs on every `LoadAsync`, and `SelectAsync` `:64` calls it. Nothing remembers the rows (S1's seam is inert). |
| W-2 | `C1969s2_new_prompt_is_observed` (1) | After the first load, insert one prompt at `max+1`. The second `RawPrompts` and `TurnPrompts` contain it. | Green | Green | Green | A full read sees it. S2 full-reads because max changed. S3's tail includes it. |
| W-3 | `C1969s2_new_prompt_does_not_reread_the_prefix` (1) | Same shape. Second prompt-text row count is 1. | **Red.** Second count is `n+1`. | **Red.** S2 full-reads on a new max. | Green | Tail only exists in S3. |
| W-4 | `C1969s2_deleted_prompt_is_absent` (1) | `n = 400`. After the first load, delete the prompt at sequence 200 through a second context (children of a Restrict FK first, as slice 1 does). The second `RawPrompts` has no row with that sequence or that text, and has `n-1` rows (A-4: `PromptRow` carries no `Id`). | Green | **Red.** Max is unchanged, so S2 returns the deleted row. | Green | Prefix count changes. S3 full-reads. |
| W-5 | `C1969s2_rebased_top_row_is_observed` (1) | After the first load, delete the top prompt and insert a new id at the same `Sequence` with new text. Second top text is the new text. | Green | **Red.** Max is unchanged. | Green | The id at the anchor changed. S3 full-reads. |
| W-6 | `C1969s2_null_timestamp_below_a_later_pre_dispatch_sequence_stays` (1) | Seq 1 null timestamp, seq 2 timestamp before dispatch, seq 3 after. Both loads: `TurnPrompts` are seq 1 then seq 3; `RawPrompts` has all three, in sequence order. | Green | Green | Green | `:96-98` keeps a null timestamp. A `Sequence >=` cut that starts at seq 2 drops seq 1. |
| W-7 | `C1969s2_prompt_committed_after_the_rows_is_never_hidden` (1) | `n = 400`, interceptor armed for the first load. First load returns `n` rows (the insert came after its snapshot). The interceptor reports that it inserted exactly one row. The second load's `RawPrompts` and `TurnPrompts` contain the inserted prompt, and `RawPrompts` has `n+1` rows with strictly increasing sequences. | Green: no cache; the second load full-reads. | **Red.** S2 reads `max` in its own statement **after** the rows, sees `n+1`, stores the `n` rows under it, and the second load hits and lacks the prompt (B-1's torn shape). | Green: the stored stamp is derived from the `n` rows, so the second load's live stamp (`n+1`) misses and the tail reads the prompt. | B-1. |
| W-8 | `C1969s2c_concurrent_loads_never_return_a_torn_or_stale_list` (1) | One session, 200 seeded prompts, one shared `PromptCache`. A writer appends 40 prompts one at a time and publishes the committed count after each commit. 16 readers, each with its own context, run 20 loads each; each reader samples the committed count `k` before calling `LoadAsync`. Every result: no exception; `RawPrompts` sequences strictly increasing with no duplicate; contains every seeded and appended prompt with index `< k`; every text matches its sequence. A final load after the writer stops returns exactly `240` rows. | Green (no cache) | Not run (see CP-2): a torn store can make it red, but not deterministically. | Green | B-2 and B-1 under concurrency. A guard, not a deterministic red: the deterministic reds are W-7 (torn stamp) and W-9 (bounds). Named `C1969s2c_` so CP-1/CP-2's `C1969s2_*` filter excludes it. |
| W-9 | `C1969s2_eviction_returns_the_full_list` (`("entry-ceiling")`, `("total-bound")`, `("entry-cap")`, `("idle")`) | `entry-ceiling`: cache with a 1,000-char entry ceiling, 40 prompts of 100 chars; two loads. `total-bound`: 6,000-char total, two sessions of 40 × 100 chars; load A, load B, load A. `entry-cap`: `maxEntries` 1, same two-session order. `idle`: fake clock, one session, load, advance the clock 121 s, load. In every case the last load's prompt-text row count is the full `n` (a cold read), its `RawPrompts` equals the seeded list in order, and `Snapshot()` shows the bound held (entries and chars). | Green: every load full-reads. | **Red.** S2 ignores the bounds and the clock, so the last load hits (count 0). | Green | B-2: eviction, expiry and the ceiling fail closed to today's read. |

Red at base and S1 is W-1 and W-3 only. They count reader rows of the prompt projection, so a
constant or a self-compare cannot satisfy them while the outcome still contains the rows. W-4,
W-5, W-7 and W-9 are green at base because every load is a full read; S2 omits the checks that
see them, so CP-2 is their committed red. W-7 asserts that its interceptor inserted, so it cannot
pass vacuously. W-6 is green on every slice and goes red if Code switches to an unproven sequence
window. W-8 is a concurrency guard at the tip.

`C1969s2_` and `C1969s2c_` have 0 hits in this tree. Class-name check: `TranscriptPromptSpanTests`,
`TaskReportTurnSelectorTests`, `TaskReportHousekeepingTests` and the nine CP-5/CP-6 classes each
match only their own class (no class in `tests/` extends any of those names); every CP-5/CP-6
method name occurs once in `tests/`.

### Checkpoints

Lane: the runner-defaults global host (linux). Omit `-Runner` and `-Platform`. Export
`ANTIPHON_LINUX_COMPILER_POLICY=0` before every dotnet command. One isolated build per `After`
group. Each `After` group is one `--serial` run without `--row-timeout`: CP-1 at the S1 commit,
CP-2 at the S2 commit, CP-3 to CP-6 together at the tip. Red rows run once at their slice and
are not re-run after they go green. A CP-5/CP-6 red is first re-run at the base commit
(inherited or not) before D-8 applies.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1969-s2-red/` | c1969-s2-red | `/*/*/TranscriptPromptSpanTests/C1969s2_*` | W-1..W-7, W-9 red proof | 13 executed; W-1 (3) and W-3 (1) failed on the row bound (`n` and `n+1`); W-2, W-4, W-5, W-6, W-7, W-9 (4) passed | 13 | 15 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1969-s2-stamp/` | c1969-s2-stamp | `/*/*/TranscriptPromptSpanTests/C1969s2_*` | W-3, W-4, W-5, W-7, W-9 committed red | 13 executed; W-3, W-4, W-5, W-7 and W-9 (4) failed (8 cases); W-1 (3), W-2 and W-6 passed | 13 | 15 |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1969-s2-green/` | c1969-s2-green | `/*/*/TranscriptPromptSpanTests/*` | W-1..W-9 and the 9 existing span methods | 23 executed (9 + 13 + 1), 0 failed/skipped | 23 | 15 |
| CP-4 | all | CP-3 | span-callers | `/*/*/(TaskReportTurnSelectorTests*)\|(TaskReportHousekeepingTests*)/*` | selector and housekeeping still classify the same rows | 13 executed (2 + 11), 0 failed/skipped | 13 | 8 |
| CP-5 | all | CP-3 | in-place-receipts | `/*/*/(C0824RefOnlyReceiptTests*)\|(TerminalRunnerSeatReleaseTests*)\|(PreDispatchFailureDeliveryTests*)\|(StandingBootAttentionTests*)\|(PermissionWaitTests*)\|(BlockedTaskParkDeliveryTests*)\|(DataRetentionServiceTests*)/(Settlement_note_reaches_caller_and_recovers_handoffs*)\|(Parent_receipt_rejects_ack_stale_or_partial_prompt*)\|(Answer_receipt_rejects_ack_stale_or_partial_prompt*)\|(Pre_dispatch_receipt_requires_complete_user_prompt*)\|(C1156_Standing_row_makes_no_delivery_claim*)\|(C1518_NoteRecoveryRetainsOneQueueRow*)\|(C1065_FullAnswerRequiresNewSessionUserPrompt*)\|(C768_CompleteBatchReceiptPurgesEveryMappedMember*)` | A-2: receipt tests that edit prompt rows in place | 10 executed (1 + 1 + 1 + 1 + 2 + 1 + 1 + 2), 0 failed/skipped | 10 | 15 |
| CP-6 | all | CP-3 | in-place-task-input | `/*/*/(TaskInputWeakArmBoundaryTests*)\|(TaskInputAttentionCostTests*)/(Boundary_matches_the_base_outcome*)\|(Single_query_boundary_matches_frozen_builder*)\|(Results_are_byte_identical_to_original_builder*)\|(Weak_arm_reads_a_bounded_window*)` | A-2: task-input tests that edit prompt rows in place | 48 executed (17 + 11 + 17 + 3), 0 failed/skipped | 48 | 15 |

Every OR operand has parentheses and a trailing `*`. The other filters are one class, or one
method prefix with a trailing `*`. The class-by-method OR products in CP-5 and CP-6 match only the
intended pairs, because each method name exists in exactly one class of `tests/`.

Import results: recorded after the read-only import below.

Run timeout: do not pass `--total-timeout`, or pass at least the derived `2*sum+10`: 40 min for
CP-1 alone, 40 min for CP-2 alone, and 116 min for CP-3..CP-6. The manifest `totalMinutes: 90`
is the legacy default, not a cap.

## Performance budget

- **Selection:** 6 rows. Three cold `Antiphon.Tests` builds (CP-1, CP-2, CP-3) and three reuses
  (CP-4, CP-5, CP-6). Ordinary floor = 15 + 15 + 15 + 8 + 15 + 15 = **83 min** of estimates across
  three runs; the estimates are deadline inputs, not expected durations. No row is expected over
  30 min. A row that exceeds 30 min gets a Debug task with its timing. The first row's 15 min is
  the 45-minute build deadline (`3 * 15`).
- **Per-test (provisional, UNOBSERVED):** no timing sample exists for the new methods. Seeding
  400 or 1,600 prompts plus 2,000 assistant rows is one EF batch per test. The loads are the body.
  W-8 is 320 loads plus 40 single-row commits on one session, the largest new body. No host, no
  AppHost, no external call. No new `[Timeout]`. If Code adds one, use about 2x the measured p95
  and keep 30 s as the provisional ceiling (60 s for W-8) until a TRX p95 exists. Code records
  per-test durations from the TRX.
- **CP-5/CP-6 (UNOBSERVED as a selection):** `C0824RefOnlyReceiptTests` alone is 42.7 s on
  server2 at load 14.8 and carries `[Timeout(170_000)]` (its own header). The others have no
  recorded p95 here. `TaskInputWeakArmBoundaryTests` and `TaskInputAttentionCostTests` are
  `[NotInParallel("AgentQueue")]`, so CP-6 runs its 48 cases serially. Report actual wall time.
- **Phases:** `TestDbFixture` owns container, readiness and migrations once per assembly
  (unchanged). Each test isolates by a new session id and (except W-1 `select`) a private cache.
  Data preparation is in-test. Teardown is the fixture dispose. Build and slot wait stay on the
  `CHECKPOINT` line, not in the test body.
- **CP-4 estimate** is UNOBSERVED on this host. 13 small integration cases. Report the actual
  wall time.
- **Production:** on a stamp hit, one statement per `LoadAsync`: a count over the session's prompt
  index entries and four one-row index scans with one heap fetch each, no `Text`, no transaction.
  The 5 s tick keeps today's call rate. A miss that proves the prefix runs `BEGIN`, the proof
  statement, the tail of new prompt rows, `COMMIT`. A miss that does not reads today's full
  projection once, then hits. A session over the per-entry ceiling keeps today's full read on
  every call. Mean milliseconds are **UNOBSERVED**. Retained memory is bounded at 32,000,000
  chars plus the 48-per-row stand-in (about 64 MB of text); the real footprint is **UNOBSERVED**
  (M-7).

## 7. Collisions, order, rollout

- **CARD-1888 Code `6ea96bcb`**, tip `cd222da3c684fa3b049502fd0ce956c77b7a9a0c`, not on master.
  Files: `AgentSessionRuntime.cs`, `TranscriptIngestPrelude.cs`, `TranscriptIngestPrefix.cs`,
  `PolicyRefreshService.cs`. **No shared file with this slice.** Either may land first.
- **CARD-1969 slice 1** (`804edf5f`; plan review `39f3b245` at `57d7a637e5f2273ff746f7a0dcd083a0b1a70bb7`,
  clean, no amendments) edits the recovery reads in `AgentSessionRuntime.cs`, adds
  `TranscriptRecoveryRead.cs`, and in its S3 adds a paragraph to `docs/session-runtime-invariants.md`
  §CARD-1205 S4 (line 343). This slice's S3 also edits that file, in a new subsection after line
  1608. **Shared file, disjoint hunks**; whichever lands second runs `git merge-tree` against the
  first and re-reads the doc. Neither has to land first.
- **CARD-1915** summary laterals and **CARD-1933** invalidation: no shared file. This slice does
  not read the summary.
- **CARD-1910 / CARD-1914:** already in this master. Leave them.
- **Order:** S1, then S2, then S3, on this branch. Land S2 and S3 as one Merge (D-9).
- **Rollout:** server code and one doc paragraph. No migration, no index, no setting. Activation
  is the rc port of that Merge plus the AppHost restart (desktop) or the server2 production
  restart after the CARD-1744 window. Confirm `GET /api/version` afterwards. A master-only land is
  not activation.
- **Waiting sessions:** this plan adds or changes no session that waits for input.
- **Review:** round 2 is diff-only from `241f89463977945e43d7e8414edd5221501ba531`. Focus for the
  code review: a hit and a tail produce the same `RawPrompts` / `TurnPrompts` / `Notifications` as
  today's full read; the stored stamp is derived from stored rows; the miss path is one
  `REPEATABLE READ` transaction; no I/O under the lock; W-6 stays green; W-3, W-4, W-5, W-7 and
  W-9 are red in the CP-2 receipt at the S2 SHA. Review runs
  `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <code base> -HeadRef <tip>`.

## 8. Expected Postgres effect, and what to measure

Statement 3 in the attribution window was 609 calls, 971 rows/call, 45.1 s (0.070 core, 12.9%
of the measured statement time). That statement is this projection.

After deploy the hot path is `prompt-span.stamp`: **rows/call = 1**. Calls per minute should
stay near today's statement-3 rate (about 56/min in that 647 s window) as long as the tick
stays 5 s and the open-session count stays in that range. Both inputs are **UNOBSERVED** on
the desktop override (attribution M-5). `prompt-span.proof` and `prompt-span.tail` appear when a
prompt is appended and the prefix proof holds; tail rows/call is the number of new prompt rows,
often 1. `prompt-span.read` appears on a cold cache, an eviction, an idle expiry, a session over
the per-entry ceiling, a transaction bypass, and a failed proof; rows/call stays on the order of
today's 971 for those calls. The untagged four-column statement should stop increasing once
every path is tagged.

**UNOBSERVED:** the stamp's mean time, the share of calls that are hits, how much of the 45.1 s
was TOAST versus planning, the average prompt `Text` length, and the retained cache size. Do not
treat "a few milliseconds" as a measurement. The remaining O(prompt index entries) count is the
same shape as slice 1's count, on about 971 index tuples rather than about 412,000 heap rows.

**Orchestrator, after the rc is running (M-6):** a `pg_stat_statements` delta of about 10 min.
Record, per queryid: calls, rows/call, mean, and calls/min for `prompt-span.stamp`,
`prompt-span.proof`, `prompt-span.tail` and `prompt-span.read`. Confirm the old untagged
`Sequence, Text, Timestamp, Kind` statement's calls are flat. A stamp mean that is still
hundreds of milliseconds is a failed deploy of this slice, not a tuning follow-up. A
`prompt-span.read` rate near the stamp rate means sessions are over the ceiling or churning:
record it and file a card with the numbers, do not retune in place.

**Orchestrator, memory (M-7):** record the server process working set (desktop: `Get-Process`
private bytes of the server; server2: `docker stats --no-stream` for the server container) at
the same time of a normal working period on the day before and the day after the restart, plus
one sample after at least 30 min of steady dispatch. A rise above the 64 MB text bound plus
normal variance is a finding: file a card with the samples. If `dotnet-counters` is available on
the host (**UNOBSERVED**), also record `gc-heap-size`.

## 9. Operator decisions

- **F-1.** Accept D-4: in-place updates of a stored prompt row (`Text`, `Timestamp`, and a
  `UserPrompt` / `QueuedUserPrompt` swap) are invisible to the stamp, because no production writer
  does them, the S3 re-grep stops the slice if one appears, and nine test classes that do it run
  in CP-5/CP-6? **Recommend yes.** Closing it needs a text hash (TOAST on the 5 s path) or `xmin`
  (a heap visit per prompt row on every hit).
- **F-2.** Accept the incomplete S2 commit so W-3, W-4, W-5, W-7 and W-9 have a committed red, with
  S2 and S3 landed in one Merge (D-9)? **Recommend yes.** Same precedent as CARD-1888 and slice 1.
  Mutation checks are waived.
- **F-3.** Keep the deferred-report sweep on every tick, and only skip the prompt-text read
  when the stamp matches (D-5)? **Recommend yes.** Skipping the sweep hides a prompt-less
  `TurnEnd` and the grace clocks.
- **F-4.** Ship with no new index and no summary table (D-6)? **Recommend yes.** The CARD-1773
  index serves the count and the per-kind `LIMIT 1` laterals. A covering index is a human-only
  migration and is not required for the hit path.
- **F-5.** Bound the process-local cache by one lock, 256 entries, 4,000,000 chars per entry,
  32,000,000 chars in total and a 120 s idle expiry, every eviction a full read, with no setting
  (D-7, D-12, D-13)? **Recommend yes.** The numbers are unmeasured choices; M-7 checks the
  footprint, and a session over the ceiling keeps today's behaviour.

## Import

Read-only import (`ANTIPHON_LINUX_COMPILER_POLICY=0`), amendment 1:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1969-s2-amend-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md --out .antiphon/c1969-s2-amend-import.yaml`

IMPORT_RESULT_PLACEHOLDER

Original import at `241f89463` (task 2048f2d0): exit 0, 4 rows, 0 warnings, `minExecuted`
8/8/17/12, timeouts 45/45/45/24 min.
