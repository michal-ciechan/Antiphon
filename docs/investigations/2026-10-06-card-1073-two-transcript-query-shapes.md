# CARD-1073: two transcript query shapes use about 18 s of database time per 68 s

Date: 2026-10-06. Investigate task `4bd8ed4c`. Verdict: **confirmed**. The database time is the land-notification receipt scan materializing every post-floor prompt row, full entity, on each pass. Ready for **plan**.

Worktree HEAD and the deployed server reported earlier this task are `5b713f6855ee417737ff7d7e47cb340d66e3d9b8`. Citations below are that source. Measurements are read-only SELECTs against desktop Postgres (`antiphon`, port 17280). No reset, no `EXPLAIN ANALYZE`, no row text, no secrets.

## The two shapes

`pg_stat_statements` since `2026-09-30 16:39:44+00`. Snapshot `2026-10-06 19:41:11Z`. Both statements select the full `TranscriptEntries` entity (including `Text` and `ToolInput`), have no `LIMIT`, and order by `Sequence`. The SQL contains a CR, which is the Windows EF command text.

| queryid | calls | total exec | mean | rows/call |
|---|---:|---:|---:|---:|
| `2824069175592795718` | 720,154 | 75,557.1 s | 104.92 ms | 1,562.2 |
| `8388454389200601854` | 303,971 | 24,088.6 s | 79.25 ms | 1,504.3 |

Shape 1: `AgentSessionId = $1 AND Text IS NOT NULL AND Kind = $3 AND Sequence > $2`.

Shape 2: `AgentSessionId = $1 AND Text IS NOT NULL AND Kind IN ($3, $4) AND Sequence > $2`.

Together that is 99,646 s of execution over about 529,000 s of wall time since the stats reset, about 19% of one core on average. The card's 18.29 s / 68 s sample (about 27% of one core) was not in this clone; the Oct 5 write-up was never pushed. A 70.0 s window taken immediately after the snapshot, same two queryids, counters not reset:

| queryid | calls | exec | mean | rows/call |
|---|---:|---:|---:|---:|
| `2824069175592795718` | 72 | 3,033.8 ms | 42.14 ms | 851.6 |
| `8388454389200601854` | 108 | 7,601.2 ms | 70.38 ms | 1,506.0 |

Combined execution in that window is 10,635 ms, which is **10.3 s per 68 s**. Same statements, quieter than the Oct 5 sample because the one-kind calls in this window returned fewer rows (852 versus the lifetime 1,562). Over the same 70 s, `TranscriptEntries` `seq_scan` rose by 216 and `idx_scan` by 1,075. Live tuples 671,263.

An older sibling of shape 1, queryid `-5720321141429918009`, has the same predicate and no `ApiErrorTimeZoneId` column. It is frozen at 65,620 calls / 5,543.7 s / 84.48 ms / 1,383.8 rows. The running server emits the column-inclusive text.

The UUID membership statement (`4566677253624435224`, `Uuid IS NOT NULL AND Uuid = ANY ($2)`) is not this cost. At the same evening it had 2,774,695 calls, 6,640.5 s total, mean 2.39 ms. The CARD-0698 index is in use. Session-state seed, pins, fallback, and identity counters do not include these two statements; they are untagged.

## Where the SQL is built

`LandNoteReceipt.Prompts` builds both shapes (`server/Application/Services/LandNoteReceipt.cs:29-43`):

- `AgentSessionId` equals the destination and `Text != null`.
- One kind (`UserPrompt`) unless `AcceptsQueuedPrompt` (`:20-22`): non-legacy Held, Aged, Conflict, or Outcome, which adds `QueuedUserPrompt`.
- `Sequence >` the keyed row's `LastDeliveryBaselineSequence` when that floor exists.

`AgentTaskLandNotificationService.ReconcileAsync` materializes that query with no projection (`server/Application/Services/AgentTaskLandNotificationService.cs:243-248`):

```csharp
var evidence = (await prompts.OrderBy(p => p.Sequence).ToListAsync(ct))
    .FirstOrDefault(p => LandNoteReceipt.IsReceipt(expected, p.Text!));
```

The text match runs in memory after every post-floor prompt entity has been read. The gate is `DeliveryAttempts > 0` (`:228`). Several mismatch paths return before that read (`:176-237`), including a changed delivery generation (`:232-238`).

`AgentTaskLandNotificationHostedService` calls `ReconcileAsync` for every notification that is not Confirmed, NotRequired, or LegacyUnverified, then waits 5 s (`server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs:97-121`). The pass itself takes the rest of the period. `LandNotificationKind` and `LandNotificationState` are the integer enums at `server/Domain/Enums/LandingEnums.cs:16-17`.

The same one-kind full-entity load exists on the compaction receipt path (`server/Application/Services/CheckCompactionContinuationService.cs:583-589` and `:797-803`). It runs only while an episode is unresolved. At 19:41:11Z `CheckCompactionRecoveries` had two rows, both state Recovered (8). That path did not contribute to the 70 s window.

These callers use the same predicate but `Select` `Text` only, so they are different statements: `ExpectationSnapshotReader.cs:849-859` and `AgentTaskDispatcher.cs:4490-4494`.

## What was open during the window

At 19:41:11Z, non-terminal notes with `DeliveryAttempts > 0` and a sequence floor:

| shape | notes | sessions | prompt rows min / avg / max | floor min / median / max |
|---|---:|---:|---|---|
| one-kind (`UserPrompt` only) | 31 | 6 | 0 / 1,659 / 3,298 | 5 / 6,481 / 269,007 |
| two-kind (UserPrompt, QueuedUserPrompt) | 12 | 2 | 59 / 1,506 / 2,956 | 20 / 42 / 86 |

The two-kind notes are 8 non-legacy Aged and 4 non-legacy Outcome, all AwaitingReceipt. Their average prompt count, 1,506, is the shape-2 rows/call in the 70 s window. The one-kind sequence-floor notes are 21 non-legacy TaskCompletion AwaitingReceipt, 4 legacy Outcome AwaitingReceipt, 3 canceled TaskCompletion, 2 AwaitingReceipt DispatchBase, and 1 canceled DispatchBase.

Call arithmetic for the 70 s window is exact: 108 two-kind calls / 12 notes = 9 passes, and 72 one-kind calls / 9 passes = 8 notes. Every two-kind sequence-floor note ran on every pass. Eight of the 31 one-kind notes ran; the other 23 took an early return. Period = 70 / 9 = 7.8 s, which is the 5 s delay plus the time spent walking the open set.

Two-kind floors sit between 20 and 86, so each pass re-reads almost the whole prompt history of those two sessions. A plain `EXPLAIN` (not `ANALYZE`) for one of those notes is a bitmap index scan on `IX_TranscriptEntries_AgentSessionId_Sequence` for `AgentSessionId` plus `Sequence > floor`, then a heap filter for `Text IS NOT NULL` and the kind list. The index is used. The cost is fetching and returning the prompt text, not a sequential scan of the 671 k-row table. The planner's row estimate on that one plan was 11; the measured rows/call is about 1,500.

## Necessary work and repetition

A receipt check has to see prompt text above the delivery floor. Doing that once per new attempt is the necessary read. Repeating it for notes that stay AwaitingReceipt or Canceled, from a floor near the start of the session, every 7.8 s, is the repetition. Twenty notes (12 two-kind + 8 one-kind) produced the whole 10.3 s/68 s. Confirmed, NotRequired, and LegacyUnverified notes are already outside the scan.

## What this session's other cards add

- CARD-1079: `SeatWatchEnabled` defaults true and `OccupancySampleIntervalSeconds` defaults 60 (`server/Application/Settings/AttentionSettings.cs:12-21`). `SeatOccupancySampler` does not query `TranscriptEntries`. One occupancy tick per minute is not these statements.
- CARD-1065: `BlockedTaskParkingOptions.Enabled` and `ReclaimExisting` both default false (`server/Application/Settings/BlockedTaskParkingOptions.cs:6-7`). Nothing in this HEAD reads `ReclaimExisting`. The S9 reclaim sweep is not in the tree, so it adds no transcript SQL while the flags stay off.
- CARD-1076: `RemotePrepPushBudgetMinutes` defaults 20 (`server/Application/Settings/DelegationSettings.cs:632`) and bounds a git push in `RemoteWorkspaceService`. It is in this HEAD. It does not query transcripts.
- CARD-1082: `b8f9caaf9` and `4c052d063` are not ancestors of `5b713f685`. Not deployed.
- The dispatcher also reconciles due DeliveryFailure notes on its 5 s tick (`AgentTaskDispatcher.cs:4002-4019`, `PollIntervalSeconds` default 5). No DeliveryFailure note had a sequence floor at sample time, so that extra caller was idle for these two shapes.

## Tests that cover the touched code

Receipt truth is covered by `AgentTaskLandReceiptTests` and `AgentTaskLandQueuedReceiptTests`. Outbox persistence and recovery are covered by `AgentTaskLandNotificationPersistenceTests` and `AgentTaskLandNotificationRecoveryTests`. The `Select(Text)` sibling is covered by `ExpectationNoteDebtTests`. The cold compaction copy of shape 1 is covered by `CheckCompactionReceiptTests`. `TranscriptHotPathQueryTests` covers the CARD-0698 indexes (`Uuid_membership_seeks_by_session_and_uuid`, `Working_batch_uses_one_statement_and_indexed_boundaries`, `Prepared_plans_keep_the_partial_index_paths`, `Repeated_hot_reads_do_not_add_transcript_sequential_scans`) and does not cover this full-entity history load.

No current test fails when `ReconcileAsync` materializes every post-floor prompt entity. A check that would prove a fix is one reconcile of an AwaitingReceipt note whose floor leaves many prompts, asserting the command does not load N full entities. That assertion is not in the classes above.

## Uncertainties

- The Oct 5 figure of 18.29 s/68 s was not re-read from its raw trace. This window measured 10.3 s/68 s on the same two statements.
- The eight one-kind notes that pass the early returns were not identified one by one.
- Container CPU was not sampled. The measure here is `pg_stat_statements` execution time.
- Postgres CPU percent for this window was not sampled.

## Not done, noted

Read `Text` only, or stop at the first receipt, instead of `ToList` of every post-floor prompt entity on each pass (measured 10.3 s/68 s); a partial prompt index would not remove that text return; do not change retention or the UserPrompt verdict.
