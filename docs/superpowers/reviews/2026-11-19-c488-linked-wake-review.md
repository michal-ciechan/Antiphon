# C488 linked unattempted queue wake: Final review (round 1)

Review task `a4c1842e-c392-46ff-907c-2efe1957de72`. Subject: Code task `a18cc863-80c3-4697-beb0-9354919cc84a`,
branch `feat/card-task-a18cc863`, range `42d7af1f33cfd0236f3bbcdcea57c60bf948e98d..0f5e0fa84474058000090e6f1467e455b3095cdb`
(8 commits). Card CARD-1514. Master `538c33f2d4374ea9b7afce7b33294c4bcb92e734`.

Verdict: **CLEAN**. No blocker. Three advisories below.

## Double delivery (judged first)

The new wake only calls `flushes.TryEnqueue(deliveredTo)`
(`server/Application/Services/AgentTaskLandNotificationService.cs:270-275`). It writes no queue row.
The claim is in the flush, under a per-session lock, in a fresh context:

- `SessionMessageQueueService` is a singleton (`server/Program.cs:779`). `FlushIfIdleAsync` takes
  `var sem = GetLock(sessionId); await sem.WaitAsync(ct);` and opens a new scope and `AppDbContext`
  (`SessionMessageQueueService.cs:2127-2135`).
- Under that lock it reads `m.Status == QueuedMessageStatus.Pending` rows (`:2609-2614`).
- It claims before typing: `m.Status = QueuedMessageStatus.Sent; ... m.DeliveryAttempts++; m.LastDeliveryStartedAt = now;`
  (`:2966-2969`), then `SaveChangesAsync`, inside one transaction with the frozen rendering for completion rows
  (`:2978-2989`). The comment there is "Stamped BEFORE a byte is typed".
- A second flush waits on the same semaphore and re-reads in a new context. It sees Sent and does not select the row.
- `CompletionNoteFlushQueue.TryEnqueue` dedupes per session through `_pending`, and `Complete` re-enqueues once
  (`CompletionNoteWork.cs:13-29`). The four `FlushAsync` consumers never flush one session at the same time.

The wake reads `row` before the flush runs, so its view can be stale. That does not matter, because the
flush re-reads the row under the lock. The producer's link-site wakes and the startup sweep already used
this path. The new wake adds no new claimant and no unlocked write. A Sent row is never woken, so the
CARD-1892 rule "never retype that Sent/null-verdict row" holds. No second-typing path was found.

## Wake rate and cost

The wake adds no query, because it reuses the `row` that the reconcile already loaded. The hosted
scan reconciles a linked AwaitingReceipt note only while CARD-1769 `RescanPark` has not parked it. The
park backoff is 1 minute and doubles to 5 minutes while the session and queue stamp do not change
(`AgentTaskLandNotificationHostedService.cs:379-398`). So a stuck Pending/0 row is woken at most once per
backoff, plus once when its stamp changes. That rate is bounded and is not a 5-second storm. Each wake
runs one coalesced `FlushIfIdleAsync`. On a Working caller that is spill reconcile, a writable check, a
working check and the next-hold query, then a return.

## Checkpoints (checkpoint tool, run `20261119-033934-1c86`, `--serial`, at the reviewed SHA)

| CP | executed | passed | failed | skipped |
|---|---|---|---|---|
| CP-1 LinkedUnattemptedQueueWakeTests | 4 | 4 | 0 | 0 |
| CP-2 recovery-core | 13 | 13 | 0 | 0 |
| CP-3 recovery-attempt | 17 | 17 | 0 | 0 |
| CP-4 recovery-legacy | 19 | 19 | 0 | 0 |
| CP-5 persistence-park | 22 | 22 | 0 | 0 |
| CP-6 durable-sweep | 1 | 1 | 0 | 0 |
| CP-7 C1892 claimed-attempt | 9 | 9 | 0 | 0 |
| CP-8 guard-union | 81 | 81 | 0 | 0 |
| CP-9 WindowsUnitPlatformAdmission | 6 | 6 | 0 | 1 (rostered `C1210_Windows_linux_only_outcomes_are_exact`) |
| CP-10 DerivedImpactIndexTests | 27 | 27 | 0 | 0 |

The run reported wall 18m49s, 1 build, `unlisted: none`, every row `dirty=0 sourceState=clean buildSource=verified slot=granted`.
`validate --rows CP-1..CP-8,CP-10` printed VALID rows=9. A whole-run validate is `row_failed`, but only
because of the plan-rostered CP-9 Linux skip.

Other checks:

- M-1: `dotnet build tests/Antiphon.E2E -p:UseAppHost=false` under build-slot gave 0 errors in 1m30s.
- `check-evidence-diff` over the range gave commits=8, violations=0.
- `git merge-tree --write-tree origin/master HEAD` was clean, with tree `105ea1e8c`. Master is 3 CARD-1968 build commits ahead, and they do not overlap this change.

## Floor and pin (count corrections, not weakening)

- The CP-8 floor went from 85 to 81 in `26a59253`. The 85 was this branch's own pre-execution estimate in
  `a6bb8e9c`. The range touches none of the nine guard classes, and the union ran 81/81 at the tip.
- The owed pin went from 16 to 17 in `0f5e0fa8`, with the index refresh in `f57beaeb`. The new class
  references `AgentTaskLandNotificationService`, so the class count went from 41 to 42 at the same cap of
  25, and the owed count went from 16 to 17. The other capped paths moved by +1 in the same way. CP-10 is green.

## Witnesses

- Red at base: the subject's own reconcile had no wake for an already-linked row, so `linked-pending`
  records no flush at `60665a34`, which matches V-1. `held` turns red if the `HoldUntil` clause is removed.
  `drop-wakeup` turns red if the `DropWakeup` clause is removed. `linked-pending` turns red if the wake is removed (PC-1).
- E2E `WaitForReviewReceiptAsync`: the catch handles the `TimeoutException` from `ProgressAwareWait`. The
  snapshot projects with `AsNoTracking`, returns null when there is no Review task, and catches every
  exception into `unavailable:`. No null-row exception path was found. The Windows lane was not run here
  (it is Windows-native). The producer-to-recipient UserPrompt witness is the Windows
  `C488_ReviewDeliveryCrashMatrix_Batch3`, and the rc qualification owns that run.

## Advisories (not blocking)

- A-1: In the `one-attempt` shape the row is `Sent` with 1 attempt, so the `Status == Pending` clause
  already excludes it. Removing only `DeliveryAttempts == 0` (plan PC-2) leaves every case green, so PC-2
  as written cannot go red. Fix: add a `Pending`, `DeliveryAttempts = 1` (below MaxDeliveryAttempts)
  shape and point PC-2 at it. This is not a double-typing path. Without the guard, the wake would only
  start a flush earlier for a row that the flush's own `DeliveryAttempts < MaxAttempts` retry policy
  already owns.
- A-2: Removing `linkedBeforeThisPass` survives. The result is a second `TryEnqueue` for a row linked in
  the same pass, which `_pending` coalesces, so it is harmless.
- A-3: `saved.DeliveryAttempts`/`Status` are constant in this harness. `RecordingFlush` has no
  consumer, so no flush runs. Only the flush-list assertion can go red, which means the message
  "must not retype" claims more than the assertion tests.
