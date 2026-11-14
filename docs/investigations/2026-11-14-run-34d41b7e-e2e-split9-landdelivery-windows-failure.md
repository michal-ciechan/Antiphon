# Run 34d41b7e, e2e all-split-9: C488 Review delivery crash matrix (before-typing) on Windows

- Run: `34d41b7e` (Windows Full qualification, desktop), rc/2026-11-20 = `b559bfba2e9a420495122d27171352fe9b7e1ba4`
- Log dir (read-only): `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\`
- Files read: `e2e-all-split-9.trx`, `e2e-all-split-9-tests.log`, `e2e-all-split-9.timing.json`,
  `e2e-all-split-9-selection.discovery.json`, `e2e-all-split-9-execution\log_261114232552910.diag`
- No screenshots, traces or Playwright artefacts exist for this chunk. The execution folder holds only the TUnit `.diag` file. The fixture `Root`, its barrier files and the native `updates.jsonl` were temp files and were deleted at teardown.

## Verdict

**The failure is deterministic. It is the CARD-1892 pattern, through the composer-less hold that CARD-1892 added, not through the exception path.**
On a Grok caller session, a server crash at `queue-before-typing` now leaves the land-notification
queue row as `Sent` with a null verdict and `DeliveryAttempts = 1`. CARD-1892's
`HoldComposerlessUnknownLockedAsync` holds that row for good. It never retypes it and never reverts it to
Pending. No byte was typed, so no `UserPrompt` can ever late-confirm it. The note's `ConfirmedAt` stays null,
and the test's 180 s wait for the receipt expires. A longer wait would not help.

## Failing test

| Field | Value |
|---|---|
| Test | `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch4(before-typing)` |
| Outcome | Failed |
| Duration | 00:05:45.5649796 (start 23:29:07.826Z, end 23:34:53.391Z, 2026-11-14) |
| Message | `TimeoutException: complete native Review receipt` |
| Expected | `AgentTaskLandNotifications` row for the Review task, `Kind = TaskCompletion`, `ConfirmedAt != null`, within 180 s |
| Actual | No such row within 180 s (plain wait, no progress probe; the message has no `(quiet …)` suffix) |

Stack top frames, from the TRX:

```
at Antiphon.TestSupport.ProgressAwareWait.UntilAsync(...) in tests\Shared\ProgressAwareWait.cs:line 36
at Antiphon.E2E.Fixtures.LandDeliveryFixture.WaitForReviewReceiptAsync(String form, Int32 seconds) in tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.Review.cs:line 107
at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(String cut) in tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 451
```

Line 451 is `var receipt = await f.WaitForReviewReceiptAsync();`, which runs after the kill and restart. The earlier steps all
passed. Each has its own 180 s wait and a distinct timeout message:
- `StartReviewDelegateAsync`: "review provider held the marked prompt"
- `AssertReviewHeldAsync("before-typing")`: the `queue-before-typing` barrier was reached. It also asserted `DeliveryAttempts > 0`,
  `ConfirmedAt == null`, and no `review-evidence=` UserPrompt in the caller transcript.
- `KillChildAsync`, then `UseChildAsync("none")`

So the row was demonstrably `Sent` with `DeliveryAttempts >= 1` and nothing typed when the server child was killed.

Stderr: none for the failed test. The passing sibling's stderr has only
`[TUnit] External span cap of 100 reached; subsequent spans will be dropped.`
Stdout: Testcontainers Postgres start, migrations, and the usual benign `__EFMigrationsHistory` ERR on first boot.
After that it is OpenTelemetry HTTP client spans only. The 100-span cap is hit about 6 s into the test (the last span is at 23:29:23.249Z),
so nothing from the kill, restart or wait window was logged. The test does not emit server log lines from the child.
In that first window: `POST /sessions` 201, two `POST /sessions/{caller}/input` 204 (fixture boot/caller setup), all on the isolated runner.
No errors, refusals or timeouts.

## What ran at the same time

The chunk ran these 2 tests sequentially (timing.json `testCount: 2`, run wall 541 s, build 583 s, no build-slot wait):

| Test | Outcome | Duration |
|---|---|---|
| `C488_ReviewDeliveryCrashMatrix_Batch4(idle-lost-wakeup)` | Passed | 00:03:14.40 (23:25:53 to 23:29:07) |
| `C488_ReviewDeliveryCrashMatrix_Batch4(before-typing)` | **Failed** | 00:05:45.56 (23:29:07 to 23:34:53) |

The chunk itself has no host-load evidence: no host-stats spans after the cap, no sharing violations, no IO errors.
The pre-wait phases took about 166 s (345 s minus the 180 s final wait), which is in line with this cut's history.

## Sibling tests (same class, this run): all passed

| Chunk | Test | Duration |
|---|---|---|
| split-1 | `C467_V29_RealOutcomeProducerMatrix_Batch1(landed)`, `(already-present)` | 68.7 s, 41.5 s |
| split-2 | `C467_V29_..._Batch2(residue-cleanup)`, `(preoperation-refusal)` | 92.1 s, 37.0 s |
| split-3 | `C467_V29_..._Batch3(operation-refusal)`, `(conflict)` | 66.9 s, 36.6 s |
| split-4 | `C467_V30_ReceiptSaveFailureNeverRetypes(receipt)`, `(verdict)` | 89.5 s, 60.6 s |
| split-5 | `C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy)`, `(attempt)` | 87.8 s, 64.8 s |
| split-6 | `C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-save)`, `(settlement-before-commit)` | 201 s, 182 s |
| split-7 | `C488_..._Batch2(enqueue-exception)`, `(queue-before-commit)` | 218 s, 182 s |
| split-8 | `C488_..._Batch3(queue-inserted-before-outbox-link)`, `(queue-committed-before-wakeup)` | 204 s, 178 s |
| split-9 | `C488_..._Batch4(idle-lost-wakeup)` | 194 s |

These passes bound the cause:
- **`C467_V32(attempt)` passed.** It uses the same `queue-before-typing` barrier (fixture cut `attempt`), but it *releases* the
  barrier instead of killing the server, so typing goes ahead and a UserPrompt confirms the row.
  The barrier, the Grok caller and the receipt path all work. Only *kill while Sent and untyped* fails.
- Every other crash cut kills the server before any attempt is charged (settlement, enqueue, queue insert, before
  wakeup), so the row is still Pending with `DeliveryAttempts = 0`. That is outside `ComposerlessUnknownAttempts`, and those cuts recover normally.
- `idle-lost-wakeup` never stamps an attempt before the wakeup is lost, so it is likewise unaffected.
- `C467_V30(verdict)` / `prompt-before-verdict`-style cuts *did* type, so a UserPrompt exists and the composer-less hold late-confirms.

## History: did it pass on rc/2026-11-19 (run 97150a0b)?

Yes. `before-typing` passed in every prior Windows Full run, and this is its first failure:

| Run | rc | before-typing | idle-lost-wakeup |
|---|---|---|---|
| a6410818 | 2026-11-12 | Passed 5:14.7 | Passed 3:36.0 |
| dce78ee0 | 2026-11-14 | Passed 5:03.7 | Passed 3:16.3 |
| 6591e378 | 2026-11-15 | Passed 5:21.3 | Passed 3:53.3 |
| ae84478d | 2026-11-17 | Passed 4:57.5 | Passed 3:15.3 |
| 0b292fa4 | 2026-11-18 | Passed 5:02.9 | Passed 3:17.9 |
| 97150a0b | 2026-11-19 (`ab7af12c6990`) | **Passed 5:15.9** | Passed 3:44.6 |
| 34d41b7e | 2026-11-20 (`b559bfba2`) | **Failed 5:45.6** | Passed 3:14.4 |

In earlier runs the case passed in about 300 to 320 s total. That fits the old recovery path: after `InterruptedAttemptAge`, the interrupted Sent
run was classified, the composer was Clear, the run reverted to Pending and was retyped. This time the run hit 180 s with no receipt.

`git diff --stat ab7af12c6990 b559bfba2 -- tests/Antiphon.E2E tests/Shared` is **empty**: the test and fixture are unchanged.
`ab7af12c6990..b559bfba2` (327 commits) brings in all of CARD-1892 (`5b80144d3`, `282999c2c`, `4e4362bec`,
`4edbb5a2d`, `ecc9eb971`, `dcb218bf7`, ...) and CARD-1916 `36c999076` ("assert ambiguous Grok hold and late
confirmation"). None of these are in rc/2026-11-19.

## Mechanism (production code at b559bfba2)

1. `SessionMessageQueueService` (about lines 2950-2992): before any byte is typed, the run is stamped
   `Status = Sent`, `DeliveryAttempts++`, `LastDeliveryStartedAt = now`, `ClearAttemptVerdict` (null verdict). Then
   `landBoundary.ReachedAsync("queue-before-typing", ...)` runs, which is where the fixture's `attempt` cut blocks and the test kills the child.
   The original comment still says: "A crash between here and the write costs one attempt, which is the safe
   direction to be wrong in."
2. The caller session in `LandDeliveryFixture` is `AgentKind.Grok` (`LandDeliveryFixture.cs:111`).
3. CARD-1892 `ecc9eb971` added `ComposerlessUnknownAttempts` (line 3511):
   `Status == Sent && DeliveryVerdict == null && DeliveryAttempts > 0 && MaintenanceKind == None` on a Running session
   whose kind is neither ClaudeCode nor Codex. That matches this row exactly.
4. Both the flush path (line 2553, ahead of the fresh/interrupted recovery) and `FlushStrandedQueuesAsync` call
   `HoldComposerlessUnknownLockedAsync`, which:
   - catches up the transcript and late-confirms against a matching UserPrompt (there is none: nothing was typed);
   - after `InterruptedAttemptAge` records an `input-delivery-unknown` incident (`DeliveryTransportFailed`, Error,
     "orchestrator decision required ... No automatic Enter, retype, or process stop is authorized");
   - returns non-null `FlushResult.Nothing`, so the old interrupted-run recovery (classify the composer, then Clear, revert, retype)
     **never runs** for this session.
5. No API in the rc resolves a held unknown Input. The only references to `input-delivery-unknown` are the
   incident writer and `AttentionService.cs:2181`. So the receipt can never complete without operator action, and the outcome is deterministic.

This is the same CARD-1892 family as g54 `LandRefusalDeliveryWitnessTests` C1196 (repair `8cec1e74e`, not in
b559bfba2), but the trigger is different. g54 threw an `IOException` at the boundary, which `InputDeliveryUncertainException.Is`
classifies as ambiguous. Here it is a **process kill** after the attempt was stamped, and the new composer-less hold
picks the attempt up on restart. The g54 trick of making the seam throw a definitive 409 refusal does not apply: there is no throw, just a kill.

## Repair options (Debug to choose; the second is a product/design call)

- **Test-side: adopt the CARD-1892 contract.** For `before-typing` on a Grok caller, assert the new behaviour instead of a
  receipt:
  - the row stays `Sent`, null verdict, `DeliveryAttempts == 1`;
  - no `review-evidence=` UserPrompt and no second attempt are charged;
  - after `InterruptedAttemptAge` (`LandDeliveryFixture.InterruptedAttemptAge`, already computed in the fixture) exactly one
    `AgentIncidents` row with `FailureReason == "input-delivery-unknown"` exists for `CallerId`;
  - the note's `ConfirmedAt` is still null.

  To keep the "lands once after recovery" coverage, the harness needs a driver for explicit recovery. Today there is no route, so either
  the test writes the operator-equivalent DB repair (reverting the row to Pending, which documents the gap), or that leg moves to a
  ClaudeCode/Codex caller. Those kinds keep the composer-reader recovery, but the fixture's native Grok `updates.jsonl` receipt
  check would need a per-kind variant. `C540_PreTypingCrashRecoversWarnings` needs the same treatment.
- **Product-side: narrow the hold to an actual send.** The stamp-before-typing attempt is not ambiguous until an Input
  request has actually been issued. If the server persisted an "input issued" marker (or the hold required evidence that
  `DeliverAsync` started), a crash before the write could keep the old revert-and-retype recovery. That would preserve the
  documented "costs one attempt, safe direction" contract. As it stands, any server crash or restart in that window on a Grok, Grok-native or other composer-less caller
  strands a land notification until an operator acts. That is a real-world regression candidate, not only a test artefact.

## Expected sibling impact in this run (not yet executed when read)

- `DispatchBaseWarningDeliveryE2ETests.C540_PreTypingCrashRecoversWarnings` (`CrashAsync("attempt", "queue-before-typing")`,
  same Grok `LandDeliveryFixture`, kill at the same boundary). It passed in 97150a0b (`e2e-all-split-54`, 3:20.4) and is
  **expected to fail the same way**.
- `C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict)` and `C540_PostPromptCrashDoesNotRetype` typed before the
  kill, so a UserPrompt exists. The composer-less hold should late-confirm them, and they are expected to pass.
- `C494_ReviewReceiptRejectsFalseEvidence_*` uses cut `attempt` but *releases* the boundary, so it is expected to pass.

## Why it was missed before the rc run

CARD-1892's checkpoints were unit/integration rows (`SessionMessageQueueInterruptedAttemptTests`,
`LandRefusalDeliveryWitnessTests`, CARD-1916 Grok hold tests). The E2E crash matrices that kill the server between the attempt stamp
and the write (`C488 before-typing`, `C540 PreTyping`) are OptIn, multi-minute E2E rows. Checked at b559bfba2,
`tests/impacted-classes.json` has exactly one row naming `AgentTaskLandDeliveryE2ETests`. Its globs cover only the land-notification sources
(`AgentTaskLandNotificationHostedService.cs`, `AgentTaskLandNotification*.cs`, `LandNotificationReconcileQueries.cs`,
`AgentTaskLandMonitorService.cs`), with `"windows": "only"`. No row anywhere matches `SessionMessageQueueService` (0 hits). So a change
confined to `SessionMessageQueueService.cs` never selects this class, and its reliance on the old "crash before write reverts and
retypes" path for a Grok caller went unexercised until the Windows Full run. Follow-up: add
`server/Application/Services/SessionMessageQueueService.cs` to that row (and to the C540 class's row) as part of the repair.
