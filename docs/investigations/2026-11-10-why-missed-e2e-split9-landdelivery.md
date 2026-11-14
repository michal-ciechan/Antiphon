# Why missed: e2e-split9 C488 before-typing crash still expected an automatic retype

rc qualification run 34d41b7e, rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4,
chunk e2e-all-split-9 (Windows native desktop):
`AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch4(before-typing)` failed
after 5m 45.6s with `TimeoutException: complete native Review receipt`
(`LandDeliveryFixture.WaitForReviewReceiptAsync`, 180 s `ProgressAwareWait`). Its sibling
`Batch4(idle-lost-wakeup)` passed in the same chunk (3m 14s). Batches 1-3 passed in
e2e-all-split-6/7/8. The same case passed in the previous run 97150a0b (rc/2026-11-19,
5m 15.9s), which did not contain the CARD-1892 composer-less hold.

Classification: TEST FIXTURE DEFECT (stale pre-CARD-1892 expectation). Deterministic, not
timing or host load, not a Windows product defect, not an expected Windows skip. The E2E
land-delivery fixture only runs on Windows, so the Windows qualification found it first.

## Cause

The `before-typing` cut runs the owned child host with `cut=attempt`. The child claims the
review-completion queue row (Sent, `DeliveryAttempts++`, `LastDeliveryStartedAt` stamped,
null verdict), commits it, then blocks at the `queue-before-typing` LandDeliveryBoundary
inside `SessionMessageQueueService.FlushLockedAsync`. The test kills the child there and starts
a `none` child, then waits for the receipt of a retyped delivery.

CARD-1892 commit ecc9eb971 (`hold composer-less unknown input for transcript or decision`,
cherry-pick of 0c90d5f8d) added `ComposerlessUnknownAttempts` /
`HoldComposerlessUnknownLockedAsync`: for a Running session whose kind is not ClaudeCode or
Codex, a Sent/null-verdict charged attempt is held for a complete UserPrompt or an orchestrator
decision. The flush path and the stranded-queue watchdog return before any Enter, retype or
attempt charge, and after `InterruptedAttemptAge` record a durable `DeliveryTransportFailed`
incident with `FailureReason = input-delivery-unknown`
(docs/session-runtime-invariants.md). The fixture's caller (`C467 owned caller`) is a Grok
agent, so the killed pre-typing attempt is held forever and no receipt can arrive. The host
cannot tell a crash before the first typed byte from a crash after typing, so the hold is the
contract, not a defect.

This is the same CARD-1892 contract change as g54 (C1196, repaired by 8cec1e74e), but a
different mechanism. g54 threw an `IOException` that a definitive refusal could replace. Here
the cut is a process kill, which is inherently ambiguous, so no seam change can keep the old
retype. `DispatchBaseWarningDeliveryE2ETests.C540_PreTypingCrashRecoversWarnings` uses the
same `attempt` cut, kill and Grok caller, and would fail the same way. It had not run yet in
34d41b7e.

## Repair (test side)

- `LandDeliveryFixture.WaitForInterruptedDispatchEligibilityAsync(Guid? sourceTaskId)`
  now takes the source task, so the Review cut uses the same real-time eligibility wait
  (attempt start + InterruptedAttemptAge, with timestamps unedited) while the child is dead.
- New `LandDeliveryFixture.AssertUnknownPreTypingAttemptHeldAsync(sourceTaskId, marker)`
  waits up to 150 s after eligibility (two 60 s watchdog periods plus warm-up, the
  `WaitForLateConfirmedInterruptedRowsAsync` window) for the restarted host's
  `input-delivery-unknown` incident on the caller session. It then asserts that each
  attempted row is still Sent, has a null verdict, and has an unchanged attempt count and
  start, that its notification is unconfirmed, and that no caller UserPrompt or native
  `user_message_chunk` carries the marker.
- C488 `before-typing` calls these helpers plus `AssertReviewSettledOnceHeldAsync`: the
  Review settled once, with one clean StageOutcome, one notification and one linked queue
  row. C540 `attempt` calls the hold helper. No timeout was widened. The pre-crash
  assertions of both tests are unchanged. Method names are kept so rosters stay stable.

## Introducing commit/card

introducing commit/card: ecc9eb97130680c19e634f2a6ce236e61991cd3a (CARD-1892 round 2,
cherry-pick of 0c90d5f8dedbf18684fddb9e6c90ab03cf3e3bd8), with the CARD-1892 classifier
5b80144d3. The stale expectation dates from CARD-0488 (C488 crash matrix) and CARD-0540, which
were correct under the earlier revert/retype recovery.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- d552541f-0d4b-42be-9386-7c2f8f495a38 (CARD-1892 round 1) and
  caeec914-1116-45ef-b19e-5e087159296f (round 2, Full; its D-1 became CARD-1905). The
  selection was the checkpoint table of `docs/superpowers/plans/2026-11-13-card-1892-input-uncertainty.md`.
  CP-2 was `/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(PhoneHomeLaunchTransportTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(RunnerTerminalSessionInputEncodingTests*)|(RunnerTerminalSessionGenerationTests*)|(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/*`,
  followed by the CP-3 guard union and CP-4/5/6 (GrokRulesReadyOrdering, AgentSessionLaunchFailure,
  SessionMessageQueuePhoneHomeDrop).
- The round-3 diff-only Review of `2026-11-13-card-1892-round3.md` added CP-8 (attention
  classes) and CP-9 (`SessionMessageQueueCodexPointerTests`).
- All of these selections were in Antiphon.Tests only. None ran an Antiphon.E2E row. The plan
  itself records "Native Windows/live acceptance ... remain explicitly pending".

## Outside the selection

why the failing test was outside that selection (lane/category/path): Antiphon.E2E,
`[Category("OptIn")]`, Windows-only (LandDeliveryFixture asserts Windows),
`tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs`. The only E2E rows in
`tests/impacted-classes.json` (`e2e-land-delivery`, `e2e-land-delivery-probe`) select on
land-notification service globs, not on `SessionMessageQueueService.cs`, which is where the
hold lives. The derived index works from type references inside Antiphon.Tests, so it cannot
see that an E2E crash cut on a Grok caller relies on queue recovery retyping a Sent attempt.
Linux Code/Review hosts cannot run this class at all.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782
(added to `tests/impacted-classes.json` as row `e2e-unknown-input-hold`, `windows: only`):

`server/Application/Services/SessionMessageQueueService.cs`,
`server/Application/Exceptions/InputDeliveryUncertainException.cs` ->
`tests/Antiphon.E2E /*/*/AgentTaskLandDeliveryE2ETests/(C488_ReviewDeliveryCrashMatrix_Batch4*)`

This is about 9 minutes on the Windows desktop with one E2E build and a built client/dist.
`DispatchBaseWarningDeliveryE2ETests/C540_PreTypingCrashRecoversWarnings` is the same
mechanism, but it is left out of the row. `DispatchBaseWarningRosterTests` declares
`public partial class DispatchBaseWarningDeliveryE2ETests` inside string literals, and
`scripts/impacted-classes.ps1` maps it to Antiphon.Tests as well. That row would emit a
zero-match `tests/Antiphon.Tests` Windows filter, which is a selector imprecision to follow up.
Run C540 by hand next to the row's C488 method when the hold changes.

## Exemption

none. This is the first note for this cause. Related, same contract change, different
mechanism: [g54 C1196](2026-11-10-why-missed-g54-landrefusal-c1196.md).
