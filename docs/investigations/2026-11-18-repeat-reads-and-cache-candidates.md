# Repeat reads and in-memory cache candidates (CARD-1987, CARD-1969, CARD-1888)

Task 24283212 (Investigate). Read-only: code, the desktop server log, and three read-only HTTP GETs
(`/api/agents`, `/api/agent-tasks?boardId=..`, `/api/diagnostics/session-state`, which returns
in-process counters only and runs no DB query). No Postgres query, no setting change, no restart. No
transcript text was read or printed.

- Source read: master `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (= `origin/master` at 06:20Z, = the
  running server: `GET /api/version` returned the same SHA).
- The orchestrator's pg_stat_statements numbers (window 05:52:19Z-06:02:45Z, 626 s) are taken as given.
- Server process: pid in `/api/diagnostics/session-state`, `processStartedAt` 2026-11-18 01:16:01Z.
  Counter snapshots at 06:23:30Z and 06:24:01Z (31.3 s apart). Census at ~06:22Z, about 20 minutes
  after the pg window, so it is the nearest available population, not the window's own.
- CARD-1987 plan (`docs/superpowers/plans/2026-11-18-card-1987-query-rate-and-hot-path-caching-plan.md`
  on `feat/card-task-fa6c783e`): it was pushed while this note was being written. Read at
  `7fc39b0f073c2dbaff23f5c812b1c941ca754613`; see "Against the CARD-1987 plan" at the end.

## Headline

1. **Yes, the same transcripts are re-read, and the clearest case is ingestion, not prompt-span.**
   Since 01:16Z the server took 14,434 ingest calls. 10,970 were all-duplicate batches (76%). 10,605
   took the prefix-memo path with no UUID probe (`rt.ingest.prelude`). The other ~3,829 ran **43,802**
   `session-state.identity` commands. That is ~11.4 per call, each probing up to 512 UUIDs, so about
   5,800 already-stored UUIDs are re-probed per non-memo ingest. In the 31 s sample, 13 non-memo ingests
   ran 101 identity commands (7.8 each, 194/min).
2. **prompt-span.read still runs because the cache's full-read path has four entries and nothing counts
   which one fires.** These are cold, idle expiry past 120 s, a failed proof, and an oversize entry
   (>4,000,000 chars, returned but never stored). There is no counter, and `prompt-span.*` is not in
   the `DbCommandMetrics` allowlist. The 352 ms mean favours large sessions, so the oversize or cold
   case is the leading hypothesis. It is UNOBSERVED.
3. **session-state.seed is mostly first touch of distinct sessions, not a repeat.** 772 loads since
   start, 641 resident entries and 27 evictions, so at most ~131 repeat loads in 5.1 h (ESTIMATE ≤17%).
   Each seed **also clears the CARD-1888 ingest memo** (`SessionStateStore.cs:207`). The next snapshot
   ingest of that session then pays the full UUID re-probe in point 1.
4. Two of the five named statements cannot be pinned without their statement text. No
   `AgentSessionEvents` table exists anywhere at this SHA (repo-wide grep, 0 hits). The
   `SessionQueuedMessages EXISTS` shape has 29 `AnyAsync` sites.

## Q1. Call sites and triggers

| Statement | Call site | Trigger |
|---|---|---|
| `prompt-span.read` | `TranscriptPromptSpan.ReadRowsAsync` `server/Application/Services/TranscriptPromptSpan.cs:196-220`, tag `:193`. Issued from `LoadPromptRowsAsync` `:134-164`: ambient transaction or no connection string `:138-140` (bypass), cold `:144-150`, proof fallback `:190`. | Every `TranscriptPromptSpan.LoadAsync` that misses. Callers: (a) **dispatcher tick**: `SettleDeferredReportsAsync` `AgentTaskDispatcher.cs:4890` runs `TaskReportTurnSelector.SelectAsync` (`TaskReportTurnSelector.cs:64`) for **every open Dispatched/Working task on every tick** (`:4902-4940`). (b) Delivery watchdog `FailNeverStartedAsync` `AgentTaskDispatcher.cs:2511`, per suspect per tick. (c) Settlement `AgentTaskReplyService.cs:3357,3412,1972,1977` per report/turn end. (d) `CardTaskSettlement.cs:261` on closed-card sweep. (e) `ApiErrorRecoveryService.cs:302` per due recovery. (f) `LateFailedReportForwarder.cs:99`. Tick = `Delegation:PollIntervalSeconds` default 5 s (`DelegationSettings.cs:28`). In the window the observed tick was **~17 s**: the watchdog line for task cb93cdf7 repeated 36 times in 626 s (server log). |
| `session-state.seed` | `SessionStateLoader.LoadAsync` `server/Infrastructure/Data/SessionStateLoader.cs:90-109`; SQL `:39-88` (whole-session `count(*)`, activity aggregate, 7 laterals, `open_tasks AS MATERIALIZED`). | `SessionStateStore.LoadAsync` `SessionStateStore.cs:202`, batched ≤512 ids. It runs on (a) a cold entry at the first read of a session id (`EnsureLoadedAsync` `:189-200`, via `ReadAsync`/`IsWorkingBatchAsync` from `AgentService.cs:128,186` (GET /api/agents), `AgentSessionRuntime.cs:1293`, `TerminalRunnerSeatReleaseService.cs:920,1069`, `SessionMessageQueueService.cs:5983`), (b) the first ingest of a session (`BeginWriteAsync` `:83-93`), (c) reload after an uncertain or failed ingest commit (`AgentSessionRuntime.cs:1422,1441`; `NeedsReload` `:1661,1774,1787`; `ReceiptReadiness.cs:88`), (d) a retention mutation (`SessionStateStore.cs:164-176`, `DataRetentionService.cs:398`, `SessionStateDeletion.cs:37`), (e) the one-minute warm-up for newly pinned ids (`SessionStateWarmupService.cs:16-19`, `WarmAsync` `:237-246`), and (f) re-read after a 15-min idle eviction (`TerminalIdleMinutes` 15, `SessionStateSettings.cs:8`; `EvictIdleLocked` `:248-258`). |
| `session-state.identity` (the `SELECT 0 AS "Part", s."StartedAt" …` prelude shape; pg normalizes the `0`) | `TranscriptIngestPrelude.ReadAsync` `server/Infrastructure/Data/TranscriptIngestPrelude.cs:97-132`, tag `:17,123` (identity when the batch carries UUIDs, else `rt.ingest.prelude`). Further 512-UUID chunks are separate commands with the same tag at `AgentSessionRuntime.cs:1532-1540`. | **Per transcript ingest batch** (`PersistTranscriptCoreAsync` `AgentSessionRuntime.cs:1468-1512`), i.e. per runner transcript snapshot or stream batch. With `recordCandidate` (`:1499`, true whenever the memo is null **or the batch is at least as long as the memo**, which holds for every growing snapshot), part 0 adds a **whole-session `count(*)`** plus first/top row (`TranscriptIngestPrelude.cs:29-64`). |
| `SessionQueuedMessages EXISTS` | Not pinned: 29 `AnyAsync` sites in 18 files. Per-tick or per-event candidates: `SessionMessageQueueService.cs:1855` (`OnTurnEndCoreAsync`, per TurnEnd), `:4027/:4032` (per prompt-confirm classify), `:6301` (`TryPollLocalCommandAsync`, poll), `:4167` (launch baseline), `:805` (enqueue). The `session-state.pins` EXISTS is inside that tagged statement (308 calls since start), so it is not this one. | UNOBSERVED. Resolved by the pg_stat_statements `query` text for that row (or its `queryid` plus the column list). |
| `AgentSessionEvents select` | **No such table or DbSet at this SHA.** A repo-wide grep for `AgentSessionEvents` has 0 hits. Nearest tables: `AgentTaskEvents` (`AppDbContext.cs:92`), `HostLoadObservedEvents`. | UNOBSERVED. The label needs the statement text. |

The cheap-but-numerous rows are **untagged**. `dbCommands` shows 6,203 of 6,410 commands (97%) untagged
in the 31 s sample, 12.3k commands/min in total, so call sites cannot be pinned from counters.

## Q2. Are the same sessions read over and over?

Population (census ~06:22Z, ids/counts only): 2,971 agents, 21 live sessions, 11 Working
(b2f1a869, de753196, 4c1872cd, 45ce74fc, 88c0d555, 901e84c6, b1ad6662, 4c1dcf63, 21cd89d5, b0a2b667,
7f94cbab), 10 open Dispatched/Working tasks on 10 distinct sessions (16 boards enumerated). The
diagnostics showed `liveSessions` 18-19, `cachedSessions` 640-641.

| Statement | Calls/10 min | Sessions | Reads per session (label) |
|---|---|---|---|
| prompt-span.read | 128 (12.3/min) | ≤ ~11 sessions with open tasks or watchdog suspects | ~1.1/min per session if spread evenly. ESTIMATE, attribution UNOBSERVED. The deferred sweep alone calls `LoadAsync` ~10 tasks × 3.5 ticks/min ≈ 35/min, so roughly 1 in 3 calls takes a full read (ESTIMATE). |
| identity (prelude + chunks) | pg heavy shape 39 (3.7/min); all identity commands 194/min (31 s sample) | ~18-21 live sessions | ~75 ingest calls/min across live sessions ≈ 3.6-4.2 per session per minute. Each non-memo ingest re-reads ~4,000-5,800 stored UUIDs (counters: 101 commands / 13 ingests; 43,802 / 3,829 since start). ESTIMATE per session; the repeat itself is OBSERVED from the counters. |
| seed | 46 (4.4/min); 7.7/min in the 31 s sample | mostly distinct | Since start ≤17% repeat loads (above). Mostly NOT the same sessions. |
| delivery watchdog (log) | 36 hand-offs, 1 session | 9f263c85 (task cb93cdf7) | **3.5/min on one session, OBSERVED** in the log. Every hand-off runs `CatchUpTranscriptAsync` and `TranscriptPromptSpan.LoadAsync` (`AgentTaskDispatcher.cs:2503,2511`). The same session also logged 54 "Running brief recovery could not attach" lines. A stuck suspect re-reads its transcript every tick indefinitely. |

**The log does not carry session ids for these reads.** The server Serilog file
(`C:\src\Antiphon\server\logs\antiphon-*.log`, `docs/logs.md` row `desktop-server`) logs
`LIVE_SLOW_CALL … phase=db-query` (36 lines, 53.7 s in the window, max 3.6 s) with no tag or session.
`RuntimePhase` logs `session=` only at Debug (`RuntimePhase.cs:32`), and Debug is not shipped. PROPOSAL
ONLY: one Information line per prompt-span full read would carry it:
`prompt-span.full session={SessionId} reason={cold|idle|proof|oversize|bypass} rows={n} chars={c} elapsedMs={ms}`.
Alternatively, a per-session top-N counter in `/api/diagnostics/session-state`.

## Q3. What is already cached, and why prompt-span.read still runs

| Cache | Key | Bound / invalidation | Counters |
|---|---|---|---|
| CARD-1969 S2 `TranscriptPromptSpan.PromptCache` (`TranscriptPromptSpan.cs:391-504`) | (connection string, session id) `:419`, process-local static `Shared` `:396` | 256 entries, 4,000,000 chars/entry, 32,000,000 total, **120 s idle** (`:396-397`). Every use pays one `prompt-span.stamp` statement (`:152`). On a stamp change it reads `prompt-span.proof` then `prompt-span.tail` in RR (`:155-163,171-191`). A failed proof gives a full read. Whole-session delete is detected by the stamp. | **None.** `Snapshot()` `:486` (entries, chars) has no caller in `server/`. The `prompt-span.` prefix is absent from `DbCommandMetrics.Prefixes` (`DbCommandMetrics.cs:21`). |
| CARD-1888 ingest prefix memo `TranscriptIngestPrefix` on `SessionStateStore.Entry.IngestPrefix` (`SessionStateStore.cs:286,314`) | session id (under the session lease) | Recorded only on a clean commit or all-duplicate (`AgentSessionRuntime.cs:1665-1683`). **Cleared on every seed, reconcile or reset** (`SessionStateStore.cs:207`) and on a bulk delete (`:138`). Verified each use against DB first row, row at stored max, and prefix count (`AgentSessionRuntime.cs:1520`). | Indirect only: `rt.ingest.prelude` = memo-covered batches with no new UUIDs. `duplicateBatches` is in `/api/diagnostics/session-state`. No memo hit/miss/verify-fail counter. |
| CARD-1403 `SessionStateStore` committed projection (`SessionStateStore.cs:14-342`) | session id | MaxSessions 4096, eviction after 15 min idle when unpinned (`SessionStateSettings.cs:6-8`), appended in-process on ingest (`Lease.PublishAsync` `:297-309`) | Yes: `hits`, `loads`, `evictions`, `loadFaults`, `capacityFallbacks`, `ingestCalls`, `committedRows`, `duplicateBatches` (`:332-339`). `efReadAttempts`: seed/pins/fallback/identity/binding (`SessionStateCommandMetrics.cs:10`). Since start: hits 217,261 / loads 772. |
| Other: `TranscriptReadHold` (`TranscriptReadHold.cs:13-16`, CARD-1798, 256 sessions) and `LandReceiptScanCache` (`LandReceiptScanCache.cs:271`, has hits/misses) | not on these statements | | |

Why `prompt-span.read` ran 128 times at 352 ms. From the code there are exactly five ways to reach
`ReadRowsAsync(..., ReadTag)`:
1. **Bypass**: an ambient transaction or a null connection string (`:138-140`). No current caller
   holds a transaction across `LoadAsync`. `ApiErrorRecoveryService`'s tx (`:102`) is in
   `EnsureAdoptedAsync`, not `FireOneAsync` (`:302`). The Review settlement tx (`AgentTaskReplyService.cs:2709`)
   wraps only the save. `Program.cs:133-142` uses `UseNpgsql(connectionString)`, so the connection
   string is non-null. **Unlikely.**
2. **Cold**: the first touch per session after a server start (start 01:16Z, 4.6 h before the window,
   so a small share).
3. **Idle expiry**: no use for >120 s. Sessions touched only at turn ends or settlement. The sweep
   tick (~17 s) keeps open-task sessions warm, so this mostly hits settlement-only sessions.
4. **Proof failure** (`:174-190`): the first id, prefix count or anchor id changed, or the tail count
   is off. The documented writers are append-above-max and whole-session delete only, so this should
   be rare.
5. **Oversize**: an entry over 4,000,000 chars is returned and **not stored** (`:466-467`), so **every
   call on that session is a full read** of all its prompt text. A session read on every tick by the
   sweep or watchdog then shows up as roughly one 300-400 ms read per tick. That matches the 352 ms
   mean much better than the cold read of a small delegate session.

Verdict: case 5 (or a related large-session case 3) is the leading hypothesis. It is **UNOBSERVED**.
One counter resolves it (Q5).

## Q4. What can be in memory safely

Writer facts. There is one server process (one pid in diagnostics). Runners phone transcripts to the
server API (`docs/logs.md`, `docs/ops-http.md`). There are exactly three production
`TranscriptEntries.Add` sites, all in ingest (`AgentSessionRuntime.cs:1596,1719,1755`), and one
whole-session delete (`DataRetentionService.cs:372`), both in-process. A second server process
pointed at the same DB (server2 production window, CARD-1744) would break any purely in-process
invalidation. Every proposal below keeps a DB proof or is fail-safe, for that reason.

Delivery-evidence guard: the transcript-confirmed UserPrompt is the delivery verdict. The prompt span
feeds the delivery watchdog and settlement, and the seed feeds Working. **Nothing may serve those
stale without a per-call freshness proof.**

| # | Candidate | Hold / key / staleness / invalidation | Guard | DB s saved per 10 min (ESTIMATE) | Risk | Rank |
|---|---|---|---|---|---|---|
| P1 | Prompt-span: stop full reads | First measure the read reason (Q5). Then fix the dominant case: oversize → store a text-free classified projection (seq, ts, kind, housekeeping/notification/allowlist flags, marker hits) under the same stamp; idle → lengthen the 120 s idle for sessions with an open task. Key (conn, session) unchanged. Staleness 0: the stamp check stays per call. | **SAFE** only while the per-call stamp check stays. Removing the stamp check is NOT SAFE (watchdog kill and settlement read it). | up to ~40 of 45 s (if the misses are cases 3/5) | Low (cache and proof exist) | 1 |
| P2a | Ingest: `SessionCount` for a memo record from `lease.Snapshot.Count` instead of the whole-session `count(*)` (`TranscriptIngestPrelude.cs:58-64`, `AgentSessionRuntime.cs:1682`) | Exact under the session lease: all inserts and deletes are in-process under the same gate. A wrong value only makes the next `StillStored` check mismatch, which falls back to the full probe. | **SAFE** (fail-safe direction). | Part of 32.5 s plus the uncounted `rt.ingest.prelude` count cost (50/min); UNOBSERVED split | Low | 2 |
| P2b | Ingest: keep the memo across a seed or reconcile when the seed proves the same prefix (seed already returns Count, LastSequence, LastEntryId) instead of clearing it at `SessionStateStore.cs:207` | Same verify rule (`:1520`) on next use | **SAFE** while verify stays. Skipping verify is NOT SAFE: a wrongly skipped UUID would DROP a UserPrompt row, which is a false delivery negative. | Most of the 3,829 non-memo ingests' extra chunks (~10 of 11.4 commands each); share of 32.5 s UNOBSERVED | Medium (CARD-1888 rule 5 exists on purpose) | 3 |
| P3 | Seed: cheaper SQL rather than a cache (whole-session `count(*)` and the activity aggregate per session, `SessionStateLoader.cs:52-75`), and longer retention for terminal sessions (`TerminalIdleMinutes`, an operator setting) | Entries are already the in-memory cache, and mostly first touch | Reads feed Working, so only proof-preserving changes. | ≤17% of 44.5 s from caching (≈7 s); the rest needs SQL work | Medium | 4 |
| P4 | Delivery watchdog stuck suspect (9f263c85, 3.5/min, 54 attach failures) | Not a cache: a back-off on an unchanged suspect, i.e. skip `CatchUp` and span while the session's transcript stamp and runner state are unchanged | NOT SAFE as a cache. A design question for the watchdog owner. | small DB; also stops log noise | — | note only |
| P5 | `SessionQueuedMessages EXISTS`, `AgentSessionEvents` | Need the statement text first. The Pending-row checks (`:1855,:6301`) gate TurnEnd and poll decisions: **NOT SAFE** to cache. | | ≤12.9 s / ≤12.1 s | — | blocked on text |
| P6 | Cheap high-rate untagged rows (AgentSessions boot columns 20.3/s, runner lookup 19.7/s, `SessionRuntimeActions` Phase update 8.6/s, `LastSeenAt` 7.3/s) | Tag first (`TagWith("rt.…")`). Runner binding columns look launch-stable but that is unverified. | UNOBSERVED | | | after tagging |

## Q5. Hit/miss counters

| Cache | Has counter? | Smallest addition |
|---|---|---|
| PromptCache | No | Add `"prompt-span."` to `DbCommandMetrics.Prefixes` (`DbCommandMetrics.cs:21`). The stamp, proof, tail and read tags then appear in `/api/diagnostics/session-state` `dbCommands`. Plus six `Interlocked` counters in `LoadPromptRowsAsync` (bypass, cold, hit, tail, full-after-proof, oversize-not-stored) and the existing `Snapshot()`, exposed beside `cache`. |
| Ingest prefix memo | Indirect | Counters: memo-null, digest-not-covering, verify-fail, covered. Optional `rt.ingest.chunk` tag on the later-chunk probe (`AgentSessionRuntime.cs:1535`) so chunks separate from the prelude. |
| SessionStateStore | Yes | Split `loads` by reason (first-touch, reconcile, mutation, warm, post-eviction): one enum arg on `LoadAsync`. |

## Recommended first slice (one)

**S1 = prompt-span observability (Q5 row 1).** Add the `prompt-span.` prefix to
`DbCommandMetrics.Prefixes`, plus six outcome counters and the entry/char snapshot on
`/api/diagnostics/session-state`. That is about 30 lines, no behaviour change, zero delivery risk. It
targets the single largest consumer (45.0 s/10 min) and decides whether P1's saving slice is oversize,
idle, or proof. Without it any P1 change is a guess. If the plan must save DB time in its first slice,
**P2a** is the smallest safe saving change: in-memory `SessionCount` under the lease, fail-safe on
mismatch.

Remaining uncertainties: no per-session attribution for any statement, and no statement text for the
EXISTS and `AgentSessionEvents` rows. The census was ~20 min after the window. The counter rates come
from one 31 s sample.

Not done, noted: one fix idea per item is in the P-table; nothing was implemented.

## Against the CARD-1987 plan (fa6c783e @ 7fc39b0f0)

**Where we agree:**
- Measure first (its M1-M3).
- The delivery-evidence guard (its §3).
- A second writer process is UNOBSERVED (its §1).
- The runner-binding lookup is launch-stable routing that can be cached (its S1 ≈ my P6).

**Where this note differs:**
1. **Ranking axis.** The plan ranks by calls/s. Its S1 and S2 (~25.6 qps) remove cheap round trips.
   The three statements that dominate **DB time** are absent from its slices: prompt-span.read
   45.0 s, seed 44.5 s and identity 32.5 s per 10 min, ≈122 s of the ~147 s named.
2. **Row 6 says "CARD-1888 owns it, re-measure".** The live counters say CARD-1888 does not cover it:
   ~3,829 non-memo ingests ran 43,802 identity commands (≈11.4 per ingest, about 5,800 stored UUIDs
   re-probed). Every seed or reconcile clears the memo (`SessionStateStore.cs:207`). P2a/P2b belong
   in this card or a CARD-1888 follow-up, not "n/a".
3. **M3 should add `prompt-span.` to `DbCommandMetrics.Prefixes` and the PromptCache outcome
   counters** (this note's S1). Otherwise the largest DB-time consumer stays unattributed after the
   plan's measurement slices.
