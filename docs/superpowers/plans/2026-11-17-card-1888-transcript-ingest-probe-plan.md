# CARD-1888 plan: stop re-probing stored transcript UUIDs on every snapshot replay

Card: CARD-1888, "Transcript ingest re-probes ~600 stored UUIDs per batch; whole-session reads"
(Backlog, Normal/Soon, rank 8, revisions 0, not parked, not operator-only, not on master).
Master read: `300c8a99d0d267a39791410b7540189ab702f251` (origin/master, 2026-11-17).
Plan task: c58df935 (Opus, Plan). Lane: Linux on server2, which is the runner default
(`GET /api/runner-defaults` names `globalRunnerId=server2`). No Windows-only surface.

## 1. Problem as measured

### What is probed

`AgentSessionRuntime.PersistTranscriptCoreAsync` (`server/Application/Services/AgentSessionRuntime.cs:1409-1576`)
dedups every incoming batch by transcript-line `(Uuid, Kind)`, and by runner `Sequence` for
null-UUID entries. Before any insert it asks the database which of the batch's UUIDs are already
stored:

- `TranscriptIngestPrelude.ReadAsync` (`server/Infrastructure/Data/TranscriptIngestPrelude.cs:54-85`)
  sends one command. It returns the session row, the stored max sequence, the first UUID chunk
  (at most 512 keys, `Uuid = ANY(@uuids)`) and the null-UUID sequences. Its tag is
  `session-state.identity` when UUIDs are present and `rt.ingest.prelude` when they are not.
- Each further 512-key chunk is its own EF query (`AgentSessionRuntime.cs:1449-1457`), with the tag
  `session-state.identity`, shape `SELECT t."Uuid", t."Kind" FROM "TranscriptEntries" ... "Uuid" = ANY($2)`.

So one batch of N UUIDs costs `ceil(N/512)` commands and N index seeks on
`IX_TranscriptEntries_AgentSessionId_Uuid` (non-unique, INCLUDE Kind), whether or not anything is new.

### Who sends whole histories

Nine call sites pull the runner's full transcript snapshot (`GetTranscriptAsync`) and ingest all
of it. They are `SyncTranscriptAsync` (`:1258`, called by `SessionRunnerEventPump.cs:204` and
`AgentSessionService.cs:2357`), `PullAndPersistAsync`/`CatchUpTranscriptAsync` (`:1030`),
`CatchUpForCeilingAsync` (`:987`), `ProbeWorkingTranscriptAsync` (`:1110`), receipt readiness
(`AgentSessionRuntime.ReceiptReadiness.cs:81`), `PhoneHomeRecoveryPump.cs:388`, and
`ChannelOutboundTerminalTranscript.cs:28`. The live stream (`ObserveTranscriptAsync`, `:413`)
sends one entry per batch. Within one tailer lifetime a runner snapshot is the previous snapshot
plus a new tail, so almost every probed key is already stored.

### Observed counts (cited from the card; not re-measured by this plan)

Desktop `pg_stat_statements`, Investigate task 1540fb0b, reset 2026-11-11 17:45Z, about 44.1 h
(source: card body; query method: docs/logs.md `pg_stat_statements`):

| Statement | Calls | Rows | Rows/call | Exec time |
|---|---:|---:|---:|---:|
| `-- session-state.identity SELECT t."Uuid", t."Kind" ... "Uuid" = ANY($2)` (chunk form, `:1451`) | 1,068,019 | 642,995,481 | 602 | 5,394 s, 2.42 G shared blocks |
| Prelude UNION form, 120 s delta | 71 | n/a | n/a | 214.6 ms each, 15.2 s |
| Chunk form, 120 s delta | 2,168 (18/s) | n/a | n/a | 14.4 s |

Rows/call of 602 means the chunk probe returns more rows than its 512 keys. One transcript line
UUID is stored under several kinds (`C698_same_uuid_different_kinds...`), so nearly every probed
key is already stored. In the 120 s window that is about 30 chunk commands per prelude, which
points to snapshots of roughly 15,000 UUIDs. That size is an inference, UNOBSERVED directly. Card
estimate: 0.25 core-equivalent of DB time from the identity and prelude statements.

UNOBSERVED, and not claimed by this plan:
- Why the prelude form averages 214.6 ms when a chunk averages 6.6 ms. Its plan was not captured.
  The `max()` subquery or a generic plan for `ANY` are candidates. Code does not depend on this.
- Which caller dominates the snapshot replays. There is no per-caller tag. The 0.6 prelude/s rate
  fits the periodic sync and catch-up loops, but no measurement attributes it.
- Any link to the 2026-11-17 desktop overload. This plan has no evidence for one and makes no
  such claim.

### Whole-session reads (second half of the card)

Grep at 300c8a99d matches two of the three cited shapes:
- `SELECT Sequence, Text, Timestamp, Kind ... Kind IN ($2,$3) ORDER BY Sequence` is
  `TranscriptPromptSpan.LoadAsync` (`server/Application/Services/TranscriptPromptSpan.cs:88-94`):
  88,720 calls, 1,503 rows/call. This is the dispatch prompt-span delivery evidence read.
- `SELECT Timestamp, CreatedAt WHERE AgentSessionId=$1` is
  `PolicyRefreshService.HasBeenIdleLongEnoughAsync` (`server/Application/Services/PolicyRefreshService.cs:530-541`):
  6,062 calls, 74,491 rows/call. C# only uses `Max(Timestamp ?? CreatedAt)`.
- The full-row read (2,723 calls at 1,567 ms) has an UNOBSERVED caller. Its select shape is not
  quoted on the card.

## 2. Ground truth: card vs code

| Card assumes | Code at 300c8a99d | Consequence |
|---|---|---|
| Ingest could probe only UUIDs "above the session's stored high-water mark (sequence or timestamp)". | Stored `Sequence` is rebased per session (`:1460-1481`). Runner `Sequence` restarts per tailer lifetime (`:1426-1432`). Timestamps are optional and not monotonic. Catch-up exists to fill gaps in the middle (`SyncTranscriptAsync` doc, `:1247-1251`). | A high-water cut can skip an unstored middle entry, such as a missed `UserPrompt`. That fails open against transcript-confirmed delivery evidence. Rejected (D-1). |
| The 600-per-call statement is "the identity probe". | It is the chunk form for chunks 2 and later (`:1449-1457`). The first chunk rides in the prelude. | Both forms must shrink. The fix sits before the chunking. |
| "Dedup correctness for replayed batches (CARD-1403 S3) must hold." | `C1403_persist_prelude_keeps_dedup_and_rebase` and `C1403_persist_prelude_is_one_command` pin dedup, rebase and the one-command prelude. | Keep both unchanged and green (CP-3). |
| A unique index could make inserts idempotent. | `(AgentSessionId, Sequence)` is unique. `(AgentSessionId, Uuid)` INCLUDE Kind is not unique. | Insert-ignore needs a migration (human-only gate) and the sequence rebase still needs answers before the insert. Rejected (D-3). |
| Transcript rows are only appended. | Only one delete exists: `DataRetentionService.PruneTranscriptsAsync` → `TranscriptEntries.Where(AgentSessionId).ExecuteDeleteAsync` (`DataRetentionService.cs:372`), which removes the whole session under `MutateTranscriptAsync`. No `ExecuteUpdate` on `TranscriptEntries` exists. Code re-greps this (S3 gate). | "Stored stays stored" holds except for a whole-session delete. The memo is invalidated on that delete (D-5). |
| (not stated) | A null-UUID entry whose runner sequence `s` is at or below the stored max, with no stored row at `s`, is inserted at `max+1`. On the next replay, the probe at `s` still finds nothing, so the entry is inserted again. | This plan does not change null-UUID dedup (D-6). It is a separate defect, UNOBSERVED in data. Operator question F-3. |

## 3. Design

### Chosen: a verified runner-prefix memo (process-local, one per session)

After a batch commits cleanly, the session's `SessionStateStore.Entry` records a small memo:

```
IngestPrefix { int Count; byte[32] Digest; Guid FirstRowId }
```

- `Count` and `Digest`: a SHA-256 over the identity tuple `(Uuid ?? "", Kind, Uuid is null ? Sequence : 0)`
  of each of the batch's first `Count` sanitized entries, in source order. Each field is
  length-prefixed, so two different tuple sequences cannot encode to the same bytes.
- `FirstRowId`: the `Id` of the session's lowest-`Sequence` stored row as the prelude read it.
  If the session was empty, it is the first row this batch inserted.

On the next batch for that session:

1. Digest the incoming entries. If `memo.Count <= entries.Count` and the digest of the first
   `memo.Count` entries equals `memo.Digest`, then the UUID entries in that prefix are "verified
   stored". They are left out of the UUID probe. Only the tail's UUIDs, together with all null-UUID
   sequences (unchanged, D-6), go to the prelude.
2. The prelude's part 0 also returns `FirstRowId`, through one more index seek on the unique
   `(AgentSessionId, Sequence)` index in the same statement and snapshot. If it differs from
   `memo.FirstRowId`, the memo is stale: rows were deleted or replaced. The prefix UUIDs are then
   probed with the existing chunk loop, which is exactly today's path and cost, and the memo is dropped.
3. Dedup, rebase, `AdmitActionsAsync`, the recovery/receipt fences, the save, the fallback and
   publication are unchanged. A verified prefix UUID entry is skipped the way a found-stored entry
   is skipped today. A tail entry that repeats a prefix identity is still found by the tail probe,
   because the prefix row is stored, and is skipped.
4. The memo is recorded or replaced only when all of these hold:
   - the batch took the clean save path, or every entry was a duplicate;
   - there was no `RecoveryFenceRejection`, no `NeedsReload`, no fallback and no cancellation;
   - the batch is at least as long as the current memo, or extends the memo's prefix.
   So a one-entry live batch never replaces a snapshot memo.
5. Every `SessionStateStore.LoadAsync` (reconcile or reset) clears the memo, and so does every
   `BeginMutationAsync` lease (in-process deletes such as retention). Idle eviction and capacity
   non-admission drop it together with the entry. A server restart starts cold, and the first
   replay pays today's cost once.

Equivalence argument (the property W-3 and W-4 pin): today a UUID entry `(U,K)` is skipped iff
`(U,K)` was stored at prelude time or appeared earlier in the batch. A prefix entry under a valid
memo was stored, by insert or by finding it, when the memo was recorded. Rows only leave the table
through a whole-session delete. That delete changes the first row's `Id`: our own inserts always go
above the max, and a re-insert after a delete gets a fresh `Guid`. So the entry is stored now, and
today's code would also skip it. Ordering is unchanged because the tail keeps its source order.
Idempotence is unchanged because a replay of a verified batch inserts nothing. Fail-closed: an
unknown or mismatched fingerprint falls back to probing everything. The memo is never consulted
across sessions, and never for null-UUID entries.

Two servers on one database, as in a rolling rollout (temp beside old): inserts by the other
process do not move the first row, and they are found by tail probes. A whole-session delete by
the other process changes `FirstRowId` and invalidates the memo. A concurrent delete racing our
commit leaves our re-inserted row as the new first row, which differs from the memo, so the memo
is invalidated.

Expected effect: a snapshot replay sends its tail UUIDs (normally 0-10), in one command. The card's
verify condition "identity rows per call < 10" is the target. A cold session pays today's cost once
per process lifetime or reconcile.

Cost: SHA-256 over about 15,000 identity tuples (about 0.6 MB) per snapshot ingest. That is under
1 ms of CPU, an estimate (UNOBSERVED). Memory is about 60 bytes per cached session.

### Rejected alternatives

| Option | Why rejected |
|---|---|
| D-1 High-water mark by stored sequence or timestamp (the card's proposal) | Runner and stored sequences are different number spaces. Prefix-closure ("everything below X is stored") is false exactly when catch-up is needed: a missed middle event. That fails open. |
| D-2 Set-based anti-join (`unnest(@uuids,@kinds) WHERE NOT EXISTS`) returning only missing keys | It removes about 600 rows of transfer per call, but every key is still an index seek, so most of the 2.42 G shared blocks and 5,394 s exec remain. It can be a later complement if tail probes ever grow. |
| D-3 Unique `(AgentSessionId, Uuid, Kind)` index with `ON CONFLICT DO NOTHING` | It needs a migration and an index build on 1.69 M rows (human-only gate). Existing duplicates are UNOBSERVED. Rebase and action admission need new-row knowledge before the insert. |
| D-4 Bounded in-memory set of stored `(Uuid,Kind)` per session | It is correct with the same fingerprint, but costs O(rows) memory. `MaxSessions` defaults to 4096, and sessions reach about 75,000 rows (card: 74,491 rows/call), so it needs a second global budget. The prefix memo is O(1) and covers the measured shape: same-tailer snapshot replays. |
| Tail-first probing, stopping at the first fully stored chunk | It assumes prefix-closure. It fails open for the same reason as D-1. |
| Fingerprint by `max(Sequence)` or the max row | Every insert, including our own live stream, moves it, so the memo would be invalidated on nearly every batch. A delete followed by a re-insert can restore the same max. |
| Fingerprint by `count(*)` | It costs an index-only scan of the session (up to about 75,000 tuples) on every live single-entry batch. |

## 4. Slices

| Slice | Files | Change |
|---|---|---|
| S1 (red tests) | `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.Persist.cs`, new `tests/Antiphon.Tests/TestHelpers/IdentityProbeCounter.cs` | Add W-1 to W-4 (section 6) and a `DbCommandInterceptor` that sums the element counts of array parameters on commands whose leading tag is `session-state.identity` or `rt.ingest.prelude`, and counts those commands. It reuses `DbCommandMetrics.LeadingTag` the way `QueryBudget` does. `PersistFixture.CreateAsync` already takes one `IInterceptor`. No production change. |
| S2 (memo, no fingerprint: deliberately incomplete) | `server/Application/Services/SessionStateStore.cs` (memo field on `Entry`, accessors on `Lease`, cleared in `LoadAsync` and `BeginMutationAsync`), `server/Application/Services/AgentSessionRuntime.cs` (`PersistTranscriptCoreAsync` takes `SessionStateStore.Lease?`; all four callers pass their lease: `:1358`, `:1403`, `:1110`, `ReceiptReadiness.cs:81`), new `server/Application/Services/TranscriptIngestPrefix.cs` (digest and memo record) | Prefix skip and record rules 1, 3, 4 and 5. W-1 and W-2 turn green. W-3 must be red here: the memo trusts a prefix whose rows another process deleted. This proves W-3 is not a stub. |
| S3 (fingerprint) | `server/Infrastructure/Data/TranscriptIngestPrelude.cs` (part 0 adds `FirstRowId` through `ORDER BY "Sequence" LIMIT 1`; `NULL::uuid` in the UNION branches; `Result` gains `FirstRowId`), `AgentSessionRuntime.cs` (rule 2 fallback) | W-3 green. Before committing, re-grep `TranscriptEntries` for `ExecuteDelete`, `ExecuteUpdate`, `Remove` and raw `DELETE`/`UPDATE`. A new partial-delete or identity-update path invalidates the equivalence argument and stops the slice (report blocked). |
| S4 (optional, F-2) | `server/Application/Services/PolicyRefreshService.cs:530-541` | Replace the whole-session `(Timestamp, CreatedAt)` list with `MaxAsync(t => (DateTime?)(t.Timestamp ?? t.CreatedAt))`. A null result keeps `return false`. Covered by `PolicyRefreshServiceTests` (21 tests, CP-5). |

Not in this card (F-2 says split): `TranscriptPromptSpan.LoadAsync` (a delivery-evidence read; a
bound by `dispatchedAt` must keep null-timestamp rows and the compact-wrapper proof), the
unattributed full-row read, and the null-UUID re-insert (F-3).

### Must NOT touch

- `ChannelOutboundTerminalTranscript.cs`: its own payload-equality proof read stays a full
  re-read by design.
- `PhoneHomeRecoveryPump.cs`: a caller only; CARD-1964 is in flight there.
- `SessionStateLoader.cs`, `SessionStateSnapshot.cs` and the CARD-1915 summary
  migration/triggers: the memo is not part of the snapshot record or its equality/revision.
- `DataRetentionService.cs`: invalidation comes from the existing `BeginMutationAsync` it
  already uses, so no edit is needed.
- `server/Migrations/**`: no migration and no index.
- The selector/index chain (`scripts/impacted-classes.ps1`, `scripts/lib/release-impact*`,
  `tests/impacted-classes.json`, `tests/derived-impact-index.json`): held by CARD-1958, and not
  needed here.

### Existing tests affected

- **Statement budget:** `AgentSessionRuntimeTests.C1403_persist_prelude_is_one_command` asserts 1
  command for the prelude scope. It must stay 1 (part 0 grows a column; no new command).
- **Dedup/rebase:** `C1403_persist_prelude_keeps_dedup_and_rebase`,
  `C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations` and
  `C561_sync_over_a_runner_snapshot...` must stay green unchanged.
- **Fenced callers:** `WorkingTranscriptRecoveryTests` (T10-T16, 7 tests) and
  `StandingResumeReceiptReadinessTests` (6) exercise `PersistTranscriptCoreAsync` with fences.
  The signature changes (lease parameter), so they run in CP-4.
- **Store:** `SessionStateCommitTests` (8) and `SessionStateCacheReadTests` (2) cover entry,
  lease and publication.
- **Retention:** `DataRetentionServiceTests` (64) covers whole-session prune under the mutation
  lease.
- **Not affected:** migrations, public DTOs and routes, operation labels, remote script functions.
- **Impacted-classes table (CARD-1782):** `scripts/impacted-classes.ps1 -Path <the S1-S3 files> -Json`
  at 300c8a99d returned `kinds: []`, so no row matches. The Merge fixed subset (`-MergeSubset`)
  still applies at merge.

## 5. Decisions (defaults the plan was written under)

- **D-1 to D-4:** rejected alternatives, as tabled above.
- **D-5:** the memo is process-local on `SessionStateStore.Entry`, under the existing session
  gate. It is never persisted and never shared, and it is not part of `SessionStateSnapshot`.
  Reason: a restart or reconcile costs one full probe; persistence would add a schema change.
- **D-6:** null-UUID entries keep today's sequence probe, so behaviour is unchanged for them,
  including the re-insert hazard in section 2. Reason: exact equivalence for the delivery-evidence
  path. F-3 asks whether to fix the hazard separately.
- **D-7:** no new metric or route. Verification uses the card's own `pg_stat_statements` 120 s
  delta. Reason: a `GetMetrics` field is a public surface change.
- **D-8:** the digest is SHA-256 (`IncrementalHash`), not a 64-bit hash. Reason: a collision would
  skip an unstored entry (fail-open). The cost is negligible.
- **D-9:** S2 is committed in a deliberately incomplete state so that W-3 has a committed red.
  Reason: mutation checks are waived (operator 2026-11-12), so the red has to come from the slice
  order instead.

## 6. Verification design

Witnesses live in `AgentSessionRuntimeTests` (partial `AgentSessionRuntimeTests.Persist.cs`) on the
shared `TestDbFixture` Postgres through `PersistFixture`. Each test makes its own session id.
Snapshots are built with the existing `TranscriptEvent(...)` helper: 1,200 entries with distinct
UUIDs (`c1888-{i}`, kinds alternating `AssistantText`/`ToolCall`; W-4 adds the multi-kind case), so
the base code needs 3 identity commands for them (512 + 512 + 176).

| ID | Test | Assertion | At base 300c8a99d | Why, from the code |
|---|---|---|---|---|
| W-1 | `C1888_snapshot_replay_probes_only_the_new_tail` | Persist 1 prior row, then the 1,200-entry snapshot. Then replay snapshot + 5 new entries under `IdentityProbeCounter`: probed UUID keys `<= 5` and identity/prelude commands `== 1`. Rows = 1 + 1,200 + 5. Sequences strictly increasing in source order. | **Red**: probed keys = 1,205 (first chunk 512 in the prelude, then chunks of 512 and 181) across 3 commands. | `:1433-1443` sends every UUID to the probe; `:1449-1457` loops the remaining chunks. |
| W-2 | `C1888_live_entry_between_snapshots_keeps_the_prefix_memo` | Snapshot (1,200), then `ObserveTranscriptAsync` of one new entry, then replay snapshot + that entry + 4 new. Probed keys `<= 5`, one command, rows +4 (the live entry is not duplicated). | **Red**: 1,205 keys. | Same lines. The live batch must not replace the memo (rule 4). |
| W-3 | `C1888_rows_deleted_by_another_writer_are_reinserted_on_replay` | Snapshot (1,200), then delete the session's `TranscriptEntries` through a separate `AppDbContext` (`f.Db()`), which bypasses the store like another server's retention. Replay the same snapshot: rows = 1,200 again, and `UserPrompt` entries are present. | Green at base, because it always probes. **Red at S2** (CP-2): the memo skips the 1,200-entry prefix, so 0 rows. | Guards rule 2 (`FirstRowId` fingerprint). |
| W-4 | `C1888_duplicates_in_and_across_batches_stay_deduplicated` | Snapshot, then replay a batch whose tail repeats a prefix `(U,K)`, repeats a tail `(U,K)` twice, adds the same `U` under a new kind, and has 2 null-UUID entries (one stored sequence, one new). Expect exactly the new `(U,K')` row and the new null-UUID row. A second replay adds nothing (`LastStoredSeq` null). | Green at base (guard). | Pins equivalence: the tail probe still finds prefix rows, and in-batch `seenUuids` dedup is kept. |

The existing `C1403_persist_prelude_is_one_command` and
`C1403_persist_prelude_keeps_dedup_and_rebase` serve as the CARD-1403 guards (unchanged).

Red proof on paper: in W-1 and W-2 the counter reads the array parameters actually bound to the
commands (`uuids` in the prelude; the EF `ANY` array parameter in the chunk query). At base,
nothing filters the UUID set before `Chunk(512)`, so the counter sees all 1,205 keys. The only way
the count drops is that UUIDs never reach the probe, which is the change under test. A constant or
self-compare cannot satisfy both "keys ≤ 5" and the row-count assertion.

### Checkpoints

Lane: Linux, server2 (default runner; omit `-Runner` and `-Platform`). One isolated build per
`After` group.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1888-red/` | c1888-red | `/*/*/AgentSessionRuntimeTests/(C1888_*)` | W-1..W-4 red proof | exactly 4 executed; W-1 and W-2 red on the probed-key assertion (1,205 > 5); W-3 and W-4 green | 4 | 15 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1888-memo/` | c1888-memo | `/*/*/AgentSessionRuntimeTests/(C1888_*)` | W-1, W-2 green; W-3 red proof | exactly 4 executed; W-3 red on the row count (0 vs 1,200); W-1, W-2, W-4 green | 4 | 15 |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1888-green/` | c1888-green | `/*/*/AgentSessionRuntimeTests/(C1888_*)\|(C1403_*)` | W-1..W-4, C1403 guards | all 6 listed, 0 failed/skipped | 6 | 15 |
| CP-4 | all | CP-3 | ingest-callers | `/*/*/(AgentSessionRuntimeTests*)\|(WorkingTranscriptRecoveryTests*)\|(StandingResumeReceiptReadinessTests*)\|(SessionStateCommitTests*)\|(SessionStateCacheReadTests*)/*` | existing tests affected | >= 68 executed (41 + 4 new + 7 + 6 + 8 + 2), 0 failed | 68 | 8 |
| CP-5 | all | CP-3 | retention-policy | `/*/*/(DataRetentionServiceTests*)\|(PolicyRefreshServiceTests*)/*` | retention invalidation; S4 if taken | >= 85 executed, 0 failed | 85 | 8 |

Import (read-only, plan commit 7bd3255c3): `dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan <this file> --out .antiphon/c1888-plan-import.yaml` gave exit 0, "imported 5 rows", 0 warnings, 3 builds, and row timeouts of 45/45/45/24/24 min.

Suffix-collision check at 300c8a99d: no test class name starts with any operand other than itself
(`grep -rhoE "class <Name>[A-Za-z0-9_]*" tests`). `C1888_` is new, and `C1403_` matches exactly the
two methods in `AgentSessionRuntimeTests`. If F-2 is "no", CP-5 still runs: it is the retention
guard. Expected red rows (CP-1, CP-2) are run once at their slice and are not re-run after green.

## Performance budget

- **Selection:** 5 rows. Three cold `Antiphon.Tests` builds at 15 min estimate each (45-minute
  build deadline, the CARD-1958 lesson) and two reuse rows. Ordinary floor = 15 + 15 + 15 + 8 + 8
  = **61 min**. That is under the 30-minute Debug trigger per row; the total is spread over 3
  slices.
- **Per-test:** the new W tests are provisional at about 3-6 s each: about 1,200-row EF insert, a
  replay, and a count. No p95 has been measured, so the budget is provisional and TestDesign or
  Code records actual durations from the TRX. A per-test `[Timeout]` is not added. If Code adds
  one, use about 2x measured p95, with 30 s as the provisional ceiling.
- **Phases:** container/readiness and migrations are shared once per assembly by
  `TestDbFixture` (no change). Data preparation is in-test (one session row, 1,200 entries).
  There is no host startup and no snapshot/clone. Teardown is the `PersistFixture` dispose. Build
  and slot wait are reported separately on the `CHECKPOINT` line.
- **Row estimates** for CP-4 and CP-5 are UNOBSERVED for these classes on server2. They are
  provisional. Report actual wall time in the Code report, and raise a Debug task if any row goes
  over 30 min (per docs/test-performance-standards.md).
- **Runtime budget (production):** each snapshot ingest saves about `ceil(N/512)-1` commands and
  N-tail seeks. It adds one SHA-256 pass and one index seek (first row) in the prelude.

## 7. Collisions, rollout, review and merge

- **In-flight work:** the 40 most recently updated remote branches at 300c8a99d were scanned. None
  touches `AgentSessionRuntime*.cs`, `TranscriptIngestPrelude.cs`, `SessionState*.cs` or
  `QueryBudget.cs`. `feat/card-task-aaabade9` edits `PhoneHomeRunnerClient.cs` (not ours).
- **CARD-1964** (phone-home recovery pump stall) may edit `PhoneHomeRecoveryPump.cs`, which calls
  `PersistTranscriptAsync`. This plan does not edit that file, and the call signature
  (`PersistTranscriptAsync(Guid, list, ct)`) is unchanged. There is no shared file.
- **CARD-1960** (workspace slots) shares no file.
- **CARD-1915** (Review) touches `SessionState*` tests and the summary triggers, not these files.
  The insert shape is unchanged, so the `c1915_insert` trigger is unaffected.
- **Selector/index chain:** not touched.
- **Triage rule:** do not put a second Code seat on transcript ingest.
- **Rollout:** server code only. No migration, no new index, no setting. Activation is the normal
  rc port plus a server restart by the orchestrator (AppHost on the desktop, or the server2
  production restart after the CARD-1744 window). The memo starts cold after a restart, so the
  first replay per session pays today's cost.
- **Post-activation check:** a 120 s `pg_stat_statements` delta on the production DB. Pass when the
  `session-state.identity` rows/call is < 10 and the chunk-form call rate falls by more than 90%.
- **Review:** an ordinary Review (round 1 full) checks the equivalence argument against
  `PersistTranscriptCoreAsync`, the memo record conditions (rule 4) and the invalidation points
  (rule 5). The CP-2 red receipt must show W-3 red at the S2 SHA. Review runs
  `scripts/check-evidence-diff.ps1 -BaseRef 300c8a99d0d267a39791410b7540189ab702f251 -HeadRef <tip>`.
- **Merge:** land onto master once Review is clean. Landable is checked with
  `git merge-tree --write-tree origin/master HEAD`, plus the `-MergeSubset` fixed rows. A
  reviewed rc port follows the standing rules.
- **Waiting sessions:** this plan adds or changes no session that waits for input.

## 8. Operator decisions

- **F-1:** Ship the prefix memo (D-5) instead of the card's high-water-mark proposal?
  **Recommend yes.** The high-water mark can skip an unstored middle entry, which fails open.
- **F-2:** Split the whole-session reads (`TranscriptPromptSpan`, the unattributed full-row read)
  into a follow-up card, keeping only the S4 `PolicyRefreshService` aggregate here?
  **Recommend yes.** S4 is a 6-line SQL aggregate with 21 existing tests. The prompt-span read is
  delivery evidence and needs its own plan.
- **F-3:** File a separate card for the null-UUID re-insert on replay (section 2, last row)?
  **Recommend yes.** It is UNOBSERVED in data. First measure with
  `SELECT "AgentSessionId", count(*) FROM "TranscriptEntries" WHERE "Uuid" IS NULL GROUP BY 1 ORDER BY 2 DESC LIMIT 20`.
- **F-4:** Accept the deliberately incomplete S2 commit, which is unsafe on its own, so that W-3
  has a committed red (D-9)? **Recommend yes.** The branch is never landed at S2. The alternative
  is a mutation PC, and mutation checks are waived.
