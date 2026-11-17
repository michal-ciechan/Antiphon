# CARD-1969 slice 1 plan: the stale-Working probe must not re-read a whole session

Date: 2026-11-17. Plan task 8626ac28 (Opus). Plan only: no code, no setting change, no restart and no SQL
against any production database.

- Source read: `origin/master` **7901e9ba537b757740c2913aa2211d513b0b31f2** (also this branch's base).
  The attribution note (c747c0c4, commit `c684172fc9ba93b161b87b73faf006599ca34ef4`) shows that the running
  desktop server (`870a93d21`, `rc/2026-11-24`) has the same bytes for every file cited here.
- Inputs: card CARD-1969; `docs/investigations/2026-11-17-transcript-read-attribution.md` §3-§6 (on
  `origin/feat/card-task-c747c0c4`); the CARD-1888 plan
  (`docs/superpowers/plans/2026-11-17-card-1888-transcript-ingest-probe-plan.md` on master) and its Code
  branch `origin/feat/card-task-6ea96bcb` (tip `2bc851b5c`, not landed at 7901e9ba5);
  `docs/session-runtime-invariants.md` §Stale Working transcript recovery (CARD-1205 S4).
- The orchestrator's M-1 measurement (desktop log `antiphon-20261117.log`) is quoted from the brief. It was
  not re-measured here.

## 1. Problem as measured

`StaleWorkingTranscriptRecovery` probes each tracked local Running session whose transcript reads as Working.
Each probe calls `AgentSessionRuntime.ProbeWorkingTranscriptAsync` (`AgentSessionRuntime.cs:1046-1152`). That
method reads the **whole** session transcript (all 25 columns, `RecoveryRow`, `:1155-1159`) through
`ReadRecoveryObservationAsync` (`:1176-1188`), from one to four times:

| Read | Line | Under | Used for |
|---|---|---|---|
| `before` | `:1062` | state lease 1 | stored max (`Rows.Last().Sequence`), the frontier row (`LastOrDefault(RecoveryActivity)`, `:1082`), and the baseline for the whole-array equality |
| `current` | `:1095` | state lease 2 | `current.Rows.SequenceEqual(before.Rows)` (`:1102`) |
| `fenced` | `:1534` | lease 2 plus the `AgentSessions` row lock, inside the recovery transaction, **`CancellationToken.None`** | `fenced.Rows.SequenceEqual(expectedRecovery.Rows)` (`:1540`) |
| `after` | `:1124` | lease 2, after publication | the `Id` set for `AddedCount` and the last `Sequence` for `PostStoredSequence`. Both feed only the result log line (`StaleWorkingTranscriptRecovery.cs:224-226`). A grep finds no other consumer of `AddedCount`/`PostStoredSequence` in `server/`. |

M-1 (brief): 294 of 327 probes in the log are for orchestrator session `b2f1a869` (412,289 rows). Every
one of them read the whole session in 8.4-9.2 s, with `pre == post` and `added 0`. The other sessions are 57-82
rows, 10-13 ms. The attribution note measured statement 1 at 0.154 core, 28.6 % of Postgres statement time.
That is a lower bound: parallel workers and cancelled reads are not counted (note §4).

## 2. Why `unknown_freshness` (question a)

**What the label is.** `unknown_freshness` is not a property of the session or of the transcript. It is the
*due* reason that `Admit` prints when the in-memory directory entry has `LastSuccess == null`
(`StaleWorkingTranscriptRecovery.cs:181-182`). `interval_elapsed` is printed otherwise. No code computes a
freshness fact from the transcript. `Due()` (`:160`) is true whenever `LastSuccess` is null, whatever the
transcript's age.

**How `LastSuccess` becomes or stays null.** It is a field on a process-local `Entry`:

1. **A new entry.** `Track` (`:134-148`) creates `new Entry(...)` with `LastSuccess = null` when a session is
   first discovered. It does the same after `Remove`, which `ReconcileAsync` calls (`:127-128`) as soon as one
   pass sees the session as not busy (`TranscriptWorkingStateQuery`), not local Running, or gone. It does the
   same on a generation change (`:138-139`) and on every server restart (a new `_epoch`, an empty directory).
   So the fact is **lost every time the session goes idle between turns**. The next Working turn is re-tracked
   with no history and is probed on the next pass.
2. **An Unknown outcome.** `ProbeAsync` sets `LastSuccess` only when the outcome is not Unknown (`:216`). Every
   Unknown result therefore leaves it null, and the entry is re-admitted on every pass, no sooner than
   `PollSeconds` (15 s) after the last attempt (`:170`). This is by design: "Unknown retains debt and retries no
   faster than the poll" (T24, `session-runtime-invariants.md`).

Nothing else invalidates it. Keepalives, cache reads, queue attempts and another session's activity are
explicitly *not* allowed to reset the age (T20, `C1205_Healthy_keepalives_do_not_reset_probe_debt`).

**Which of the two drives b2f1a869: UNOBSERVED.** The brief gives the due reasons (290/37) but not the
`Stale Working probe result ... reason {Reason}, outcome {Outcome}` values for b2f1a869. Two hypotheses fit,
and the same log separates them at no cost (M-7, §10):

| Hypothesis | Expected result lines | Fit |
|---|---|---|
| **H-churn**: each orchestrator turn is a new entry. The probe returns `CheckedWorking`/`runner_working` (2 whole reads), the turn ends, reconcile removes the entry, and the next turn re-tracks it. | outcome `CheckedWorking`, reason `runner_working`, no `probe unknown` warning for b2f1a869; `interval_elapsed` only for turns over 120 s | about 30 probes an hour is a plausible orchestrator turn rate; 2 reads × about 4.3 s ≈ 8.4-9.2 s; `added 0`, and `pre == post` because nothing is persisted on this path |
| **H-unknown**: the probe ends Unknown (for example `committed_working`, `publication_working`, `frontier_changed` or `persist_uncertain`), so `LastSuccess` stays null and the entry is re-probed every pass | outcome `Unknown` and one `Stale Working probe unknown ... evidence debt retained` warning per probe | `pre == post` and `added 0` also fit. But retrying every pass would give about 140-240 probes an hour while Working, not 30. The 4-read paths at 5.2 s mean would also exceed the 8.4-9.2 s durations. A weaker fit. |

**Would fixing the freshness fact remove most probes?** Under H-churn, yes. A gate of "probe only when the
session's newest runner-sourced activity row is older than N seconds" skips every probe of a session that is
actively streaming. That covers nearly all 294. Under H-unknown, the gate also skips while the session streams,
but an idle-looking Unknown loop would remain. In both cases the gate **changes CARD-1205 S4 semantics**:

- `Due()` today ignores activity (T18-T20).
- `C1205_Hosted_restart_discovers_working_sessions_immediately` binds "cold epoch is due without advancing the
  interval".
- The gate must count only runner-sourced activity, never server annotations such as queue rows. T20 exists
  because a stuck Working session keeps receiving queue annotations.

So the gate is **not in this slice** (D-3, F-3). This slice makes every probe cheap whatever its cadence or
outcome. That is the fix that holds under both hypotheses.

## 3. Ground truth: brief/card vs code

| Brief or card assumes | Code at 7901e9ba5 | Consequence |
|---|---|---|
| Every probe reads the whole session | Yes. 1 to 4 whole reads per probe, depending on the outcome (note §3.1 table) | The fix must cover all four reads |
| `unknown_freshness` is a freshness fact computed from the session | It is the due label for `LastSuccess == null` (`:182`). No freshness is computed. It is lost on idle → re-track and kept null by Unknown | §2. The scheduling fix is a separate CARD-1205 semantics change |
| The final read `:1124` feeds only a log line | It feeds `TranscriptRecoveryResult.AddedCount`/`PostStoredSequence`. The only server consumer is the result log line. Tests assert `AddedCount` | `after` becomes a stamp-only read (D-6). `AddedCount` stays exact for rebased inserts |
| `:1102`/`:1540` compare two whole arrays | Yes. `RecoveryRow` record equality over all 25 columns of every row. It also catches in-place updates anywhere | The replacement detects in-place changes only inside the K-row tail. Below the tail they are a documented hole (D-4, F-1) |
| `fenced` runs under the session row lock | Yes (`:1528` lock, `:1534` read). It is also **not cancellable**: `CancellationToken.None`, outside the 10 s probe budget | A bounded read also bounds the time the lock is held |
| A stamp of `MAX(Sequence)` + the newest row's id is enough | No. A middle delete leaves both unchanged (CARD-1888 round-1 D-1). The count is needed. An in-place update is invisible to any count or id stamp | Stamp = count + first row id + max sequence + top row id, and value equality of the K-row tail (D-2) |
| The CARD-1888 memo type can be reused | Its `IngestPrefix` lives on `SessionStateStore.Entry` across batches, is cleared on load and mutation, and answers "is the replayed prefix still stored". This probe compares two observations a few seconds apart **within one probe** and needs no cross-probe memory | Reuse the check shape (count, first row, top row), the equivalence argument and the S3 re-grep gate. Do not share the type (D-5) |
| A covering index or summary table might be needed | No. The unique `IX_TranscriptEntries_AgentSessionId_Sequence` `(AgentSessionId, Sequence)` already serves the count (index-only), the first and top seeks, and the ordered tail | No migration and no human gate |
| All inserts are appends | Every production insert goes above the prelude's stored max (`:1481`, `storedSeq = e.Sequence > maxSeq ? e.Sequence : maxSeq + 1`). The only deletes are whole-session (`DataRetentionService.cs:372`, cascade). No `UPDATE` path exists (CARD-1888 §2 grep; re-checked here: the only `TranscriptEntries.Add` calls are `:1513`, `:1612`, `:1648`, all with rows from the same rebased batch) | The basis of the equivalence argument in §4. Re-grep gate in S3 |
| Statement 1 costs 0.154 core | That is a lower bound (note §4: parallel workers at 72-75 % each are not attributed) | Savings are a range (§10) |

## 4. Design (question b)

### Chosen: a per-observation stamp plus a bounded tail, read in one statement

Replace `RecoveryObservation.Rows` (the whole session) with:

```
RecoveryObservation(Generation, Status, RunnerId, RunnerStoreId, RunnerCwd,   // session row, as today
                    RecoveryStamp Stamp, RecoveryRow[] Tail, bool Full)
RecoveryStamp(long Count, long? MaxSequence, Guid? FirstRowId, Guid? TopRowId)
```

- **One statement per observation**, tagged `-- rt.recovery.observe`. It is raw SQL in a new
  `server/Infrastructure/Data/TranscriptRecoveryRead.cs`, following the `SqlQueryRaw` + `TagWith` pattern of
  `TranscriptIngestPrelude.cs:76-77`. The statement returns:
  - the stamp: `count(*)` over the session (index-only on the unique `(AgentSessionId, Sequence)` index), the
    `Id` of the lowest-`Sequence` row (one seek), and `max(Sequence)` with the top row's `Id` (one backward
    seek);
  - the newest `K` rows (`ORDER BY "Sequence" DESC LIMIT @k`, all 25 `RecoveryRow` columns), returned in
    ascending order.

  One statement is one MVCC snapshot. `TopRowId` therefore equals the tail's last row by construction, and a
  write between the stamp and the tail cannot happen. The session-row read stays the separate EF query it is
  today, so the consistency of the session fields is unchanged.
- **Frontier.** `frontier = Tail.LastOrDefault(RecoveryActivity)`. `RecoveryActivity` is a per-row predicate,
  so when the tail holds an activity row this is the same row that today's whole-array scan returns.
  `RecoveryIdentityMatches` is unchanged: the tail row carries all 24 data columns.
- **Fail-closed fallback.** If the tail holds no activity row and `Count > K`, `before` is re-read in today's
  whole-session form (`Full = true`). `current` and `fenced` then also read whole, and the comparison is
  today's `SequenceEqual`. The whole path is then byte-for-byte today's behaviour (W-7).
- **Change detection** (`:1102` and `:1540`), when `before.Full` is false:
  `Changed(a, b) = a.Stamp != b.Stamp || !a.Tail.SequenceEqual(b.Tail)`.
  Record equality covers Count, MaxSequence, FirstRowId and TopRowId, plus every column of every tail row. The
  lease `ServerEpoch`/`Revision`/`ResetEpoch` check at `:1101-1102` and the ownership checks stay as they are.
- **`after`** (`:1124`) becomes a stamp-only read with `count(*) FILTER (WHERE "Sequence" > @beforeMax)` as
  `AddedCount`. `PostStoredSequence` = `MaxSequence`. Ownership and generation still come from the session row.
  Under rebased inserts this equals today's "ids not in `before`" (D-6).
- **`Result()`** uses `before.Stamp.MaxSequence ?? 0` instead of `before.Rows.LastOrDefault()?.Sequence ?? 0`.
  The value is the same.
- **Unchanged:** the runner pull, generation, session and snapshot checks, `runnerState` classification,
  `PersistTranscriptCoreAsync`'s ingest (CARD-1888's territory), publication, `IsWorkingAsync`, the scheduler
  and every setting.

### Why the stamp cannot miss a change (the property W-3 to W-6 pin)

Row `Id`s are client `Guid.NewGuid()` (`:1486`), never reused, and no production path updates a row.
Between two observations a few seconds apart:

1. **Append by any production writer** (always above the stored max, `:1481`): `MaxSequence` and `TopRowId`
   change. *Observed.*
2. **Whole-session delete** (retention `:372`, `PruneSessionsAsync` cascade), with or without a refill:
   `Count`, `FirstRowId` and `TopRowId` change. *Observed* (existing `replace-frontier` variant).
3. **Delete of one middle row** (no production writer; another server or an operator could do it): `Count`
   drops, while `FirstRowId`, `MaxSequence` and `TopRowId` stay. *Observed by the count only* (W-3, W-4).
   A compensating rebased insert moves `MaxSequence`/`TopRowId`. *Observed.*
4. **Top row deleted and refilled at its `Sequence`** (a rebased insert after the top delete lands at the
   freed S): the count and max are equal and `TopRowId` differs. *Observed* (W-6).
5. **In-place `UPDATE`, or a delete plus insert at the same `Sequence ≤ max` by a writer that does not
   rebase:** *observed inside the K-row tail* by value equality (W-5). **Below the tail it is not observed.**
   No production statement does either (§3). This is the same hole CARD-1888 recorded and its Review accepted
   (F-1 there). S3 repeats the CARD-1888 re-grep gate: any `ExecuteUpdate`, raw `UPDATE`/`DELETE`/`INSERT` on
   `TranscriptEntries`, or an insert whose `Sequence` is not rebased above the stored max, stops the slice
   (report blocked).

**Delivery evidence.** The probe decides nothing about delivery from absence. The stamp and tail are read
fresh at each of `before`, `current` and `fenced`, in one snapshot each. The only durable effect, the persist
of the runner snapshot, still happens under the fenced comparison with the session row locked. A transcript
whose newest K rows hold no activity falls back to today's read. Transcript-confirmed `UserPrompt` evidence is
never inferred from a prefix: the probe never treats a row as present or absent below the tail. It only
compares the count and the boundary ids.

### Rejected alternatives

| Alternative | Why rejected |
|---|---|
| `MAX(Sequence)` + top row id only (the brief's first sketch) | Blind to a middle delete (CARD-1888 round-1 D-1). Kept as the deliberately incomplete S2 so that W-3/W-4/W-5 have a committed red (D-9). |
| SQL digest over all rows (`md5(string_agg(...))` or `xmin`) | Still O(rows) heap and TOAST work in Postgres, which is the CPU we are removing. A digest over `Text` costs more than today's read. |
| Cross-probe memo ("stamp unchanged since the last probe → skip the probe") | A stale session is exactly a session whose transcript does not change. The probe must still ask the runner. A memo would suppress the recovery CARD-1205 exists for. |
| Reuse CARD-1888's `IngestPrefix` on `SessionStateStore.Entry` | Different question and lifetime (§3). It would couple this slice to a type that is not landed yet. |
| CARD-1915 durable summary as the stamp | Flag off; activation is a separate commissioning, and CARD-1933 says EF batch appends invalidate it. It would still not see in-place changes. A later option for the remaining count cost (§10). |
| A covering index or trigger-maintained revision counter | A migration is a HUMAN-ONLY gate, and it is not needed: the existing unique index serves every part of the statement. |
| Gate the probe on runner activity age (question a) | Changes CARD-1205 S4 due semantics (T18-T20, T24, T26/T30). Separate card after M-7 (F-3). |

## 5. The second uncovered read (question c)

`TranscriptPromptSpan.LoadAsync` (`TranscriptPromptSpan.cs:88-94`), reached from
`AgentTaskDispatcher.SettleDeferredReportsAsync` (`:4890-4940`) on every 5 s dispatcher tick per open task, is
**not in this slice**. It is a delivery-evidence read: the prompt span decides which turn is the task's report
turn, and the CARD-1969 card requires its own plan, witnesses and a Review round focused on the delivery
invariants. Its measured cost is 0.070 core (12.9 %, note §2). It becomes **CARD-1969 slice 2: its own plan,
its own Review round**. The two slices share no file: slice 2 touches `TranscriptPromptSpan.cs`,
`TaskReportTurnSelector.cs` and possibly `AgentTaskDispatcher.cs`, none of which this slice touches.

## 6. Slices

| Slice | Files | Content |
|---|---|---|
| S1 (red tests, no production change) | `tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs` | W-1..W-7 (§8), a private nested `FullRowReadCounter : DbCommandInterceptor`, and a `SeedAsync(n, …)` helper on the private `Harness`. The counter wraps each reader whose command text reads `"TranscriptEntries"` and projects `"ApiErrorTimeZoneId"` (the whole-row recovery projection, at base and after; no other read on these paths selects it), and counts rows read and commands. `Reset()` runs just before `ProbeAsync`, and the values are captured as soon as it returns, before the test's own `RowsAsync`. Nested, so `TestHelpers/` and `tests/derived-impact-index.json` stay untouched (D-8). |
| S2 (stamp without the count or tail equality: **deliberately incomplete, never landed alone**) | `server/Application/Services/AgentSessionRuntime.cs`, new `server/Infrastructure/Data/TranscriptRecoveryRead.cs` | The one-statement read with `K` tail rows and a stamp of `MaxSequence` + `TopRowId` only. Frontier from the tail and the full fallback. `Changed` = `MaxSequence`/`TopRowId` differ. `after` stamp-only. W-1, W-2 green. W-3, W-4 and W-5 **red**: a middle delete or tail edit passes. |
| S3 (count + first row + tail equality) | same two files; `docs/session-runtime-invariants.md` (CARD-1205 S4 section: one paragraph on the stamp/tail observation and the documented hole) | `Count` and `FirstRowId` in the stamp, tail value equality, and the S3 re-grep gate (§4 point 5). All W green; every existing C1205 test green. |

### Must NOT touch

- `server/Application/Services/StaleWorkingTranscriptRecovery.cs`, `StaleWorkingTranscriptSettings.cs`,
  `StaleWorkingTranscriptHostedService.cs`, `server/appsettings*.json`: scheduling and settings stay as they are (D-3).
- `TranscriptIngestPrelude.cs`, `TranscriptIngestPrefix.cs` (new in CARD-1888), `SessionStateStore.cs`, and the
  ingest part of `PersistTranscriptCoreAsync` (`:1409-1527`, `:1542` onward): CARD-1888's territory. This slice
  edits only the fence block `:1532-1541` and the `expectedRecovery` parameter's type.
- `TranscriptPromptSpan.cs`, `TaskReportTurnSelector.cs`, `AgentTaskDispatcher.cs`: slice 2.
- `SessionStateLoader.cs`, `SessionStateSnapshot*`, the CARD-1915 summary/triggers, `DataRetentionService.cs`,
  `PhoneHomeRecoveryPump.cs` (CARD-1964), the workspace-slot files (CARD-1960).
- `server/Migrations/**`: no migration and no index.
- The selector/index chain (`scripts/impacted-classes.ps1`, `tests/impacted-classes.json`,
  `tests/derived-impact-index.json`).

### Existing tests affected

- **Migrations, public DTOs and routes, remote script functions:** none. `TranscriptRecoveryResult` keeps its
  shape. `AddedCount` stays exact for rebased inserts (D-6).
- **Operation labels / statement budgets:** one new SQL tag, `rt.recovery.observe`. No `QueryBudget` or
  statement-budget test covers the probe (`grep QueryBudget` in both C1205 classes: 0). The CARD-1403
  prelude budget is not touched.
- **Behaviour guards that must stay green unchanged:** `WorkingTranscriptRecoveryTests` (13 methods, 54
  cases: T10-T16 including `C1205_Concurrent_commit_invalidates_snapshot` with `new-prompt`,
  `queue-annotation` and `replace-frontier`, and `C1205_Late_precondition_change_rejects_without_publication`
  `frontier`); `StaleWorkingTranscriptWatchdogTests` (20 methods, 53 cases); `StandingResumeReceiptReadinessTests`
  (6; shares the fenced transaction block); `AgentSessionRuntimeTests` (48 at 7901e9ba5, 54 once CARD-1888
  lands).
- **Impacted-classes table (CARD-1782):** `scripts/impacted-classes.ps1 -Path
  server/Application/Services/AgentSessionRuntime.cs,server/Infrastructure/Data/TranscriptRecoveryRead.cs,tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs -Json`
  at 7901e9ba5 returned `kinds: []`, `classes: []`. No row matches. The Merge fixed subset still applies.

## 7. Decisions (defaults the plan was written under)

- **D-1:** stamp plus bounded tail per observation, in one statement (§4). Reason: it removes the O(rows)
  materialisation and TOAST reads, keeps the frontier and whole-revision checks, and needs no migration.
- **D-2:** the stamp is `(Count, MaxSequence, FirstRowId, TopRowId)` plus tail value equality. Reason: the
  count is the only cheap check that sees a middle delete. The tail catches in-place changes where activity
  happens.
- **D-3:** the scheduler's due rule is unchanged in this slice. Reason: changing it changes CARD-1205 S4
  semantics and its T18-T35 witnesses; F-3 proposes the follow-up after M-7.
- **D-4:** in-place updates and non-rebasing same-`Sequence` replacements below the tail are a documented
  hole, guarded by the S3 re-grep. Reason: no production writer does either, and detecting them needs an
  O(rows) heap read or a migration. Same as CARD-1888 (F-1).
- **D-5:** no type is shared with CARD-1888's memo. Only the check shape and the re-grep gate are reused.
  Reason: different lifetime and question (§3), and that type is not landed.
- **D-6:** `after` is stamp-only; `AddedCount` = rows with `Sequence > before.MaxSequence`. Reason:
  telemetry only. It equals today's id difference for every rebased insert. The one difference (top row
  deleted and refilled at S during the probe) counts the refill as 0 instead of 1, in a log field.
- **D-7:** `K = 64` (a constant `TranscriptRecoveryRead.TailRows`, not a setting). Reason: the frontier is the
  newest activity row, and a run of 64 server-only rows at the tail is the fallback case, not the common
  case. 64 full rows bound the bytes per read. W-7 pins the fallback.
- **D-8:** the counter is a private nested class in `WorkingTranscriptRecoveryTests`. Reason: no
  `TestHelpers` file means no derived-impact-index regeneration (held by CARD-1958).
- **D-9:** S2 is committed deliberately incomplete so that W-3, W-4 and W-5 have a committed red (mutation
  checks waived, operator 2026-11-12). S2 and S3 land together in one Merge, never separately, and an rc port
  carries them the same way.
- **D-10:** no new metric or route. Verification after deploy uses `pg_stat_statements` and the existing
  result log line (§10).

## 8. Verification design (question d)

Witnesses are new methods in `WorkingTranscriptRecoveryTests`, on its `Harness` (shared `TestDbFixture`
Postgres through `SessionStateTestFixture`; isolated schema per fixture). `SeedAsync(n)` persists one batch of
`n` runner events through `Runtime.PersistTranscriptAsync`: a `UserPrompt` at index 0 and every 50th index,
alternating `AssistantText`/`ToolCall` otherwise, and distinct UUIDs `c1969-{i}`. The runner snapshot is set to
the same events. It ends Working (a final `ToolCall`, no `TurnEnd`) for the CheckedWorking path, or with an
appended `TurnEnd` for the idle path. Deletes through a second context (`F.Db()`) remove the row's
`SessionRuntimeActions` first (`Restrict` FK, as in `replace-frontier`).

| ID | Test (cases) | Assertion | At base 7901e9ba5 | Why, from the code |
|---|---|---|---|---|
| W-1 | `C1969_unchanged_working_probe_reads_a_bounded_tail` (`[Arguments(300,true)]`, `(1200,true)`, `(300,false)`, `(1200,false)`; n, cache) | Outcome `CheckedWorking`/`runner_working`. Whole-row rows read ≤ `2 * 64` = 128, and whole-row commands ≤ 2. | **Red**: 600 / 2,400 rows (2 × n) > 128. The command assertion is green at base too (2): it is a guard against a per-row loop. | `:1062` and `:1095` each materialise every row (`:1182-1187`). |
| W-2 | `C1969_unchanged_idle_probe_reads_a_bounded_tail` (`(300)`, `(1200)`; cache true) | Outcome `RecoveredIdle`. Whole-row rows read ≤ `3 * 64 + 1` = 193 (`before`, `current`, `fenced` tails; `after` reads no rows), and whole-row commands ≤ 3. Exactly one `TurnEnd` row is added. | **Red**: 4n + 1 rows (four whole reads; `after` also holds the new `TurnEnd`) > 193. Red on the commands too (4 > 3). | `:1062`, `:1095`, `:1534`, `:1124`. |
| W-3 | `C1969_middle_row_deleted_between_reads_is_observed` (`(true)`, `(false)`) | n = 300, idle snapshot. While the runner pull is held (`Handler.Hold`), delete the stored `UserPrompt` at index 150 through `F.Db()`. Release. Outcome `Unknown`, reason `frontier_changed`, `AddedCount` 0, no `TurnEnd` row stored, 299 rows. | Green (whole-array equality). **Red at S2** (CP-2): the max and top row are unchanged, so the probe persists and returns `RecoveredIdle`. | `:1102` today. At S2 only the count would see it. |
| W-4 | `C1969_middle_row_deleted_at_the_fence_is_rejected` (1) | n = 300, idle, cache true, `Faults.HoldIdentity`. When `Reached`, delete the middle `UserPrompt` and release. Outcome `Unknown`, reason `frontier_changed_at_commit`, no `TurnEnd` row, store snapshot unchanged. | Green. **Red at S2**: the fence passes, and `TurnEnd` is committed. | `:1540` today. The identity probe runs before the fence (pattern of `C1205_Late_precondition_change_rejects_without_publication` `frontier`). |
| W-5 | `C1969_tail_row_changed_in_place_is_observed` (`(true)`, `(false)`) | n = 300. While the pull is held, `ExecuteUpdate` the `Text` of the row at max − 2 through `F.Db()`. Outcome `Unknown`/`frontier_changed`, no `TurnEnd`. | Green. **Red at S2**: no tail equality, so it persists. | Record equality today; tail equality at S3. |
| W-6 | `C1969_top_row_replaced_at_its_sequence_is_observed` (`(true)`, `(false)`) | n = 300. While held, delete the top row and insert a new row (new `Id`, same `Sequence`, `Kind` `AssistantText`) through `F.Db()`. Outcome `Unknown`/`frontier_changed`. | Green. Green at S2 too (`TopRowId`). A guard: only `TopRowId` sees it. | Count and max are unchanged. |
| W-7 | `C1969_tail_without_activity_falls_back_to_the_whole_read` (1; cache true) | n = 300, then 72 server-only `QueueEnqueue` annotation rows (pattern of the `annotation` variant, `:253-254`) persisted after the seed, so the newest 64 rows hold no activity. Idle snapshot without the annotations. Outcome `RecoveredIdle` (as at base), and whole-row rows read ≥ 372 (the fallback read the whole session). | Green (always whole). Green after: the fallback is today's read. | Red if Code wrongly returns `activity_frontier_absent`, or uses a frontier outside the tail without the fallback. |

Red proof on paper: at base, nothing bounds `ReadRecoveryObservationAsync`. The counter reads the actual reader
rows of the whole-row projection, so W-1 and W-2 can turn green only if fewer rows are materialised. A constant
or a self-compare cannot satisfy both the outcome and the bound. W-3, W-4 and W-5 are green at base because the
whole-array equality sees the change. S2 omits exactly the checks that see it, so CP-2 proves they are not
stubs.

### Checkpoints

Lane: Linux, server2 (the runner defaults name `server2` globally; omit `-Runner` and `-Platform`). Export
`ANTIPHON_LINUX_COMPILER_POLICY=0` before every dotnet command. One isolated build per `After` group. Each
`After` group is ONE `--serial` run without `--row-timeout`: CP-1 at the S1 commit, CP-2 at the S2 commit, and
CP-3..CP-5 together at the tip. The tool takes one run per committed slice group. Red rows are run once at
their slice and are not re-run after green.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1969-red/` | c1969-red | `/*/*/WorkingTranscriptRecoveryTests/C1969_*` | W-1..W-7 red proof | exactly 12 executed; W-1 (4) and W-2 (2) red on the rows bound (600/2,400 > 128; 1,201/4,801 > 193); W-3..W-7 (6) green | 12 | 15 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1969-stamp/` | c1969-stamp | `/*/*/WorkingTranscriptRecoveryTests/C1969_*` | W-3..W-5 red proof | exactly 12 executed; W-3 (2) and W-5 (2) red on outcome (`RecoveredIdle` vs `Unknown`), W-4 (1) red on the committed `TurnEnd`; W-1, W-2, W-6, W-7 (7) green | 12 | 15 |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1969-green/` | c1969-green | `/*/*/WorkingTranscriptRecoveryTests/*` | W-1..W-7 and every C1205 recovery guard | 66 executed (54 + 12), 0 failed/skipped | 66 | 15 |
| CP-4 | all | CP-3 | fence-callers | `/*/*/(AgentSessionRuntimeTests*)\|(StandingResumeReceiptReadinessTests*)/*` | shared persist/fence block | ≥ 54 executed (`AgentSessionRuntimeTests` 48, 54 once CARD-1888 is in the base; `StandingResumeReceiptReadinessTests` 6), 0 failed | 54 | 8 |
| CP-5 | all | CP-3 | stale-working | `/*/*/StaleWorkingTranscriptWatchdogTests/*` | scheduler, hosted driver, T18-T35 | 53 executed, 0 failed/skipped | 53 | 10 |

Suffix-collision check at 7901e9ba5 (`grep -rhoE "class <Name>[A-Za-z0-9_]*" tests`): `AgentSessionRuntimeTests`
(3 partial files, one class), `StandingResumeReceiptReadinessTests` (2 partial files, one class),
`WorkingTranscriptRecoveryTests` and `StaleWorkingTranscriptWatchdogTests` each match only themselves.
`C1969_` is new (0 hits). Every OR operand has parentheses and a trailing `*`. The other rows use one exact
class.

Read-only import of this table: see the import line at the end of this section.

## Performance budget

- **Selection:** 5 rows. Three cold `Antiphon.Tests` builds at 15 min each (the first row's 15 min gives the
  45-minute build deadline) and two reuse rows. Ordinary floor = 15 + 15 + 15 + 8 + 10 = **63 min** across
  three runs. No row is expected to approach 30 min. A row over 30 min gets a Debug task with its timing.
- **Per-test (provisional, UNOBSERVED):** seeding 300 or 1,200 events is one EF batch, about 1-6 s by
  analogy with the CARD-1888 1,200-entry W tests, which are also provisional. W-1 at base reads 2,400 rows in
  well under 1 s. W-3..W-6 hold the runner pull on a signal with a 10 s `WaitAsync`, as the existing C1205
  tests do. No per-test `[Timeout]` is added. If Code adds one, use about 2x measured p95, with 30 s as the
  provisional ceiling. Code records actual per-test durations from the TRX.
- **Phases:** container/readiness and migrations are shared once per assembly by `TestDbFixture` (no change).
  Each test creates one isolated schema through `SessionStateTestFixture` (existing). Data preparation is
  in-test (one session, n events). No host startup in CP-1..CP-4. CP-5's hosted cases start `WatchdogHost`
  web factories as today (unchanged). Teardown is the fixture dispose. Build and slot wait are reported
  separately on the `CHECKPOINT` line.
- **CP-4/CP-5 estimates** are UNOBSERVED for these classes on server2. Report actual wall time.
- **Production runtime budget:** per observation, one statement instead of a whole-session read: an
  index-only count over the session's `(AgentSessionId, Sequence)` entries, two seeks, and 64 full rows. The
  count stays O(rows) in index tuples (§10). Lock hold at the fence drops by the same factor.

## 9. Collisions, order and rollout

- **CARD-1888 (Code `6ea96bcb`, tip `2bc851b5c`, not landed at 7901e9ba5)** edits `AgentSessionRuntime.cs`
  hunks `@@1107` (the `:1110` call gains a lease argument), `@@1400` (the `PersistTranscriptCoreAsync`
  signature) and `@@1542` (the memo record at the `pending.Count == 0` return, adjacent to this slice's fence
  block `:1532-1541`). **ORDER: CARD-1888 lands first. This Code task is cut from a master that contains it**
  (F-6). If dispatched earlier, the land rebase conflicts at `:1110` and around `:1540`. Do not put a second
  Code seat on `AgentSessionRuntime.cs` transcript persistence while CARD-1888 is in flight.
- **CARD-1964** (phone-home recovery pump) and **CARD-1960/1961** (workspace slots): no shared file.
- **CARD-1915/1933** (session-state summary): no shared file. The stamp does not use the summary.
- **Slice 2 (prompt span):** no shared file. It can be planned in parallel.
- **Rollout:** server code only. No migration, no index and no setting. Activation is the normal rc port
  (S2+S3 together) plus the orchestrator's server restart (AppHost restart on the desktop, or the server2
  production restart after the CARD-1744 window). Confirm `GET /api/version` afterwards.
- **Waiting sessions:** this plan adds or changes no session that waits for input.
- **Review:** an ordinary Review (round 1 full) checks the equivalence argument (§4) against every
  `TranscriptEntries` writer, that W-3/W-4/W-5 are red in the CP-2 receipt at the S2 SHA, that the fallback
  (W-7) is today's read, and the re-grep gate. Review runs
  `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <code base> -HeadRef <tip>`.

## 10. Expected effect and what to measure (question e)

- **Postgres:** a probe of the 412,289-row session reads 2-3 statements, each with an index-only count over
  about 412k index tuples, two seeks and 64 rows. The count is the remaining O(rows) term. The estimate is
  about 10-50 ms per statement (UNOBSERVED; it depends on the visibility map), against 8.4-9.2 s per probe
  today. At about 30 probes an hour that is under 0.002 core. **Saved:** about 0.15 core of leader time
  (statement 1, 28.6 % of measured statement time), plus the parallel workers if the sampled 72-75 % workers
  were statement 1's (up to about 0.3 more core; UNOBSERVED, note M-2). Saved too: cancelled reads that
  pg_stat_statements never counted, and the session-row lock held for seconds at the fence. "A few
  milliseconds" would need the count from a summary (CARD-1915). Not in this slice.
- **Server:** no longer materialises about 412k `RecoveryRow`s with all text 2-4 times per probe, or compares
  two such arrays by value. The allocation and GC saved are UNOBSERVED.
- **Not removed:** the runner snapshot pull (`GetTranscriptAsync`, `:1071`) and, on the idle path, the
  whole-snapshot ingest (CARD-1888's fix). If the probe's `duration` stays in seconds after deploy, the pull
  dominates and is a follow-up.
- **Measure after deploy (orchestrator):**
  - **M-6a:** a `pg_stat_statements` delta over 10 min. The untagged whole-row statement 1 should disappear,
    and the new `rt.recovery.observe` statement should show rows/call ≤ 65 and a mean in milliseconds to tens
    of milliseconds. Record calls/min.
  - **M-6b:** the `Stale Working probe result` lines for b2f1a869: `duration` should drop from 8.4-9.2 s to
    the runner pull time plus well under 1 s.
  - **M-7 (before or after deploy, free, the same log):** count the result lines for b2f1a869 by
    `Outcome`/`Reason`, and the `probe unknown` warnings. `CheckedWorking`/`runner_working` confirms H-churn;
    a repeating Unknown reason confirms H-unknown and names the reason. This decides F-3's shape.

## 11. Operator decisions (question f)

- **F-1:** Accept the documented hole: in-place updates and non-rebasing same-`Sequence` replacements below
  the 64-row tail are not observed, because no production writer does either, and the S3 re-grep gate stops
  the slice if one appears (D-4)? **Recommend yes.** It is the same hole CARD-1888's clean Review accepted.
  Closing it needs an O(rows) heap read or a migration.
- **F-2:** Accept the deliberately incomplete S2 commit so that W-3/W-4/W-5 have a committed red, with S2 and
  S3 landed together in one Merge (D-9)? **Recommend yes.** Mutation checks are waived, and this is the
  CARD-1888 precedent.
- **F-3:** After M-7, file a separate card for the scheduling question: gate `Due()` on the age of the
  session's newest runner-sourced activity row, so that an actively streaming Working session is not probed?
  **Recommend yes.** It is not in this slice, because it changes CARD-1205 S4 semantics and needs its own
  T-witnesses.
- **F-4:** Rotate the orchestrator session b2f1a869 at a natural break, as a stop-gap until this lands?
  **Recommend yes, when convenient.** A fresh session's probes cost milliseconds today. This fix makes that
  unnecessary afterwards.
- **F-5:** `TranscriptPromptSpan` (statement 3) is CARD-1969 slice 2, with its own plan and its own Review
  round focused on the delivery invariants? **Recommend yes.**
- **F-6:** Dispatch this Code only after CARD-1888 (`6ea96bcb`) lands on master? **Recommend yes.**
  Otherwise the hunks around `:1110` and `:1540` conflict.
