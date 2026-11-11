# Run d88de419 group 60: TurnEndGapRestartDeliveryTests render-committed red (Windows)

Run d88de419 (desktop, Windows), rc/2026-11-16 = `6f8741d872d63507827014f972ccf71196722d4a`.
Chunk `antiphon-all-group-60`: 101 tests, 99 passed, 2 failed, wall 7m 32s
(16:26:28Z to 16:34:02Z, 2026-11-11). Source files read only:
`antiphon-all-group-60.trx`, `antiphon-all-group-60-tests.log`,
`antiphon-all-group-60.timing.json`, `antiphon-all-group-60-execution/log_261111162628783.diag`.

## Verdict

**Real regression in this cut. It is deterministic, not a load flake and not the environment.**
CARD-1505 (`5aaae94e6`, a cherry-pick of master `7c6af459f`) is new in this cut. It is not in
`ac6d00022`. It routed interrupted `Sent` recovery through `ClassifyTypedComposerAsync`. For a
readable, empty Claude box, that path waits `PostEvidenceSettleMs` (default 500 ms) on the
injected `TimeProvider`. In this harness that provider is the manual `ControlledTimeProvider`.
`TurnEndGapDeliveryHarness.JoinDeliveryAsync` only advances 20 ms and 50 ms timers. The 500 ms
`Task.Delay` therefore never fires. `FlushStrandedQueuesAsync` never returns. The harness's
90-second `CancellationTokenSource` then cancels the wait.

Only the `render-committed` cut reaches this path: its row is `Sent` with one attempt, no
submission, and an empty composer. Both `busy` values failed, and each took exactly the 90-second
harness budget (1m 30.059s and 1m 30.047s).

Master (`17ebc0a32`) carries the same product change and the same harness, so the test is red
there too. The failure does not depend on Windows. The red needs no PTY or OS-specific behaviour.

## Failed tests

Both have the same message and stack. The only difference is the `busy` argument.

| Test | Duration | Window (UTC) |
|---|---|---|
| `C1205_Restart_recovers_each_delivery_handoff(render-committed, False)` | 1m 30s 058ms | 16:30:22.705 to 16:31:52.763 |
| `C1205_Restart_recovers_each_delivery_handoff(render-committed, True)` | 1m 30s 047ms | 16:32:20.767 to 16:33:50.814 |

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TaskCanceledException: A task was canceled.
  at Antiphon.Tests.TestHelpers.ControlledTimeProvider.WaitForTimerAsync(...)   ControlledTimeProvider.cs:55
  at Antiphon.Tests.TestHelpers.TurnEndGapDeliveryHarness.JoinDeliveryAsync(Task operation)   TurnEndGapDeliveryHarness.cs:334
  at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff(String cut, Boolean busy)   TurnEndGapRestartDeliveryTests.cs:316
  at ... TurnEndGapRestartDeliveryTests.cs:336
Standard output: ANTIPHON_TEST_PHASE_ID ...C1205_Restart_recovers_each_delivery_handoff(System.String, System.Boolean).10.1.0   (9.1.0 for True)
```

- **Expected vs actual:** there is no Shouldly assertion. The test expects
  `JoinDeliveryAsync(h.Queue.FlushStrandedQueuesAsync(h.Token))` to complete. Line 316 is that
  call, after `h.Clock.Advance(37s)`. Instead, `JoinDeliveryAsync` waited at line 334
  (`await timer`) for a 20 ms or 50 ms timer that was never created. The flush was parked inside
  `ComposerStaysEmptyAsync` → `Task.Delay(500ms, _timeProvider)` (`SessionMessageQueueService.Expectations.cs:359-366`).
- **Concurrency:** no test from any other class overlapped either window. The TRX start and end
  times show that the chunk ran these tests one at a time. There was no lock or sharing
  violation, and no stderr.
- **Logs:** the diag file has only TUnit node-state updates for these tests. Before this fix,
  the harness's `C1205 recipient diagnostic` dump existed only in `AssertReceiptsAsync`, which
  the test never reached, so no harness log was printed.

## Siblings that passed (they bound the cause)

`TurnEndGapRestartDeliveryTests`: 29 passed, 2 failed. Every other cut of the same theory passed,
both `busy` values each:

- `obligation-insert`, `settled-committed`, `note-insert` and `note-committed`. These settlement
  cuts never enter `RecoverDeliveryRunLockedAsync`.
- `attempt-committed` and `receipt-before-save`. These have a submission and a transcript
  receipt, so recovery confirms from the transcript before any composer read.
- `wakeup-full`.

Other passing methods in the class:

- `C1205_Restart_delivers_five_gap_held_completions_once(True/False)`
- `C1205_Restart_recovery_worker_executes_queue_cycle`
- `C1205_Watchdog_delivers_while_event_stream_remains_healthy`
- `C1205_Restart_after_transcript_commit_recovers_lost_wakeup`
- `C1205_Receipt_restart_confirms_without_retyping`
- `C1205_Unknown_probe_keeps_obligations_held(wrong-generation/pull-fault)`
- `C1205_Flush_rechecks_a_new_turn_after_recovery`
- `C1205_Busy_tail_keeps_completion_obligations_pending`
- `C1205_Already_eligible_caller_receives_completion`
- `C1205_Only_complete_destination_receipts_confirm` (below-floor, wrong-destination,
  truncated, header-only)

Other classes in the chunk all passed: `WorktreeRetirementRaceTests` 54, `AgentTaskLivenessTests` 6,
`BlockedTaskSyncRecoveryTests` 5 and `LandReplacementApprovalTests` 5.

Taken together, the only cut that fails is the one whose stranded row has a typed attempt, no
transcript evidence, and a readable empty Claude box. That is exactly the case where CARD-1505
added the settle read.

## Prior run and source delta

- Run 6591e378 (final-result note `7a3c45c3f` on `origin/feat/card-task-d6579463`): the note
  lists `antiphon-all-group-60` under "Appendix B. Chunks that can be seeded as passed". The
  string `TurnEndGap` does not appear anywhere in the note, so the class was not a failure there.
  That run used rc `ac6d00022`, which does not contain CARD-1505.
- `ac6d00022..6f8741d87`: the harness, the test and `ControlledTimeProvider` did not change. The
  only edit to `C544World.cs` is a `ServiceGraph` capture, which is not involved. The changed
  sources are `server/Application/Services/SessionMessageQueueService.cs` and
  `SessionMessageQueueService.Expectations.cs`, from `5aaae94e6 fix(CARD-1505): read a collapsed
  paste as still in the composer`. Before that commit, `RecoverDeliveryRunLockedAsync` read one
  snapshot. A head that was not visible went straight back to Pending, with no timer. After it,
  `ClassifyTypedComposerAsync` returns `Clear` only after two empty reads `PostEvidenceSettleMs`
  apart.
- This is not a renumbering: the class and chunk are the same, and there is a new code path.

## Fix (this branch)

`tests/Antiphon.Tests/TestHelpers/TurnEndGapDeliveryHarness.cs`, `JoinDeliveryAsync`:

1. The delivery join now also advances the composer settle timer. Its due time is read from the
   world's `IOptions<SupervisionSettings>` and clamped to 0..3000 ms, the same as the product
   code. It is not a hard-coded 500. The product's double-read is unchanged and is still
   exercised. The test's `expectedAttempts: 2` for `render-committed` (revert to Pending, then
   retype) still holds, because two empty reads give `Clear`. No assertion was weakened, and no
   budget was widened.
2. Failure-path diagnostics: if the harness budget expires while the operation is still running,
   the join throws a `TimeoutException`. It names the clock time, the due times the join
   advances, and every live controlled timer (id, state, due, period, deadline). The bare
   `TaskCanceledException` is wrapped as the inner exception. The next unadvanced timer is
   therefore named in the TRX instead of reading as a cancel.

No product change: the CARD-1505 settle is intended behaviour (CARD-0299: one empty snapshot is
not evidence).

## Why it was missed before the rc run

1. The CARD-1505 Code and Review checkpoints (review `321278967`, run `20261110-152955-2552`) had
   no plan `### Checkpoints` table. CP-1 ran only `SessionMessageQueueWedgedHeadTests`,
   `SessionMessageQueueInterruptedAttemptTests` and `ExpectationDirectDeliveryTests`, 91/91.
   CP-2 was the guard union.
2. The derived impact index caps both changed files at 25 of 150 classes:
   `SessionMessageQueueService.cs` and `.Expectations.cs` have `reason=cap`, `owed=125`.
   `TurnEndGapRestartDeliveryTests` is in the owed tail. It reaches the queue service only
   through `TurnEndGapDeliveryHarness` and DI. `impacted-classes` on the CARD-1505 range selected
   only `TestLaneCategoryGuardTests`.
3. Harness gap: `JoinDeliveryAsync` modelled a fixed set of delivery timers (20 ms and 50 ms), so
   any new `TimeProvider` delay on the delivery path hangs silently until the 90-second budget
   runs out. The new diagnostic names such a timer next time.

Follow-up worth a card (not done here): CARD-1505 also routes Pending retry
(`SessionMessageQueueService.cs` around line 2741) and the F1 hold through the same settle read.
Other harnesses on a manual clock may join delivery with the same fixed set of timers. These are
`PermissionWaitFixture.JoinDeliveryAsync` and any `C544World` consumer that advances specific due
times only. They are worth a sweep for the same hang. None failed in group 60.

## Proof owed

- No build or test was run from this task. The brief made the desktop read-only while run
  d88de419 is live, and `Antiphon.Tests` needs the Docker/Postgres fixture, which this task must
  not touch.
- Compile, and run the red-then-green proof on Linux (server2). The red needs no Windows:
  `--treenode-filter "/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_recovers_each_delivery_handoff*"`.
  At `6f8741d87` without the fix, expect 2 red (render-committed). With the fix, expect 16/16
  green. Then run the whole `TurnEndGapRestartDeliveryTests` class, 31 rows.
- Native Windows proof is owed in the next rc run, group 60.
- The fix must go to both rc and master, because master has `5aaae94e6`.
