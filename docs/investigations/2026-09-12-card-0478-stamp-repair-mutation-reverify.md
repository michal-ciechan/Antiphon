# CARD-0478 Mutation re-verification (task 702f5ce7)

Subject: commit `3b021db9ddaef6a6b2d98de63d387d335082f8cb` on `feat/card-task-48888860`
(`23bba396` fix + `4b2041de` rename + `3b021db9` three new stamp-repair tests).
Checked out detached in `C:\Antiphon\worktrees\card-task-702f5ce7`; evidence branch
`mutation/card-0478-702f5ce7` at that commit.

Independent re-run of the four stamp-repair positive controls after Code (task e406088d)
added the three tests the prior Mutation pass (task c113100d) said were missing. All four
now reproduce. Prior pass found only PC-1 reproducing.

## Baseline

`--treenode-filter '/*/*/PostLandMutationDeliveryTests/C478_Completion*'` selects exactly
**5** tests (the two `C478_CompletionReplay*` retention tests plus the three new
`C478_CompletionNote*` tests) — `total: 5`, confirming the prefix wildcard is not the
silent-zero case. Baseline before any mutation: **5/5 pass (4m 14s)**.

Isolated output `--property:OutputPath=bin-702/` (forward slash); rebuild ~1m 15s.

## PC table

| PC | Mutation site | Test (method-scoped prefix filter) | Red observed | Green after restore |
|---|---|---|---|---|
| PC-1 | `DataRetentionService.PruneQueuedMessagesAsync` — delete the `CompletionNoteStamp.RepairFromAsync` call | `C478_CompletionReplay*` (2 tests) | **RED** 1 failed / 1 succeeded (1m 40s). `ShouldAssertException: task.CompletionNoteQueuedAt` at `PostLandMutationDeliveryTests.cs:311` in `C478_CompletionReplayAfterPartialEnqueueRetention`; `C478_CompletionReplayAfterRetention` stayed green | **2/2 pass** (1m 39s) |
| PC-2 | `CompletionNoteWorkHostedService.RecoverMissingSourcedCompletionNotesAsync` — delete the `RepairFromAsync` call | `C478_CompletionNoteScanner*` (1 test) | **RED** 1 failed (1m 12s). `ShouldAssertException: probe.HitsFor(settled.World.TaskId)` — the scanner skipped the repair and drove the task through `completion-scan` -> `EnqueueAsync` duplicate-skip instead | **1/1 pass** (1m 27s) |
| PC-3 | `SessionMessageQueueService.EnqueueAsync` — `false && stampCompletion` on the insert+stamp transaction (pre-fix two-commit behaviour) | `C478_CompletionNoteInsert*` (1 test) | **RED** 1 failed (1m 15s). `ShouldAssertException: await observer.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == ...)` — an **unstamped queue row survives** the post-insert fault: the exact CARD-0478 partial state | **1/1 pass** (2m 51s) |
| PC-4 | `SessionMessageQueueService.EnqueueAsync` duplicate-skip path — delete the `CompletionNoteStamp.ApplyAsync` repair | `C478_CompletionNoteDuplicate*` (1 test) | **RED** 1 failed (3m 01s). `ShouldAssertException: repaired.CompletionNoteQueuedAt` — the second enqueue skips as a duplicate and leaves the stamp null | **1/1 pass** (in final sweep) |

Verdict: **4/4 reproduce.** Each mutation was introduced alone, built, run, and restored
before the next was applied. No two mutations were live at once (PC-3 and PC-4 are in the
same method, so they were never batched).

## Restore verification

`git diff 3b021db9` is empty — source is byte-identical to the subject commit. Final sweep
of all five tests after restore: **5/5 pass (5m 06s)**. All `bin-702/` output directories
deleted.

## Test-authenticity spot check

Each new test exercises the real code path it claims, not a handcrafted shortcut:

- **`C478_CompletionNoteInsertStampAtomicity` (PC-3)** — `QueueWriteAfterInsertFault` is a real
  EF `SaveChangesInterceptor` registered through `ConfigureDbContext`. It captures the match in
  `SavingChangesAsync` (`Added SessionQueuedMessage` with `Origin == Delegation &&
  SourceTaskId != null`) and throws from `SavedChangesAsync`, i.e. after the INSERT is written
  and before the ambient transaction commits. The insert is the real production one from
  `SessionMessageQueueService.EnqueueAsync` during a real `SettleMutationAsync`, and
  `fault.Triggered` is asserted, so a silently-unfired fault cannot pass.
- **`C478_CompletionNoteScannerRepairsCanceledRow` (PC-2)** — sets the leftover row to
  `QueuedMessageStatus.Canceled` (the one status `HasCompletionNoteAsync`'s row predicate
  excludes) with the stamp nulled, then starts the **real** `CompletionNoteWorkHostedService`
  via `StartCompletionRecoveryAsync`. The `CompletionScanProbe : LandDeliveryBoundary` asserts
  zero `completion-scan` boundary hits for the task, which is what distinguishes the repair
  path from the duplicate-skip path — and PC-2's red is exactly that probe firing. Not a
  duplicate-skip test in disguise.
- **`C478_CompletionNoteDuplicateSkipRepairsStamp` (PC-4)** — `SettleMutationAsync` performs the
  first real `EnqueueAsync`; the test nulls the stamp and calls `Queue.EnqueueAsync` a second
  time with the same `SourceTaskId`, `ConversationKey`, `ContentDigest` and `Body`, then asserts
  the row count stays 1 **and** the stamp is repaired to that digest. Two genuine enqueues of
  the same completion.

## Commands to reproduce

```
git fetch origin feat/card-task-48888860 && git checkout --detach 3b021db9
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-702/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-702/ -- \
  --treenode-filter '/*/*/PostLandMutationDeliveryTests/C478_Completion*'
```
