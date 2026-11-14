# Run 34d41b7e, group 58: CodexCliObservationGapTests Windows failure

Task 71cec1b8 is a read-only extract from the desktop. It covers run `34d41b7e` on rc/2026-11-20
(`b559bfba2e9a420495122d27171352fe9b7e1ba4`), which was still running when this was written.
Sources, read-only, in
`C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\`:

- `antiphon-all-group-58.trx` (114336 B)
- `antiphon-all-group-58-tests.log`
- `antiphon-all-group-58.timing.json`
- `antiphon-all-group-58-execution\log_261114123302127.diag`

The test binary was built from `C:\Antiphon\releases\checkout-8e08368a`, whose HEAD is
`b559bfba2e9a420495122d27171352fe9b7e1ba4`, the same SHA as this worktree. No test was rerun,
because this task is read-only on the desktop while the qualification is live.

## Verdict

**Deterministic. This is a contract change, not load.** CARD-1892 (`5b80144d3`, "retain live
sessions and attempts after ambiguous Input") is on rc/2026-11-20 and is not on rc/2026-11-19.
It made an unacknowledged guarded Input count as *delivery unknown*: the queue row stays
`Sent` with a null verdict. Before CARD-1892 the row was reverted to `Pending`.
`C1029_Unobservable_timestamp_floor_is_original` deliberately loses the Input reply
(`HoldPrompts(w, loseInputReply: true)`). Line 359 then still asserts the pre-CARD-1892
outcome, `Pending`. The test is stale. The product follows the documented CARD-1892 contract
(`docs/session-runtime-invariants.md`).

## Failure

| Field | Value |
|---|---|
| Test | `Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original` |
| Outcome | Failed, 1 of 47 in the chunk (46 passed, 0 skipped) |
| Duration | 13.420 s (12:38:36.632 to 12:38:50.052 UTC) |
| Iteration | The first one: `kind=ClaudeCode`, `variant="older"`. Exactly one `C1029 DISPATCH` line was printed, so no later iteration started and the Codex variants never ran. |
| Assertion | `row.Status should be QueuedMessageStatus.Pending but was QueuedMessageStatus.Sent` |
| Expected / actual | `Pending` / `Sent` |
| Line | `CodexCliObservationGapTests.cs:359` (`row.Status.ShouldBe(QueuedMessageStatus.Pending);`), called from the loop body at :400 |

Stack top frames, from tests.log:

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: row.Status
    should be
QueuedMessageStatus.Pending
    but was
QueuedMessageStatus.Sent
  at Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original() in ...\tests\Antiphon.Tests\Application\CodexCliObservationGapTests.cs:359
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original() in ...\CodexCliObservationGapTests.cs:400
```

Standard output for the test, complete. The TRX has no stderr, and the diag holds nothing beyond
the same message:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.CodexCliObservationGapTests.1.1.C1029_Unobservable_timestamp_floor_is_original.1.1.0
C1029 DISPATCH task=682c29a0-7c2d-45e1-aa8d-a76daca816cc attempt=1 status=Dispatched session=c6f2c108-783f-4c19-b894-b93025170496
REMOTE_FIXTURE_REFUSAL operation=ConditionalInput code=phone_home_unsupported_target detail=C1029 lost reply after actual terminal submit
```

## Mechanism

The code is at b559bfba2. Every step below is deterministic, and none of them reads a clock
or a poll.

1. The test sets `HoldPrompts(w, loseInputReply: true)`, so the recipient's `RecordPrompt`
   throws `InvalidOperationException("C1029 lost reply after actual terminal submit")` *after*
   the terminal submit. That is `CodexCliObservationGapTests.cs:568-572`.
2. `World.CreateAsync` defaults to `remote: true` (`CodexCliDeliveryWorld.cs:50`), so the write
   travels as a phone-home `ConditionalInput`. `PhoneHomeCommandDispatcher` maps an
   `InvalidOperationException` to an Error frame with `phone_home_unsupported_target`, status
   400 (`PhoneHomeCommandDispatcher.cs:665-667`). This produces the `REMOTE_FIXTURE_REFUSAL` line
   above.
3. `PhoneHomeRunnerClient.SendConditionalInputAsync` turns any Error frame into
   `ConditionalInputOutcomes.Unknown` (`PhoneHomeRunnerClient.cs:613-614`).
4. The Claude guarded write calls `ProviderDialogObservationService.RequireWritten`. For
   `Unknown` it throws
   `IOException("Guarded input acknowledgement unavailable; submission is uncertain.")`
   (`ProviderDialogObservationService.cs:48-54`).
5. `InputDeliveryUncertainException.Is` returns true for `IOException`
   (`server/Application/Exceptions/InputDeliveryUncertainException.cs`).
6. The delivery claim in `SessionMessageQueueService` has already stamped
   `Status=Sent`, `DeliveryAttempts++` and `LastDeliveryStartedAt`. The CARD-1892 catch at
   `SessionMessageQueueService.cs:3005-3024` keeps that attempt ("Sent + null verdict is a
   persisted attempt awaiting evidence") and returns `Failed` without calling `RevertRunAsync`.
   The CARD-1892 card-launch catch in `AgentSessionService` (around line 336) also sets the
   row to `Sent` in the boot-write case.
7. The test reads the row after `JoinLaunchAsync` and gets `Sent`. It expected the old
   behaviour: the generic `catch (Exception)` → `RevertRunAsync` → `Pending`.

The diff `ab7af12c6990..b559bfba2` of `SessionMessageQueueService.cs` shows the change exactly.
The old `catch (OperationCanceledException) when (ct.IsCancellationRequested) { RevertRunAsync; throw; }`
became `catch (Exception ex) when (InputDeliveryUncertainException.Is(ex)) { ...retain...; return FlushResult.Failed; }`.
Before that change, the IOException fell into the generic catch, which reverted the row to
Pending.

## Deterministic, not timing or load

- The assertion checks returned state, a DB row read once. There is no poll expiry,
  `TimeoutException`, `OperationCanceledException` or sharing violation in the output.
- The phone-home Error frame came back as an ordinary reply, not a `RequestTimeout`. The
  ConditionalInput deadline is 60 s (`PhoneHomeLiveConnection.RequestTimeoutFor` default arm),
  and the whole failing iteration took 13.4 s. That is the same per-World cost as its passing
  siblings: `Unobservable_screen_retains_spill` ran 2 Worlds in 24.1 s.
- Classification is by exception type (`IOException`) at step 4, whatever the host load.
- The same test **passed** in group 58 of the previous rc run, 97150a0b
  (rc/2026-11-19 = `ab7af12c6990`). `git merge-base --is-ancestor 5b80144d3 ab7af12c6990`
  returns 1, so that rc lacks CARD-1892. rc/2026-11-20 is the first rc that contains it.
- Nothing else in the chunk ran beside it. The class is `[NotInParallel("MessageQueue")]` with
  `ParallelLimiter<ProcessSpawnLimit>`. Between 12:38:36.6 and 12:38:50.1 only this test ran in
  chunk 58: the previous sibling ended at 12:38:36.631, and the next test,
  `C1029_Post_input_crash_late_confirms_once`, started at 12:38:50.063. Other run chunks may have
  overlapped. They were not enumerated, because the cause does not depend on them.

## Chunk grouping and passing siblings

Chunk 58 had 47 tests in 6 min 27 s wall (12:33:02 to 12:39:29). It ran three classes:
AgentTaskLandTargetRaceTests (12, all passed), CodexCliObservationGapTests (7, 6 passed), and
CardRelationshipApiTests (28, all passed).

CodexCliObservationGapTests, in execution order:

| Test | Outcome | Duration | What it bounds |
|---|---|---|---|
| C1029_Durable_spill_survives_recreated_graph | Passed | 28.49 s | Spill and recreation path is fine when the Input reply arrives. |
| C1029_Per_kind_receipts | Passed | 212.24 s | Grok, Claude and Codex receipts with acknowledged Input. All vectors `receipt=complete submits=1`. |
| C1029_Enqueue_fault_and_retry_keep_identity | Passed | 23.27 s | Storage faults (not Input-reply loss) still revert and retry. |
| C1029_Old_generation_does_not_confirm | Passed | 34.68 s | Prior-generation floor; Input acknowledged. |
| C1029_Unobservable_screen_retains_spill | Passed | 24.13 s | **Same unobservable baseline, Claude then Codex, but the Input reply is NOT lost.** Claude gets `Delivered` and Codex stays `Pending`. Only the lost reply differs from the failing test. |
| **C1029_Unobservable_timestamp_floor_is_original** | **Failed** | 13.42 s | Lost Input reply (ClaudeCode, "older"). |
| C1029_Post_input_crash_late_confirms_once | Passed | 14.88 s | Post-input *storage* fault; Input acknowledged; late confirm once. |

The only test in the class that loses the Input reply is the one that failed. Every sibling that
gets an Input reply passed. That bounds the cause to how a lost guarded-Input reply is
classified.

## Fix direction for Debug

The fix belongs in the test; the product is behaving as specified. Rewrite the
`loseInputReply` expectation at `CodexCliObservationGapTests.cs:359` and its follow-on steps for
the CARD-1892 contract:

- After a lost guarded-Input reply the row is `Sent`, with a null `DeliveryVerdict`,
  `DeliveryAttempts == 1`, `LastDeliveryStartedAt` set, and the baseline still null for this
  unobservable case.
- Keep the floor and late-confirm assertions: the original `LastDeliveryStartedAt` survives
  recreation (pc-246), there is no false settle for "older" and "null", and the original floor
  is kept after settlement.
- The ClaudeCode "older"/"null" branch (`w.Fault.RejectRetry`, expecting a throwing
  `FlushAsync`, at :380-385) assumed a Pending row that would be retried. Under CARD-1892 a
  Claude unknown attempt goes through the fresh-attempt reconciliation, including
  `retainOnClear`. That branch needs re-deriving, not loosening.

The Codex variants never ran in this failure, so their outcome on b559bfba2 is unverified.
Remote Codex body writes may use plain `Input`, where an Error frame becomes a
`ConflictException` and the row reverts to Pending. The `IOException` path may still be specific
to Claude's guarded write. Debug should run the whole method, both kinds and all four variants,
red first.

Other C1029 tests that call `HoldPrompts(..., loseInputReply: true)`: only this method does,
according to grep.

## Why missed

- **CARD-1892 Code and Review selections** (`docs/superpowers/plans/2026-11-13-card-1892-*.md`).
  They ran closed checkpoint rows: C1677RetentionDispatchTests, PhoneHomeCommandDispatcherTests,
  C1722GitTimeoutBudgetTests, GrokRules*, PhoneHomeLaunchTransportTests,
  SessionMessageQueueInterruptedAttemptTests, RunnerTerminalSession*, AgentSessionLaunchFailureTests,
  SessionMessageQueuePhoneHomeDropTests, attention classes and SessionMessageQueueCodexPointerTests.
  CodexCliObservationGapTests is not among them. The `tests/impacted-classes.json` glob matched
  only `test-lane-category`. The evidence states that the wider derived closure (100/101 classes)
  is "not represented as passed".
- **The class is in that derived closure.** `tests/derived-impact-index.json` lists it under
  `server/Application/Services/GrokRulesRefreshService.cs`, a file 5b80144d3 touched. It also
  appears under TypedBodySpill.cs and PtyDeliveryCeilings.cs. `SessionMessageQueueService.cs`
  is a capped path (150 classes, 125 owed), so a class reached only through the queue service
  is not even listed. Running the derived closure of the CARD-1892 change would have selected this
  class.
- **Merges 212-214**, the batch that carried CARD-1892 onto master (`1fde603cc chore(merge-213)`),
  ran selected classes only. The sibling write-up
  `docs/investigations/2026-11-14-session-runtime-action-delivery-fixture-debug.md` records
  that their selections missed CARD-1892 recovery interactions. This extract cannot see Merge
  213's exact filter from local data, so whether this class was in it is **unverified**. Local
  records give no indication that it was.
- **CARD-1912/1913.** Per the caller, Code round 2 (c995680c) is running the Codex observation
  classes now, which is after the rc cut. If its tree contains CARD-1892, the ClaudeCode/"older"
  iteration should fail there the same way. Treat a red there as this inherited contract
  mismatch, not as a CARD-1912/1913 regression.
