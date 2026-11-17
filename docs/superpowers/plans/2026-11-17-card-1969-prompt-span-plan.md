# CARD-1969 slice 2 plan: the prompt-span read must not re-read a whole session

Date: 2026-11-17. Plan task 2048f2d0 (Grok). Plan only: no code, no setting change, no restart and no SQL
against any production database.

- Source read: `origin/master` **7901e9ba537b757740c2913aa2211d513b0b31f2** (also this branch's base).
- Attribution note: `origin/feat/card-task-c747c0c4` commit `c684172fc9ba93b161b87b73faf006599ca34ef4`,
  sections 3.3 and 5 (statement 3). No SQL was run there or here.
- Slice 1 plan: `origin/feat/card-task-8626ac28` tip `804edf5f8015ad823988fc35e8c544b8bc386b03`.
  Its plan-review ref `origin/feat/card-task-39f3b245` is pushed at that same SHA. No review amendments.
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
- the tail is exactly the prompt rows with `Sequence > anchor`, read in the same snapshot as that proof.

**Fail closed to today's full read** when any proof is missing:

- no cache entry (first sight, process restart, eviction);
- the caller already has a database transaction (do not nest one, and do not publish a cache entry
  from that snapshot);
- anchor row gone, prefix count changed, first id changed, or the id at the anchor sequence changed
  (delete, rebase, whole-session replace);
- the tail's row count is not the stamp's count delta.

An in-place `Text` change does **not** change this stamp. That hole is D-4.

## 2. Ground truth

| Card or brief assumes | Code at 7901e9ba5 | Consequence |
|---|---|---|
| The span is a prompt window, maybe the last prompt | SQL returns every prompt row of the session (`:88-94`). `RawPrompts` keeps them all. | A sequence cut without a proven prefix hides barriers. §1. |
| The sweep is the hot caller | `SettleDeferredReportsAsync` calls `SelectAsync` every tick per open task (`:484`, `:4940`). 609/647 s fits that. Other callers are real and share `LoadAsync`. | The cache sits inside `LoadAsync`, so every caller gets it. The sweep is not rewritten. |
| CARD-1910 and CARD-1914 already shrank the read | They cut in-process CPU (floor index, `TrimStart`). `LoadAsync` still materialises every prompt row. | This slice is the row cut. Do not redo those cards. |
| A stamp of count, first id, max sequence and top id sees every change that matters | It sees append, middle delete, top-row replace and whole-session delete. It does not see an in-place `Text` update. | D-2 and D-4. Same hole CARD-1888 recorded. |
| Rows are only appended | Production inserts rebase above the stored max (`AgentSessionRuntime.cs:1481`; the only `TranscriptEntries.Add` sites are `:1513`, `:1612`, `:1648`). Production deletes found here: whole-session `ExecuteDeleteAsync` (`DataRetentionService.cs:372`) and the session-row cascade (CARD-1888 plan §2). No `ExecuteUpdate` on `TranscriptEntries` under `server/`. | Prefix proof is valid for production writers. Re-grep gate in S3. Tests do `ExecuteUpdate` `Text`; the cache must not serve those rows after a text change inside one test unless the test clears the cache (D-8). |
| The 5 s stamp read must be cheap | **UNOBSERVED** milliseconds. The statement does not read `Text`. | §4. Measure after deploy. Do not invent a p95. |
| Skipping the sweep when nothing was appended is safe | The sweep exists because a text-less turn never re-triggers settlement (`AgentTaskDispatcher.cs:4861-4888`). A `TurnEnd` is not a prompt, so a prompt stamp does not move. Grace is a wall clock. | Do not skip the sweep (D-5). |
| A covering index or summary table is required | `IX_TranscriptEntries_AgentSessionId_Kind_Sequence` (CARD-1773, `AppDbContext.cs:1751-1755`) already serves the kind filter, the count and `max(Sequence)`. Id is two heap fetches. Text leaves the hot path. | No migration (D-6). |

## 3. Design

### Chosen: cache the full prompt list, reuse it when a one-snapshot stamp matches, append a tail only when the prefix is proven

Inside `TranscriptPromptSpan.LoadAsync` only. No caller signature change. The C# filter
(`TurnPrompts`, `Notifications`, housekeeping, allowlist) stays as it is and runs on every call,
so a new rules-refresh id or a different `dispatchedAt` cannot be served a stale classification.
The cache stores the unfiltered prompt rows (the SQL list), not `Result`.

**Stamp**, one statement, tag `-- prompt-span.stamp`. One Postgres snapshot:

- `count(*)` of `UserPrompt` / `QueuedUserPrompt` for the session;
- `max("Sequence")`;
- `Id` of the lowest `Sequence` (`ORDER BY "Sequence" ASC LIMIT 1`);
- `Id` of the highest `Sequence` (`ORDER BY "Sequence" DESC LIMIT 1`).

`count` and `max(Sequence)` are index-only on `IX_TranscriptEntries_AgentSessionId_Kind_Sequence`.
That index does not include `Id` or `Text`, so the two ids are single-row heap fetches. The
statement does not read `Text`. Empty session: count 0 and null ids, and that is a cacheable stamp.

**Hit.** Cache entry for the session exists and its stamp equals this stamp. Return the cached
rows. Prompt-text rows read: 0.

**Tail.** Cache exists, `FirstId` is unchanged, `Count` and `MaxSequence` grew, and a same-snapshot
proof says the prefix is intact: prompt-row `count(*)` where `Sequence <= cached max` equals the
cached count, and the `Id` at that sequence equals the cached top id. Read prompt rows with
`Sequence > anchor` (tag `-- prompt-span.tail`). Accept the tail only when its row count equals
the count delta and its last id equals the new top id. Store `cached ++ tail` under the new stamp.
Otherwise fall through.

**Full read.** Today's projection (tag `-- prompt-span.read`), in the same case as a miss that
the tail cannot prove. Replace the cache entry with those rows and the stamp taken in that same
snapshot. This is today's answer, including the first call after restart.

**Transaction.** If `db.Database.CurrentTransaction` is already set, run today's full projection
and do not read or write the cache. Do not open a nested transaction.

**Cache shape.** Process-local, one immutable array per session, cap 256, least-recently used
eviction. Eviction drops an entry; the next call full-reads. A returned array is never mutated;
a tail stores a new array. Cap 256 covers the open sessions on one server and the parallel tests
inside `TranscriptPromptSpanTests` (D-7). Not a setting.

**Sweep.** `SettleDeferredReportsAsync` stays on the 5 s tick. On a stamp hit it still selects,
still evaluates the grace clocks, and still honours `ReportSweepRehandSeconds`. It stops shipping
prompt text when the prompt rows have not changed.

### Why a hit or a proven tail cannot hide a prompt

The list the selector and the watchdog classify is either today's full projection or a cached
full projection plus a tail of rows that did not exist at the anchor. Append changes `max` and
the top id, so the next call cannot hit. A middle delete changes `count` and fails the prefix
proof. A top row deleted and reinserted at the same sequence changes the id at the anchor.
A whole-session delete changes count, first id and top id. A null-timestamp row and a
pre-dispatch barrier stay in the cached prefix, so `LastBefore` and `NewestBetween` see them.
`TurnPrompts` is still the C# time filter over that complete list.

### Rejected

| Alternative | Why rejected |
|---|---|
| Read `Sequence >=` the task prompt, or the last span start, with no cached prefix | Not `RawPrompts`. Hides pre-dispatch barriers and a lower-sequence null timestamp. §1. |
| Push `Timestamp > dispatchedAt OR Timestamp IS NULL` into SQL and stop | That is `TurnPrompts`, not `RawPrompts`. The selector needs the pre-dispatch rows. It also re-reads the whole post-dispatch history every tick, which is the 971-row cost on a long session. |
| Cache invalidated only when this process appends | A delete, another process, or a restart does not append. The stamp is the check. An append hook is not required once every call reads the stamp. |
| Skip `SettleDeferredReportsAsync` when the prompt stamp is unchanged | A new `TurnEnd` and the final-message / subagent grace are not prompt rows (`:4861-4888`). Settlement would wait for the next prompt. D-5. |
| Also cache the selector and skip its `TurnEnd` read (`TaskReportTurnSelector.cs:83-87`) | That read is not statement 3. Its cost is **UNOBSERVED** (not in the attribution top 7). A second stamp is a later slice. |
| `md5` / `string_agg` over prompt `Text` | Still visits TOAST for every prompt. That is the CPU this slice removes from the 5 s path. |
| CARD-1888 `IngestPrefix` or the CARD-1915 summary | Different question and lifetime. The summary flag is off; CARD-1933 says EF batch appends invalidate it; the summary has no prompt text. |
| Covering index or a summary table | Human-only migration, and the existing kind index already serves the stamp. D-6. |
| Share slice 1's recovery stamp type | Slice 1 compares two observations a few seconds apart. This cache lives across ticks and stores prompt text. No shared file. |

## 4. Slices

| Slice | Files | Content |
|---|---|---|
| S1 | `tests/Antiphon.Tests/Application/TranscriptPromptSpanTests.cs` | W-1..W-6 below. No production change. Nested `PromptTextReadCounter : DbCommandInterceptor` and a `CreateContext(DbCommandInterceptor)` used only by the new tests. Existing helpers stay. |
| S2, deliberately incomplete, never landed alone | `server/Application/Services/TranscriptPromptSpan.cs` | Cache keyed by session and `max(Sequence)` only. Equal max: return the cache and read no prompt text. Any other max, a missing entry, or an open transaction: today's full read, then store. No count, no first id, no top id, no tail. |
| S3 | `TranscriptPromptSpan.cs` | The stamp, the prefix proof, the tail, the full-read fallback, the transaction bypass, the 256 cap, and the S3 re-grep gate. |

S2 makes W-1 and W-2 green and leaves W-3, W-4 and W-5 red, so those three are not stubs (D-9).

### Must NOT touch

- `AgentSessionRuntime.cs`, `TranscriptIngestPrelude.cs`, `TranscriptIngestPrefix.cs`,
  `PolicyRefreshService.cs`: CARD-1888 Code `6ea96bcb` (tip `cd222da3c`).
- `AgentSessionRuntime.cs` `:1062-1540`, `StaleWorkingTranscriptRecovery.cs`, and slice 1's
  `TranscriptRecoveryRead.cs`: CARD-1969 slice 1.
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
  Three new SQL comments: `prompt-span.stamp`, `prompt-span.read`, `prompt-span.tail`.
- Behaviour that must stay green: the 9 existing `TranscriptPromptSpanTests` methods;
  `TaskReportTurnSelectorTests` (2); `TaskReportHousekeepingTests` (10). No `[Arguments]` on
  those 12. `HasTurnPromptSinceAsync` / `HasTurnPromptAfterAsync` keep their answers because
  they call `LoadAsync`.

## 5. Decisions

- **D-1.** Cache the unfiltered prompt rows inside `LoadAsync`. Reuse them when the stamp matches.
  Append a tail only when the prefix proof holds. Otherwise today's full read. Reason: that list
  is what `RawPrompts`, `TurnPrompts` and `Notifications` are computed from, for every caller. §3.
- **D-2.** The stamp is `(Count, FirstId, MaxSequence, TopId)` of the prompt-kind rows, read in
  one statement. Reason: count sees a middle delete; the id at the anchor sees a same-sequence
  replace; max sees an append. One statement is one snapshot, so the four fields cannot tear.
- **D-3.** Fail closed to the full read when the cache, the prefix proof, or an ambient
  transaction is missing. Reason: a wrong anchor hides a prompt (§1). The full read is today's
  behaviour, so the fallback cannot be a new miss.
- **D-4.** An in-place `Text` update is not detected. No production statement does it (§2).
  The S3 re-grep is the gate: any new `ExecuteUpdate`, `ExecuteDelete` of one row, raw
  `UPDATE`/`DELETE`/`INSERT` on `TranscriptEntries`, or an insert whose `Sequence` is not rebased
  above the stored max, stops the slice (report blocked). Whole-session `ExecuteDeleteAsync` at
  `DataRetentionService.cs:372` stays listed and is detected by the stamp (count and ids change).
- **D-5.** The deferred-report sweep still runs every tick. Only the prompt-text read is skipped
  on a hit. Reason: grace and a prompt-less `TurnEnd` are the sweep's job (`:4861-4888`).
- **D-6.** No migration and no new index. Reason: CARD-1773's kind index serves the stamp.
  A covering index would still be a human-only gate and is not what makes the hit cheap.
- **D-7.** Process-local cache, cap 256, LRU, not a setting. Reason: the hot set is the open
  sessions, not the history of every session the process has ever seen. 256 is above the
  parallelism of this class's checkpoint. Eviction full-reads, which is safe.
- **D-8.** The counter is a private nested class. `TranscriptPromptSpan.ClearCacheForTests` is
  internal and the new witnesses call it before the first read. Reason: no new test file, so
  `tests/derived-impact-index.json` stays untouched. A test that `ExecuteUpdate`s prompt `Text`
  and then calls `LoadAsync` must clear the cache or it will see D-4; Code searches for that
  shape and clears it in that test. Do not weaken the assertion.
- **D-9.** S2 is committed red for W-3, W-4 and W-5. S2 and S3 land in one Merge, never separately.
  An rc port carries them the same way. Reason: mutation checks are waived (operator 2026-11-12);
  this is the CARD-1888 / slice 1 precedent.
- **D-10.** No new metric or route. After deploy, `pg_stat_statements` and the three tags are the
  measurement (§8).

## 6. Verification design

Witnesses are new methods on `TranscriptPromptSpanTests` (Integration, shared `TestDbFixture`).
`SeedPrompts(n)` writes `n` `UserPrompt` rows with distinct text and ids, timestamps after
dispatch, plus 2,000 `AssistantText` rows that the prompt projection must not return.
`PromptTextReadCounter` counts reader rows whose command text contains `TranscriptEntries`,
`"Text"` and `"Timestamp"`. `Reset()` runs between the two loads. The counter is read before any
later assertion query.

| ID | Test (cases) | Assertion | At 7901e9ba5 | At S2 | At S3 | Why, from the code |
|---|---|---|---|---|---|---|
| W-1 | `C1969s2_unchanged_second_read_materialises_no_prompt_text` (`(400,"load")`, `(1600,"load")`, `(400,"select")`) | Second prompt-text row count is 0. First count is `n`, not `n + 2000`. `select` calls `TaskReportTurnSelector.SelectAsync` twice with an in-memory `AgentTask`. | **Red.** Second count is `n`. | Green | Green | `:88-94` runs on every `LoadAsync`, and `SelectAsync` `:64` calls it. Nothing remembers the rows. |
| W-2 | `C1969s2_new_prompt_is_observed` (1) | After the first load, insert one prompt at `max+1`. The second `RawPrompts` and `TurnPrompts` contain it. | Green | Green | Green | A full read sees it. S2 full-reads because max changed. S3's tail includes it. |
| W-3 | `C1969s2_new_prompt_does_not_reread_the_prefix` (1) | Same shape. Second prompt-text row count is 1. | **Red.** Second count is `n+1`. | **Red.** S2 full-reads on a new max. | Green | Tail only exists in S3. |
| W-4 | `C1969s2_deleted_prompt_is_absent` (1) | `n = 400`. After the first load, delete the prompt at index 200 through a second context (children of a Restrict FK first, as slice 1 does). Second `RawPrompts` lacks that id. Count `n-1`. | Green | **Red.** Max is unchanged, so S2 returns the deleted row. | Green | Prefix count changes. S3 full-reads. |
| W-5 | `C1969s2_rebased_top_row_is_observed` (1) | After the first load, delete the top prompt and insert a new id at the same `Sequence` with new text. Second top text is the new text. | Green | **Red.** Max is unchanged. | Green | The id at the anchor changed. S3 full-reads. |
| W-6 | `C1969s2_null_timestamp_below_a_later_pre_dispatch_sequence_stays` (1) | Seq 1 null timestamp, seq 2 timestamp before dispatch, seq 3 after. Both loads: `TurnPrompts` are seq 1 then seq 3; `RawPrompts` has all three, in sequence order. | Green | Green | Green | `:96-98` keeps a null timestamp. A `Sequence >=` cut that starts at seq 2 drops seq 1. |

Red at base is W-1 and W-3 only. They count reader rows of the prompt projection, so a constant
or a self-compare cannot satisfy them while the outcome still contains the rows. W-4 and W-5 are
green at base because the full read sees the delete and the new id. S2 omits the checks that see
them, so CP-2 is the committed red. W-6 is green on every slice and goes red if Code switches to
an unproven sequence window.

`C1969s2_` has 0 hits in this tree. Class-name check: `TranscriptPromptSpanTests`,
`TaskReportTurnSelectorTests` and `TaskReportHousekeepingTests` each match only their own class.

### Checkpoints

Lane: the runner-defaults global host (linux). Omit `-Runner` and `-Platform`. Export
`ANTIPHON_LINUX_COMPILER_POLICY=0` before every dotnet command. One isolated build per `After`
group. Each `After` group is one `--serial` run without `--row-timeout`: CP-1 at the S1 commit,
CP-2 at the S2 commit, CP-3 and CP-4 together at the tip. Red rows run once at their slice and
are not re-run after they go green.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1969-s2-red/` | c1969-s2-red | `/*/*/TranscriptPromptSpanTests/C1969s2_*` | W-1..W-6 red proof | 8 executed; W-1 (3) and W-3 (1) failed on the row bound (`n` and `n+1`); W-2, W-4, W-5, W-6 passed | 8 | 15 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1969-s2-stamp/` | c1969-s2-stamp | `/*/*/TranscriptPromptSpanTests/C1969s2_*` | W-3..W-5 committed red | 8 executed; W-3, W-4 and W-5 failed; W-1 (3), W-2 and W-6 passed | 8 | 15 |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1969-s2-green/` | c1969-s2-green | `/*/*/TranscriptPromptSpanTests/*` | W-1..W-6 and the 9 existing span methods | 17 executed (9 + 8), 0 failed/skipped | 17 | 15 |
| CP-4 | all | CP-3 | span-callers | `/*/*/(TaskReportTurnSelectorTests*)\|(TaskReportHousekeepingTests*)/*` | selector and housekeeping still classify the same rows | 12 executed (2 + 10), 0 failed/skipped | 12 | 8 |

Every OR operand has parentheses and a trailing `*`. The other filters are one class, or one
method prefix with a trailing `*`.

Import results: recorded after the read-only import below.

Run timeout: do not pass `--total-timeout`, or pass at least the derived `2*sum+10`: 40 min for
CP-1 alone, 40 min for CP-2 alone, and 56 min for CP-3..CP-4. The manifest `totalMinutes: 90`
is the legacy default, not a cap.

## Performance budget

- **Selection:** 4 rows. Three cold `Antiphon.Tests` builds (CP-1, CP-2, CP-3) and one reuse
  (CP-4). Ordinary floor = 15 + 15 + 15 + 8 = **53 min** across three runs. No row is expected
  over 30 min. A row that exceeds 30 min gets a Debug task with its timing. The first row's
  15 min is the 45-minute build deadline (`3 * 15`).
- **Per-test (provisional, UNOBSERVED):** no timing sample exists for these methods. Seeding
  400 or 1,600 prompts plus 2,000 assistant rows is one EF batch per test. The two loads are
  the body. No host, no AppHost, no external call. No new `[Timeout]`. If Code adds one, use
  about 2x the measured p95 and keep 30 s as the provisional ceiling until a TRX p95 exists.
  Code records per-test durations from the TRX.
- **Phases:** `TestDbFixture` owns container, readiness and migrations once per assembly
  (unchanged). Each test isolates by a new session id. Data preparation is in-test. Teardown
  is the fixture dispose. Build and slot wait stay on the `CHECKPOINT` line, not in the test
  body.
- **CP-4 estimate** is UNOBSERVED on this host. 12 small integration methods. Report the actual
  wall time.
- **Production:** on a stamp hit, one statement per `LoadAsync`: index-only count and max over
  the session's prompt index entries, plus two id fetches, and no `Text`. The 5 s tick keeps
  today's call rate. A miss that proves the prefix reads only the new prompt rows. A miss that
  does not reads today's full projection once, then hits. Mean milliseconds are **UNOBSERVED**.

## 7. Collisions, order, rollout

- **CARD-1888 Code `6ea96bcb`**, tip `cd222da3c684fa3b049502fd0ce956c77b7a9a0c`, not on master.
  Files: `AgentSessionRuntime.cs`, `TranscriptIngestPrelude.cs`, `TranscriptIngestPrefix.cs`,
  `PolicyRefreshService.cs`. **No shared file with this slice.** Either may land first.
- **CARD-1969 slice 1** (`804edf5f`, review ref `39f3b245` at the same SHA, no amendments) edits
  the recovery reads in `AgentSessionRuntime.cs` and adds `TranscriptRecoveryRead.cs`. **No shared
  file.** Plan and code may proceed beside it. It does not have to land first.
- **CARD-1915** summary laterals and **CARD-1933** invalidation: no shared file. This slice does
  not read the summary.
- **CARD-1910 / CARD-1914:** already in this master. Leave them.
- **Order:** S1, then S2, then S3, on this branch. Land S2 and S3 as one Merge (D-9).
- **Rollout:** server code only. No migration, no index, no setting. Activation is the rc port
  of that Merge plus the AppHost restart (desktop) or the server2 production restart after the
  CARD-1744 window. Confirm `GET /api/version` afterwards. A master-only land is not activation.
- **Waiting sessions:** this plan adds or changes no session that waits for input.
- **Review:** round 1 is full. Focus is the delivery invariant: a hit and a tail produce the
  same `RawPrompts` / `TurnPrompts` / `Notifications` as today's full read, W-6 stays green,
  and W-4/W-5 are red in the CP-2 receipt at the S2 SHA. Review runs
  `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <code base> -HeadRef <tip>`.

## 8. Expected Postgres effect, and what to measure

Statement 3 in the attribution window was 609 calls, 971 rows/call, 45.1 s (0.070 core, 12.9%
of the measured statement time). That statement is this projection.

After deploy the hot path is `prompt-span.stamp`: **rows/call = 1**. Calls per minute should
stay near today's statement-3 rate (about 56/min in that 647 s window) as long as the tick
stays 5 s and the open-session count stays in that range. Both inputs are **UNOBSERVED** on
the desktop override (attribution M-5). `prompt-span.tail` appears when a prompt is appended
and the prefix proof holds; rows/call is the number of new prompt rows, often 1.
`prompt-span.read` appears on a cold cache, an eviction, a transaction bypass, and a failed
proof; rows/call stays on the order of today's 971 until the session is cached. The untagged
four-column statement should stop increasing once every path is tagged.

**UNOBSERVED:** the stamp's mean time, the share of calls that are hits, and how much of the
45.1 s was TOAST versus planning. Do not treat "a few milliseconds" as a measurement. The
remaining O(prompt index entries) count is the same shape as slice 1's count, on about 971
index tuples rather than about 412,000 heap rows.

**Orchestrator, after the rc is running (M-6):** a `pg_stat_statements` delta of about 10 min.
Record, per queryid: calls, rows/call, mean, and calls/min for `prompt-span.stamp`,
`prompt-span.tail` and `prompt-span.read`. Confirm the old untagged
`Sequence, Text, Timestamp, Kind` statement's calls are flat. A stamp mean that is still
hundreds of milliseconds is a failed deploy of this slice, not a tuning follow-up.

## 9. Operator decisions

- **F-1.** Accept D-4: an in-place prompt `Text` update is invisible to the stamp, because no
  production writer does it, and the S3 re-grep stops the slice if one appears?
  **Recommend yes.** Closing it needs a text hash, which re-reads TOAST on the 5 s path.
- **F-2.** Accept the incomplete S2 commit so W-3, W-4 and W-5 have a committed red, with S2
  and S3 landed in one Merge (D-9)? **Recommend yes.** Same precedent as CARD-1888 and slice 1.
  Mutation checks are waived.
- **F-3.** Keep the deferred-report sweep on every tick, and only skip the prompt-text read
  when the stamp matches (D-5)? **Recommend yes.** Skipping the sweep hides a prompt-less
  `TurnEnd` and the grace clocks.
- **F-4.** Ship with no new index and no summary table (D-6)? **Recommend yes.** The CARD-1773
  index already serves the stamp. A covering index is a human-only migration and is not required
  for the hit path.
- **F-5.** Cap the process-local cache at 256 sessions, LRU, with no setting (D-7)?
  **Recommend yes.** Eviction falls back to today's read. A setting would be a behaviour knob
  this slice does not need.

## Import

Read-only import (`ANTIPHON_LINUX_COMPILER_POLICY=0`, slot lease waited 0 s, held 9 s):

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1969-s2-plan-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md --out .antiphon/c1969-s2-plan-import.yaml`

Exit 0. "imported 4 rows". 0 import warnings. The build printed the existing nullable and CA1416
compiler warnings in `Antiphon.HostCleanup` and `Antiphon.Checkpoints`; none named this table.
The yaml has 3 builds (`bin-c1969-s2-red`, `bin-c1969-s2-stamp`, `bin-c1969-s2-green`), row
timeouts 45/45/45/24 min, `minExecuted` 8/8/17/12, and the filters copied verbatim. CP-4 reuses
`bin-c1969-s2-green`. `totalMinutes: 90` is the legacy default.
