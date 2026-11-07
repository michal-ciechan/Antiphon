# CARD-1759 Review round 1 (task 53194ebc): CLEAN

Subject: `origin/feat/card-task-5dd682f8` tip `40cd61ba85cdafcad88f7a7406222b3ec0367b95` (Code owner `5dd682f8-3daf-4f67-9c2c-972b33434837`, Grok). Base `8b75f315467f7b25c021ec1c0299c04e7398c0b8`; three commits: red `a186982424dc732f0213febcc7c6436e823f5766`, fix `bf6d0e263e019654471b84808eafe95e979e8f24`, tracked-row witness `40cd61ba85cdafcad88f7a7406222b3ec0367b95`. Diff: 4 files (`server/Application/Services/LandNotificationReconcileQueries.cs` new, `AgentTaskLandNotificationService.cs`, `server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs`, `tests/Antiphon.Tests/Application/AgentTaskLandNotificationReconcileLoadTests.cs`). Master `99f3d328cfe1aaaaf211278f24e43b9d11b61438`; `git merge-tree --write-tree origin/master HEAD` clean, tree `bf86e1b5d7272572902b6b6e4ca6c25cd7e0394d`. `tests/test-execution-policy.json` unchanged. Review did not modify product code; this document is its only commit.

## Verdict

CLEAN. No blocker. Five advisories (A-1..A-5) below; A-1 deserves a follow-up card because the card's CPU goal is mostly not met by this change on production data.

## Blocker checks

### (1) Reconcile outcome unchanged

Old page: every note with `State NOT IN (Confirmed, NotRequired, LegacyUnverified)`; `ReconcileAsync` then returned without writing for a null-queue note whose `NextAttemptAt > now` (and for a legacy null-queue note). New page adds `QueueMessageId != null OR NextAttemptAt <= now OR (Kind = ProviderInputWait AND ReplyTo != Session)`. The only notes dropped from the page are non-terminal, unlinked, not yet due, and not a non-session provider-input wait: for exactly those the old reconcile hit `return` at the due check (or the legacy check) before any write. Per state:

| State / shape | Old outcome | New page | Same? |
|---|---|---|---|
| Confirmed, NotRequired, LegacyUnverified | excluded from page | excluded | yes |
| Queued / RetryPending / DestinationUnavailable, unlinked, due | reconciled | kept | yes |
| Queued / RetryPending / DestinationUnavailable, unlinked, not due | loaded, returned without write | skipped | yes (no write either way) |
| AwaitingReceipt / Canceled / any state with a queue row | reconciled regardless of due | kept (`QueueMessageId != null`) | yes |
| ProviderInputWait with ReplyTo None (only non-Session value) | written NotRequired before the due check | kept by the third arm | yes |
| Legacy, unlinked, due | returned without write | kept, still returns without write | yes |

`ReconcileAsync` itself still re-checks the terminal set, the provider-input arm and `NextAttemptAt > now` with its own clock, so a note the page admits early is never written early. Busy-session skipping (CARD-1705) is untouched.

### (2) Tracked caller keeps its instance

`LoadNoteAsync` reloads a `Local` instance in place and otherwise runs the compiled tracked `Single`. For a fresh scope (the hosted per-note scope and `AgentTaskReplyService.DeliverCompletionObligationAsync`, which uses a new scope after the settlement commit) the compiled load is one SELECT whose values become both current and original values, so the UPDATE's WHERE carries the database token: identical to old `SingleAsync` + `ReloadAsync`. For an already-tracked instance `ReloadAsync` overwrites current and original values from the row (old behaviour, minus the redundant first SELECT), so a token changed by another writer is picked up and no stale token is written back; unsaved in-memory edits were overwritten by the old reload too. The catch path clears the tracker first, so it always takes the compiled fresh load, as before. Witness `C1759_TrackedStaleTokenRefreshesInPlace` changes the token from a second context, asserts one note SELECT, and compares the saved row to the instance; its reported mutant (skip the reload) saves `RetryPending` through the catch path and fails on the saved-row comparison added in `40cd61ba85`.

### (3) Compiled queries, keys, witness heuristic, clock

Map: `ConcurrentDictionary<IModel, Func<...>>` keyed by `db.Model`, the finalized runtime model shared by every context with the same model-affecting options (one in production). Reference identity is the right key: EF 9 (`Microsoft.EntityFrameworkCore 9.0.21`, Npgsql 9.0.4) binds a compiled query's executor to the entity-type metadata of the model it first compiled against. `GetOrAdd` may build two `CompileAsyncQuery` delegates under a race and keep one; harmless (nothing executes at construction), and each delegate's first execution initializes its executor through EF's lazy initializer. Compiled delegates execute on the calling context's connection and interceptors; the map caches plans, not rows. Tracking semantics match the replaced queries (`NoteById` tracked, the rest projections or `AsNoTracking`). `MessageById` with a null key throws `InvalidOperationException` as the old `SingleAsync(m => m.Id == null)` did, so the catch-path bookkeeping is unchanged; `MessageOrDefaultById` with a null key yields `keyed_queue_row_missing` as before.

Witness heuristic (`@__id` / `@__now` compiled vs `@__id_0` ad hoc): judged acceptable as a witness for this EF major, fragile across an EF upgrade (A-2). Clock: the page reads the scope's `TimeProvider` once; a note that becomes due after that read waits the rest of the scan plus the 5 s period. Accepted (A-3): producers reconcile immediately on their own path and the hosted page is the backstop; the added latency is bounded by one period and the comment in the hosted service states it.

### (4) Witnesses real

Red at the pre-fix commit, run by this Review in a detached scratch worktree at `a186982424` (build lease `24f43e1a-faf5-45f9-be1e-bd3c66444084` held 223 s; run lease `0cb4c7ab-348e-468f-8c57-0548488f6a11` waited 106 s, held 72 s): total 4, failed 4, skipped 0, 1m01s. Failure messages are the intended assertions, not fixture errors: `loads.Count` (fresh load counted 2), `loads.Count` (destination lookup), `_observer.NoteSelects(note.Id).Count` (tracked path counted 2), `_observer.NoteSelects(futureId).Count` (undue note loaded by the page). Green at the tip: CP-1 below. The five mutants the Code report describes were not independently re-run this round (A-4 note); PCs stay pending for SourceLanding Mutation and none is named on the card.

### (5) Regressions elsewhere in the diff

None found. The diff touches only the four files above; `check-evidence-diff.ps1` over the range: 3 commits, 0 entries, 0 violations. No production deleter of `AgentTaskLandNotifications` exists (retention only reads them), so the tracked-reload-of-a-deleted-row difference (A-5) is unreachable today.

## Verification this round

Checkpoint run `20261107-224922-b2c6` (plan extract `.antiphon/card-1759-review-plan.md`, ignored path; `--serial`; one isolated build `bin-c1759-rv1/`, removed by the green run):

    CHECKPOINT CP-1 commit=40cd61ba85cdafcad88f7a7406222b3ec0367b95 build=ok filter=/*/*/AgentTaskLandNotificationReconcileLoadTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-53194ebc/.antiphon/checkpoints/20261107-224922-b2c6/rows/CP-1/run.trx slot=granted waited=75s dirty=0 source=40cd61ba85cdafcad88f7a7406222b3ec0367b95 sourceState=clean buildSource=verified
    CHECKPOINT CP-2 commit=40cd61ba85cdafcad88f7a7406222b3ec0367b95 build=reused filter=/*/*/AgentTaskLandNotificationRecoveryTests/* executed=49 passed=49 failed=0 skipped=0 trx=/work/worktrees/task-53194ebc/.antiphon/checkpoints/20261107-224922-b2c6/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=40cd61ba85cdafcad88f7a7406222b3ec0367b95 sourceState=clean buildSource=verified
    CHECKPOINT CP-3 commit=40cd61ba85cdafcad88f7a7406222b3ec0367b95 build=reused filter=/*/*/AgentTaskLandNotificationPersistenceTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-53194ebc/.antiphon/checkpoints/20261107-224922-b2c6/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=40cd61ba85cdafcad88f7a7406222b3ec0367b95 sourceState=clean buildSource=verified
    CHECKPOINT CP-4 commit=40cd61ba85cdafcad88f7a7406222b3ec0367b95 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 trx=/work/worktrees/task-53194ebc/.antiphon/checkpoints/20261107-224922-b2c6/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=40cd61ba85cdafcad88f7a7406222b3ec0367b95 sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-1 waited=75s executed=49.365s wall=124.426s
    CHECKPOINT-TIMING CP-2 waited=0s executed=190.874s wall=190.939s
    CHECKPOINT-TIMING CP-3 waited=0s executed=97.623s wall=97.675s
    CHECKPOINT-TIMING CP-4 waited=0s executed=103.275s wall=103.306s

    CHECKPOINT SOURCE VALID source=40cd61ba85cdafcad88f7a7406222b3ec0367b95 rows=4

Touched classes 4 + 49 + 12 = 65 executed, 0 failed, 0 skipped (matches the Code report's 65); guard batch 75/75 (matches). Evidence-diff: `EVIDENCE result commits=3 entries=0 violations=0`. Merge-tree clean. Not run: the whole Unit lane (per-task Final does not own it, docs/testing-and-build.md "Per-task Final"; nightly and rc qualification do). No `### Checkpoints` table existed for the Code task (dispatched from the card), so CP-n matching is not applicable; the Code report lists each driver with its lease.

## Advisories (D-2: none holds the land)

- **A-1 Production shape: the due filter removes little.** Read-only census of the desktop dev database at review time: 9,041 land notifications, 1,696 non-terminal. Of those, about 1,540 are AwaitingReceipt rows with a queue message (kept by the new page: 1,500 whose destination session is still `Running`, oldest created 2026-09-17; 29 Stopped; 12 Failed), 13 are Canceled with a queue row (Canceled is not terminal, so they are rescanned every 5 s forever), and only about 142 are DestinationUnavailable backoff rows that the new predicate skips. So the page still loads roughly 1,550 notes per 5 s scan, each with a session scan, a queue-row read, a transcript catch-up and prompt scan, and an UPDATE (the concurrency token is renewed at line 77 and saved at line 393). The compiled lookups cut per-note overhead, but the 9 percent share the card measured is dominated by these long-lived AwaitingReceipt notes, not by query compilation. Suggested follow-up card: bound the rescan of AwaitingReceipt notes whose delivery attempt is exhausted or whose destination generation moved on (26 of them carry `notification_reconcile_failed:DbUpdateConcurrencyException`, 16 `completion_delivery_generation_changed`, 11 `completion_queue_identity_mismatch`), treat Canceled as terminal for the page, and re-measure.
- **A-2 Witness heuristic.** Recognizing a compiled query by the `@__id`/`@__now` parameter names versus the ad-hoc `@__id_0` suffix is EF-version behaviour. An EF upgrade can make the witness vacuous or red without a product change. Acceptable now; the class doc comment records the assumption. A sturdier signal would observe the query-cache path directly rather than SQL text.
- **A-3 Clock staleness accepted.** Bounded by one scan plus 5 s; producers deliver immediately through their own reconcile; the hosted page is a backstop. No change requested.
- **A-4 Mutant evidence.** The Code report describes five restored local mutants with outcomes but no run identities or counts. The red-first requirement is independently satisfied (4/4 red at `a186982424`); the mutants remain pending for the method-scoped SourceLanding Mutation.
- **A-5 Tracked reload of a deleted row.** `ReloadAsync` on a tracked instance whose row is gone detaches it silently where the old first `SingleAsync` threw. Unreachable: nothing deletes land notifications. Note only.

## Review evidence

--- review evidence ---
subjectTaskId: 5dd682f8-3daf-4f67-9c2c-972b33434837
reviewedSourceSha: 40cd61ba85cdafcad88f7a7406222b3ec0367b95
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 8b75f315467f7b25c021ec1c0299c04e7398c0b8..40cd61ba85cdafcad88f7a7406222b3ec0367b95
