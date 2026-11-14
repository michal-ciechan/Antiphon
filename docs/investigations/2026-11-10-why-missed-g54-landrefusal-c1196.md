# Why missed: g54 LandRefusal C1196 pre-typing fault still expected a Pending revert

rc qualification run 34d41b7e, rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4,
chunk antiphon-all-group-54 (Windows native desktop):
`LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row`
failed in 0.58 s. Classification: TEST FIXTURE DEFECT (stale pre-contract expectation),
not a Windows product defect and not a Windows-only skip. It is platform independent; the
group simply first ran on the Windows qualification.

## Cause

The CARD-1196 harness models a pre-typing transport failure by throwing `IOException` from
the test-only `LandDeliveryBoundary` seam `queue-before-typing`
(`SessionMessageQueueService.FlushLockedAsync`, inside the delivery `try`). The test then
asserts the row was reverted to `Pending` with `Attempts == 1` (`C1196:pretyping-reverted`).

CARD-1892 commit 5b80144d3 made `InputDeliveryUncertainException.Is` treat
`IOException`, `HttpRequestException`, `TimeoutException` and `OperationCanceledException`
at that boundary as an ambiguous Input outcome: the attempt is retained as `Sent` with a null
verdict, no automatic resend, and only a complete transcript `UserPrompt` confirms it. The
product cannot tell this seam's exception from a lost Input reply, so the row is now held
`Sent`/null and the Pending assertion fails at once (0.58 s, deterministic, every platform).

Repair (same shape as CARD-1916's `flush-definitive-refusal-then-retry`): the fault at
`queue-before-typing` now throws a definitive runner refusal
(`RunnerProblemException(409, ..., "fixture_input_refused")`), which still takes the
revert-to-Pending path that this test witnesses. Every original assertion is kept unchanged:
Pending, one attempt, baseline and start stamped, no submission, no UserPrompt, no confirm,
history kept, then after restart the same row id is retried as attempt 2, delivered once and
resolves the historical refusal. No timeout, retry or assertion was loosened. The method name is
kept so rosters and the live-lane discovery fixture stay stable; its summary now says
"definitive pre-typing refusal". The ambiguous-transport hold itself is already witnessed by
`AgentTaskMidTurnRefineTests` (CARD-1916) and `SessionMessageQueuePhoneHomeDropTests`.

## Introducing commit/card

introducing commit/card: 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 (CARD-1892), cherry-pick of
0b2c08ae7149704b2f34b429a8a3880809f4761d. The stale expectation itself dates from CARD-1196
(b4f541580 / 4bb8719f6), which was correct under the pre-CARD-1892 contract.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- d552541f-0d4b-42be-9386-7c2f8f495a38 (CARD-1892 round 1), scope Full, selection: plan
  `docs/superpowers/plans/2026-11-13-card-1892-input-uncertainty.md` checkpoint table, CP-2
  `/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(PhoneHomeLaunchTransportTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(RunnerTerminalSessionInputEncodingTests*)|(RunnerTerminalSessionGenerationTests*)|(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/*`,
  CP-3 guard union, CP-4 `GrokRulesReadyOrderingTests`, CP-5 `AgentSessionLaunchFailureTests`,
  CP-6 `SessionMessageQueuePhoneHomeDropTests`.
- caeec914-1116-45ef-b19e-5e087159296f (CARD-1892 round 2), scope Full, same table (repair-only
  CP-2/CP-3 rerun), plus the round-3 plan `2026-11-13-card-1892-round3.md` with the same union.
  Their Found outcomes concerned other real defects and stay as recorded.
- The CARD-1916 Debug repair (b559bfba2 and predecessors on this rc) fixed the same stale
  pattern in `AgentTaskMidTurnRefineTests` but ran only that class plus guards.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Antiphon.Tests
integration lane, `[Category("Integration")]`, `tests/Antiphon.Tests/Application/LandRefusalDeliveryWitnessTests.cs`
with its fixture `tests/Antiphon.Tests/TestHelpers/LandRefusalDeliveryHarness.cs`. The CP-2 union
was chosen by queue/rules/launch class names. This class lives in the land-notification family
and does not name `InputDeliveryUncertainException`, so neither the derived impact index (type
references) nor the `land-notification` impacted-classes row (land-notification service globs)
selects it for a diff that only changes the exception classifier and the queue catch. Nothing
mapped "which tests throw a transport exception at an Input seam and assert Pending" to the
classifier file. The CARD-1916 audit found the same gap for two other classes but did not sweep
for further `IOException`/`HttpRequestException` Input fault fixtures.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782
(added to `tests/impacted-classes.json` as row `input-delivery-uncertainty`):

`server/Application/Exceptions/InputDeliveryUncertainException.cs` ->
`/*/*/(LandRefusalDeliveryWitnessTests*)|(AgentTaskMidTurnRefineTests*)|(SessionMessageQueuePhoneHomeDropTests*)/*`

About 2-3 minutes on one shared Antiphon.Tests build.

## Exemption

none; first note for this cause. Related, same contract change, different classes:
[CARD-1916/1919 inherited reds](2026-11-14-card-1916-1919-inherited-reds.md).
