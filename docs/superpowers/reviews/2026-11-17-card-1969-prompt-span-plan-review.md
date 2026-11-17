# CARD-1969 slice 2 plan review, round 1 (Full)

Date: 2026-11-17. Review task 9dbbe5bf (Claude Opus; plan author Grok). Read-only review. No
production code, test or plan file was changed.

- Subject: `docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md` on
  `origin/feat/card-task-2048f2d0`, tip **241f89463977945e43d7e8414edd5221501ba531**
  (one commit over master `7901e9ba537b757740c2913aa2211d513b0b31f2`; docs only).
- Code read at `7901e9ba5`: `TranscriptPromptSpan.cs` (all), `TaskReportTurnSelector.cs:55-100`,
  `AgentSessionRuntime.cs:1460-1660, 1780-1810`, `AppDbContext.cs:1728-1760`, `DataRetentionService.cs:372`,
  `TranscriptEntry.cs`, every `TranscriptEntries` writer under `server/` and every
  `ExecuteUpdate` on `TranscriptEntries` under `tests/`.
- Also read: CARD-1969 (board), the attribution note (`origin/feat/card-task-c747c0c4`,
  section 3.3 and the statement-3 row), the slice 1 plan (`804edf5f`) and its review
  (`origin/feat/card-task-39f3b245` = `57d7a637e`), the CARD-1888 Code tip `cd222da3c`.

**Verdict: BLOCKERS (2).** B-1 and B-2 below. Everything else holds; advisories A-1..A-6 do not
hold the land.

## What holds

- **Writers (check 1).** Production writes to `TranscriptEntries` are: the three `Add` sites
  (`AgentSessionRuntime.cs:1513`, `:1612`, `:1648`), all with `Sequence` rebased past the stored
  max (`:1481`, `storedSeq = e.Sequence > maxSeq ? e.Sequence : maxSeq + 1`), new `Guid` ids
  (`:1486`), and a unique `(AgentSessionId, Sequence)` index (`AppDbContext.cs:1747-1749`);
  `ApplyPersistStub` (`:1803-1813`) edits the row before its insert, not a stored row; the
  whole-session `ExecuteDeleteAsync` (`DataRetentionService.cs:372`) and the session cascade
  (`ProjectCascade.cs`, `DataRetentionService.cs:290`). No `ExecuteUpdate`, raw
  `UPDATE`/`DELETE`/`INSERT`, or tracked edit of a stored transcript row exists under `server/`.
  CARD-1888 Code `cd222da3c` adds none (its server diff has no delete, update or sequence change).
  With those writers, the stamp `(Count, FirstId, MaxSequence, TopId)` changes for every change
  that can alter the prompt list: an append moves max and top id; a late commit at a lower
  sequence moves count; a delete moves count or an id; a same-sequence replace moves the top id
  (new `Guid`). A delete-plus-insert that keeps all four needs a single-row delete, which no
  production writer does. A pre-dispatch barrier and a lower-sequence null timestamp stay in the
  cached prefix, because the cache holds the whole unfiltered list; W-6 pins the
  null-timestamp case. `QueuedUserPrompt` changing kind in place has no production writer.
- **F-1 (in-place `Text`).** Unreachable in production today (above); the S3 re-grep stops the
  slice if one appears. Accept, with A-2.
- **F-3 / D-5.** Correct: a prompt-less `TurnEnd` and the grace clocks are not prompt rows
  (`AgentTaskDispatcher.cs:4861-4888`); the sweep must keep running every tick.
- **F-4 / D-6.** No migration is needed for correctness. A-1 corrects the cost claim.
- **F-2 / D-9.** The S2 red set is right: at S2 (max-only), W-4 and W-5 return the stale cached
  rows because the max is unchanged, and W-3 full-reads on a new max. W-1 and W-3 are red at base
  for the right reason (the second load re-reads `n` / `n+1` prompt rows; nothing remembers
  them). W-2 and W-6 are green guards. None is a stub: each counts reader rows of the prompt
  projection or asserts list content, so a constant or self-compare cannot pass.
  `SelectAsync`'s other reads (`TurnEnd` boundaries, sibling tools; `TaskReportTurnSelector.cs:83-94`)
  do not project `"Text"` and `"Timestamp"` together on that seed, so the W-1 `select` case
  counts only the span.
- **Checkpoints (check 5).** `### Checkpoints`, CP-1..CP-4, one build per `After` group, CP-4
  reuses CP-3. EstimatedMinutes 15/15/15/8 give row timeouts 45/45/45/24; `minExecuted`
  8/8/17/12. `C1969s2_*` = 3 + 5 = 8 cases; `TranscriptPromptSpanTests` has 9 methods and no
  `[Arguments]`; no filter is skip-bearing; each class prefix matches only its own class.
  CP-4's expected count is 13, not 12 (A-3); 13 still satisfies `minExecuted` 12.
- **Collisions (check 6).** CARD-1888 Code `cd222da3c` changes `AgentSessionRuntime.cs`,
  `AgentSessionRuntime.ReceiptReadiness.cs`, `PolicyRefreshService.cs`, `SessionStateStore.cs`,
  `TranscriptIngestPrefix.cs`, `TranscriptIngestPrelude.cs`, three test files and
  `tests/derived-impact-index.json`. Slice 1 (`804edf5f`) plans `AgentSessionRuntime.cs`,
  `TranscriptRecoveryRead.cs`, `WorkingTranscriptRecoveryTests.cs` and
  `docs/session-runtime-invariants.md`. Neither touches `TranscriptPromptSpan.cs` or
  `TranscriptPromptSpanTests.cs`. Master `57563753c` (moved since the brief) changes neither
  file, nor any caller listed in the plan. No collision.
- **Mechanics.** `git merge-tree --write-tree origin/master(57563753c) 241f89463` = tree
  `0af4cc43e`, no conflict. `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 7901e9ba5
  -HeadRef 241f89463`: commits=1 entries=0 violations=0, exit 0. Checkpoint import: see the
  Import section.

## Blockers

### B-1. The full-read store can pair a stamp with a different row set and hide a prompt

- **Where:** plan §3 "Full read" and "Tail" ("the stamp taken in that same snapshot", "a
  same-snapshot proof"), §3 "Transaction", S2/S3 rows of §4, §6 witness table.
- **Failure:** the plan requires one snapshot but never says how it is obtained. Under
  Postgres's default `READ COMMITTED` (Npgsql `BeginTransactionAsync()` default; no retrying
  strategy is configured), each statement has its own snapshot. If Code runs the full
  projection and then the stamp statement (or the S2 `max` read after the rows), an ingest batch
  that commits between them yields `rows` without the new prompt stored under a stamp that
  includes it. Every later call then **hits** and returns the list without that prompt until
  some other prompt is appended. Ingest commits continuously during a live turn and the sweep
  loads every open session every 5 s, so the window recurs. The hidden prompt can be the brief:
  the watchdog (`FailNeverStartedAsync`, `TurnPrompts.Count == 0` after 10 min) fails a task
  whose brief arrived, and the selector cannot choose the owner. The opposite order (stamp, then
  rows) is safe for hits but, on the tail path, can append a row already in the cached list.
  W-1..W-6 are all single-threaded, so every one stays green on the torn implementation.
- **Why it matters:** the brief's rule: a cheaper read must never hide a prompt. Slice 1 avoided
  this by construction (one statement returns stamp and tail together).
- **Fix:** amend §3 to make the stored stamp a function of the rows actually returned: add `Id`
  to the cached projection and derive `Count`/`FirstId`/`MaxSequence`/`TopId` from the full-read
  list (and from `cached ++ tail` after the tail checks), or read stamp and rows in one statement
  or one `REPEATABLE READ` transaction, stated explicitly. Add a witness W-7: an interceptor that,
  after the first prompt-span command of the first `LoadAsync` returns, inserts a prompt at
  `max+1` through a second context; the second `LoadAsync` must contain it. It must be red
  against an implementation that reads the stamp after the rows in separate statements (state
  that in the table and give it a CP-2 or S2b red if the S2 shape cannot show it).

### B-2. The process-wide cache has no concurrency contract and no memory bound

- **Where:** plan §3 "Cache shape", D-7, F-5, Performance budget.
- **Failure:** `TranscriptPromptSpan` is a `static` class; `LoadAsync` runs concurrently from
  the dispatcher tick (sweep, watchdog), settlement (`AgentTaskReplyService`), card settlement,
  API-error recovery and the late-failure forwarder, each on its own `DbContext`. The plan
  specifies an LRU with a cap but no synchronization; an unsynchronized `Dictionary` plus
  recency list corrupts or spins under concurrent writers, inside the dispatcher tick. It also
  does not say that an entry is one immutable `(stamp, rows)` object replaced atomically (an
  older full read overwriting a newer entry is safe only then).
  Memory: the cap counts sessions, not bytes, and entries persist after their task closes, so the
  steady state is the last 256 sessions loaded. Prompt lists grow with session lifetime (971 rows
  per call now, 1,503 in the 44 h sample; attribution 3.3), prompt texts include briefs and
  task reports of up to 14,400 characters, and `ApiErrorRecoveryService` / `CardTaskSettlement`
  can load long standing sessions. Today the rows are transient; after this slice they are
  retained. **UNOBSERVED:** the average prompt `Text` length and the per-session prompt byte
  total. An illustrative, unmeasured envelope: 1,000 prompts x 4,000 chars x 2 bytes = 8 MB per
  session, about 2 GB at 256 entries. The plan states no figure and no bound.
- **Why it matters:** a server-wide memory regression and a possible tick hang, neither of
  which any witness or CP row would show.
- **Fix:** amend §3/D-7: (a) all cache reads and writes under one lock (or a concurrent
  structure with documented eviction), entries immutable and replaced whole; (b) a retained-size
  bound in characters (sum of cached `Text` lengths) with LRU eviction by size, and either skip
  caching a session above a per-entry ceiling or evict when its last open task settles; label the
  chosen numbers and the expected footprint UNOBSERVED and add the measurement to §8 (process
  working set before/after, M-6). Add a witness that a session larger than the per-entry ceiling
  (or past the total bound) still returns today's full list (eviction fails closed).

## Advisories (do not hold the land; carry to cards if not folded into the amendment)

- **A-1. Stamp cost on Postgres 16.** `IX_TranscriptEntries_AgentSessionId_Kind_Sequence` is
  `(AgentSessionId, Kind, Sequence)`. With `Kind IN ('UserPrompt','QueuedUserPrompt')` the
  index cannot return rows in `Sequence` order (an array predicate on a non-leading column), so
  `ORDER BY "Sequence" ASC/DESC LIMIT 1` for the two ids scans every prompt entry of the session
  and heap-fetches each one for `Id`, not "two single-row heap fetches" (§3, §8). Not verified by
  EXPLAIN here. Write the ids as one `Kind = '…' ORDER BY "Sequence" DESC LIMIT 1` subquery per
  kind and take the greater/lesser, which is a true one-row backward index scan each. `count(*)`
  remains O(prompt index entries) with heap visits for pages not all-visible.
- **A-2. D-4 and D-8 name only `Text`.** An in-place `Kind` change (`UserPrompt` to
  `QueuedUserPrompt`) or `Timestamp` change is equally invisible to the stamp and leaves a stale
  `Kind` in `RawPrompts`/`TurnPrompts` (the watchdog's queued-only branch, `AgentTaskDispatcher.cs:2511-2520`, reads it). Tests do change `Kind`
  in place (`TerminalRunnerSeatReleaseTests.cs:298`, `PermissionWaitDeliveryTests.cs:145`,
  `C0824RefOnlyReceiptTests.cs:147`) and `Text` (`PreDispatchFailureDeliveryTests.cs:352`,
  `TaskInputWeakArmBoundaryTests.cs:97`, `C0824RefOnlyReceiptTests.cs:146`). Widen D-4/D-8 and
  the S3 re-grep to any in-place column change; Code should list which of these classes reach
  the span between the edit and a later load, and run them (they are not in the CP table).
- **A-3. CP-4 count.** `TaskReportHousekeepingTests.C714_grok_shape_on_other_provider_is_real_prompt`
  has two `[Arguments]` cases (`TaskReportHousekeepingTests.cs:87-90`), so CP-4 executes 13
  (2 + 11), not 12; §4 "No `[Arguments]` on those 12" is wrong. `minExecuted` 12 still passes.
- **A-4. W-4 wording.** `PromptRow` has no `Id`; "lacks that id" must be by sequence or text
  unless B-1's fix adds `Id` to the cached row.
- **A-5. Provenance line.** Plan line 10 says the slice 1 review ref `39f3b245` is at `804edf5f`;
  it is at `57d7a637e` (the clean review commit).
- **A-6. Owner doc.** A process-local cache over the delivery-evidence read with an accepted
  in-place-edit hole belongs in `docs/session-runtime-invariants.md` (slice 1 records its own
  stamp hole there in S3). Add one paragraph in S3. Also key the cache by database identity as
  well as session id, so in-process hosts on different databases cannot share entries.

## Decisions F-1..F-5

| Decision | Review |
|---|---|
| F-1 accept the in-place `Text` hole | Yes, widened per A-2 |
| F-2 incomplete S2, S2+S3 one Merge | Yes |
| F-3 sweep every tick | Yes |
| F-4 no index, no summary | Yes; fix the cost claim (A-1) |
| F-5 256 sessions, LRU, no setting | **Not as written**: needs B-2's synchronization and byte bound |

## Import

Read-only import at `241f89463` in this worktree (`ANTIPHON_LINUX_COMPILER_POLICY=0`, `TEMP=/tmp`, load 14.86, slot lease waited 0 s, held 8 s):
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1969-s2-review-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md --out .antiphon/c1969-s2-review-import.yaml`
Exit 0, `imported 4 rows`. Builds `bin-c1969-s2-red`, `bin-c1969-s2-stamp`, `bin-c1969-s2-green` (CP-4 reuses green); `minExecuted` 8/8/17/12; `timeoutMinutes` 45/45/45/24; filters verbatim. Matches the plan.

## Tests

None run. The subject changes no code and no test (one plan file), so the touched-class and
guard batch reruns do not apply (brief: only when the subject changes code or tests). All PCs
stay pending.
