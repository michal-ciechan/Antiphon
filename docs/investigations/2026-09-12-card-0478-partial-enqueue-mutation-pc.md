# CARD-0478 Mutation / positive-control pass (task c113100d)

Subject: commit `4b2041de05993d947d7b617a892cc7bdf73e73c2` on `feat/card-task-48888860`
("atomically stamp completion notes and repair partial enqueue" = `23bba396` + test rename `4b2041de`).
Checked out detached in `C:\Antiphon\worktrees\card-task-c113100d`; local branch
`mutation/card-0478-c113100d` created at that commit. All four mutations restored; `git diff`
against 4b2041de is empty for `server/**`.

## Runner notes (reusable)

- Method-level OR in `--treenode-filter` (`/*/*/Class/(A)|(B)`) selects **zero tests** on this
  TUnit 1.44 / MTP 2.2.2 runner and still exits 0 — a silent no-op. Verified: `total: 0`.
- The prefix wildcard **does** work: `/*/*/PostLandMutationDeliveryTests/C478_CompletionReplay*`
  selects exactly the two retention tests (`total: 2`). Used for every cycle below.
- Isolated output: `--property:OutputPath=bin-c113/` (forward slash). Rebuild after each mutation
  is ~1m10s; the two retention tests are ~2m45s–4m35s together.

## PC table

| PC | Defect introduced | Predicted | Observed | Verdict |
|---|---|---|---|---|
| PC-1 | `DataRetentionService.PruneQueuedMessagesAsync` — delete the `CompletionNoteStamp.RepairFromAsync` call | `C478_CompletionReplayAfterPartialEnqueueRetention` red | **RED**: `ShouldAssertException: task.CompletionNoteQueuedAt should not be null but was` at `PostLandMutationDeliveryTests.cs:311`. 1 failed / 1 succeeded of 2 (3m49s). | **reproduces** |
| PC-2 | `CompletionNoteWorkHostedService.RecoverMissingSourcedCompletionNotesAsync` — delete the `RepairFromAsync` call | same test red | **GREEN** 2/2 (3m58s) | **does NOT reproduce** |
| PC-3 | `SessionMessageQueueService.EnqueueAsync` — disable the insert+stamp transaction (`false && stampCompletion`), i.e. two separate commits as before the fix | partial-enqueue gap red | **GREEN** 2/2 (2m46s) **and** 10/10 on `C478_G14*` (G140–G149, includes the `QueueInsertFault` test `C478_G149_CompletionPersist`; 12m21s) | **does NOT reproduce** |
| PC-4 | `SessionMessageQueueService.EnqueueAsync` duplicate-skip path — delete the `CompletionNoteStamp.ApplyAsync` repair | duplicate-skip scenario red | **GREEN** 2/2 (4m36s) **and** 10/10 on `C478_G15*` (G150–G161, incl. the duplicate re-enqueue test `C478_G151_CompletionQueueKey`; 14m03s) | **does NOT reproduce** |

`C478_CompletionReplayAfterRetention` (the prior round's original retention regression) **passed in
every single run**, including PC-1's red run. The fix did not reintroduce it.

## Restore verification

After restoring all four mutations, `git status` is clean against 4b2041de and a fresh rebuild +
`C478_CompletionReplay*` run is **2/2 pass** (8m49s). Every `bin-c113/` output directory (14 of them)
was deleted.

## Why PC-2, PC-3, PC-4 cannot reproduce (root cause, not flake)

All three are structurally unreachable from the current tests, not "expected red that didn't fire".

**PC-2 — scanner repair is dead code in the only test that could see it.**
`C478_CompletionReplayAfterPartialEnqueueRetention` nulls the stamp, confirms receipt, then calls
`PruneQueuedMessagesAsync`, which **deletes** the queue row. Only after that does
`StartCompletionRecoveryAsync` start the scanner. `RecoverMissingSourcedCompletionNotesAsync`'s
`RepairFromAsync` filters `SessionQueuedMessages.Where(SourceTaskId in owedIds)` — zero rows by then,
so it is a no-op. `SettleMutationAsync` starts no scanner of its own (verified in the harness), so
there is no earlier window either. Worse, the call is *redundant even in production*:
`AgentTaskCheckService.HasCompletionNoteAsync` returns true from the first predicate whenever a
non-`Canceled` row exists, so with a live unstamped row the scanner already `continue`s — the repair
only pre-stamps against a later retention, which `PruneQueuedMessagesAsync` already does itself
(PC-1). The one case where it is load-bearing is a **`Canceled`** leftover row (excluded from
`HasCompletionNoteAsync`'s first predicate), and nothing tests that.

**PC-3 — no test interrupts between the insert and the stamp.**
The transaction only changes behaviour when something fails *after* `SaveChangesAsync` and *before*
`CompletionNoteStamp.ApplyAsync`. `C478_CompletionReplayAfterPartialEnqueueRetention` does not crash
there; it **simulates** the resulting state by setting `CompletionNoteQueuedAt/Digest = null` by hand.
The existing `QueueInsertFault` (G149/G150) throws in `SavingChangesAsync` on the *insert*, so the
stamp is never reached. Also note the stamp uses `ExecuteUpdateAsync`, which bypasses SaveChanges
entirely — a `SaveChangesInterceptor` can never fault it.

**PC-4 — nothing re-enqueues a duplicate while the stamp is null.**
`ApplyAsync` is a no-op unless `CompletionNoteQueuedAt == null`. Across the whole `tests/` tree only
two tests read or write `CompletionNoteQueuedAt`/`CompletionNoteDigest` (the two retention tests);
neither performs a duplicate enqueue with the stamp cleared. `C478_G151_CompletionQueueKey` does
re-enqueue the same digest, but the stamp is already set there. And the scanner cannot drive this
path: with a live non-`Canceled` row `HasCompletionNoteAsync` is already true, so it never calls
`EnqueueAsync`.

## Recommended positive controls for Code (each is a small, mechanical test)

1. **PC-3 / atomicity.** Copy `GrokRulesTransactionTests.QueueWriteFault(afterWrite: true)`: a
   `SaveChangesInterceptor` that throws in `SavedChangesAsync` when the change set contains an
   `Added SessionQueuedMessage` with `Origin == Delegation && SourceTaskId != null`. Assert
   afterwards that **no** queue row exists for the task and `CompletionNoteQueuedAt` is still null
   (with the transaction: both absent; without it: an unstamped row survives — the exact CARD-0478
   partial state). This is the only PC that proves the transaction is load-bearing.
2. **PC-2 / scanner repair.** Null the stamp, leave the row, set it `Canceled` (so
   `HasCompletionNoteAsync`'s row predicate does not short-circuit), run the scanner, assert the
   stamp is repaired and no second row appears. Without the scanner repair this loops
   enqueue→duplicate-skip forever.
3. **PC-4 / duplicate-skip repair.** Null the stamp, keep the row, call `EnqueueAsync` again with
   the same `SourceTaskId`+digest, assert the row count stays 1 **and** `CompletionNoteQueuedAt`
   is now non-null.

## Commands to reproduce

```
git fetch origin feat/card-task-48888860 && git checkout 4b2041de
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c113/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c113/ -- \
  --treenode-filter '/*/*/PostLandMutationDeliveryTests/C478_CompletionReplay*' \
  --report-trx --report-trx-filename base.trx --results-directory .antiphon/c113-base
```

TRX evidence retained under `.antiphon/c113-base`, `c113-pc1`, `c113-pc2`, `c113-pc3`, `c113-pc4`.
