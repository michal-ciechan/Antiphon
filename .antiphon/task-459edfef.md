# Review 459edfef: CARD-1709 round 1 of 3 (base 1ef88240b..7fdf784b8)

Subject: origin `feat/card-task-e056f0ff` (Code e056f0ff-fbe7-498e-a3e7-df5483c5b9bc). Tested SHA `7fdf784b8299dfa73c0d0f18f6eb8786037cd90c`; red `49ea6c3daf0779af28f3490a0ccca4d2370e623b`; report commit `ffa1546a2` on top (docs only).

**Verdict: clean.** No defects. I found no lost note, no reordered note, no swallowed busy exception and no hot loop. There are three advisories.

## Verified
1. **Nothing is lost or reordered.**
   - On a supervision busy skip, the pre-check runs `ChangeTracker.Clear()` before any save, so the notice keeps the State, NextAttemptAt, ReasonCode and QueueMessageId it was read with. It stays Pending and due, and the next pass picks it up.
   - On a completion busy skip, the task stays `CompletionNoteQueuedAt == null` and is found again by the next pass's owed walk.
   - A skip never writes Told or a terminal state. `ConfirmAsync` (UserPrompt receipt) is unchanged.
   - Per-session order holds when the lock frees mid-pass. The busy set is checked before the enqueue in both phases, and one set is shared across the supervision and completion phases for the whole pass.
2. **A timeout has no side effects.** At `SessionMessageQueueService.cs:689-692`, `WaitAsync(bound, ct)` returning false throws `SessionQueueBusyException` before the `try` block, so it never reaches `Release`. Cancellation also throws before the `try`. A timeout writes no row, attempt, ReasonCode or backoff. The completion busy catch never sets `failed`, so a busy pass does not throw, does not call `RequestSweep` and does not touch `failuresInARow`.
3. **The busy catch comes first.** In the supervision path, `catch (SessionQueueBusyException) when (busySessions is not null)` sits ahead of the general catch. In the completion path the busy catch is unfiltered and sits ahead of the general catch; that path always passes `lockWait`.
   - Callers that pass no `lockWait` keep the unbounded `sem.WaitAsync(ct)`.
   - `EnqueueSupervisionNoticeAsync` has one caller, `RecoverAsync`.
   - `RecoverAsync` has one caller, the hosted service, which now always passes a set (`RecoverMissingAsync` defaults to a new set).
   - The other uses of `TaskSupervisionNoticeService` (`CanSendAsync`, `CloseReportWatch`, `SourceCurrentAsync`, `Unresolved`) are static and unchanged.
4. **The 5 s retry is bounded.**
   - **Coalescing:** the retry is a single `nextSweep` DateTime folded with `Min`, so delayed sweeps cannot stack up. `WaitAsync` honours `ct`.
   - **Cadence:** after a sweep, `nextSweep` is reset to now + 15 min, then pulled down to now + 5 s only when the busy set is non-empty. The retry can never fire sooner than 5 s, and the 15-minute backstop is kept.
   - **No busy work:** a pass that skipped nothing leaves `nextSweep` unchanged. C1360 pins the 15 min startup timer.
   - **Lock held forever:** a pass runs every ~(5 s + 1 s per distinct busy session; the waits are sequential).
   - **Cost of each pass:**
     - Reads: the supervision due batch (Take 24) plus the missing-failure query, the owed-task page, the `RepairFromAsync` read, one `HasCompletionNoteAsync` per owed task, and the pending-queue keyset scan.
     - Writes: none for the busy session's items. `RepairFromAsync` writes only repairable rows, and that is idempotent.
     - Fan-out: one `ScheduleFlush` → `FlushIfIdleAsync` per session with a pending sourced note, now every ~6 s instead of every 15 min. This is coalesced per session by `_pending`.

## Runs (server2 Linux, one build-slot lease per build/run, UseAppHost=false, native output)
| Run | Tree | total | passed | failed | skipped |
|---|---|---|---|---|---|
| C1709_* | 7fdf784b8 | 4 | 4 | 0 | 0 |
| C1709_* red | 49ea6c3da server files | 4 | 0 | 4 (all TimeoutException) | 0 |
| G1: 13 service classes | 7fdf784b8 | 365 | 365 | 0 | 0 (9m18s) |
| G2: scanner group A, 10 classes incl. C1705_* | 7fdf784b8 | 251 | 251 | 0 | 0 (12m00s) |
| G3: scanner group B, 9 classes | 7fdf784b8 | 157 | 157 | 0 | 0 (15m25s) |
| M supskip: supervision busy skip deleted | env-gated mutant build | 4 | 3 | 1: C1709_Busy_recipient_keeps_supervision_notice_order ("no notice is enqueued in the pass where the first one timed out": 1 vs 0) | 0 |
| M compskip: completion busy skip deleted | same build | 4 | 3 | 1: C1709_Busy_caller_keeps_completion_note_order ("no A note is enqueued..." 1 vs 0) | 0 |
| M drop: retry removed | same build | 4 | 3 | 1: C1709_Busy_pass_retries (DueTimes.First 00:15:00, expected 00:00:05) | 0 |
| M zero: BusyRetryDelay = 0 (hot loop) | same build | 4 | 3 | 1: C1709_Busy_pass_retries (TimeoutException: no timer is ever armed) | 0 |
| M uncond: retry scheduled even when nothing was skipped | same build | 43 (TaskSupervisionReceipt + CompletionNoteDurableSweep + CompletionNoteWorkQueue) | 42 | 1: C1360_DurableSweepRecoversLostFlushOnIdleCaller (DueTimes [00:00:05], should contain 00:15:00) | 0 |

The class lists for G2/G3 are the 19 CARD-1705 classes from Review bdaa665c. Mutants were restored with `git checkout --`; the worktree is clean at 7fdf784b8, and bin/obj were deleted.

## Checks
- `check-evidence-diff` -BaseRef 1ef88240b -HeadRef 7fdf784b8: commits=3, entries=0, violations=0.
- `git merge-tree --write-tree origin/master(5a95ae7a5) 7fdf784b8`: clean (tree 6fce24936).
- **Not run:** the whole Unit lane (the operator accepted Interim scope for this master-only card). The checkpoint tool was not used because the brief carries no `### Checkpoints` table. ordinaryScopeCompleted: Interim.
- **PCs stay pending** for SourceLanding Mutation: the five mutants above, plus busy-catch-as-failure and separate sets per phase (from the Code report).

## Advisories
- **A-1 (supervision head-of-line, not a regression).** A busy-skipped notice keeps its NextAttemptAt. If a busy recipient owns the 24 oldest due notices, every pass reselects the same `Take(24)` batch. Other recipients' supervision notices then wait until that lock frees. Completion notes are not affected, because that walk pages through every owed task. The base was worse: the whole pass blocked. File a follow-up only if long-held locks prove realistic.
- **A-2 (witness strength).** The unconditional-retry mutant is caught only incidentally, by C1360's 15 min startup-timer assertion; no C1709 witness covers it. The zero-delay mutant is caught indirectly, by the retry witness's timer wait timing out. Both mutants do go red, so no witness is missing.
- **A-3 (receipt scope).** The C1709 witnesses stop at the queue row. Delivery from the queue to a UserPrompt receipt is unchanged and is witnessed by C1360, which checks for a complete UserPrompt match (green in G1).
