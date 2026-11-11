# Why missed: CARD-1505 composer settle delay stalls the TurnEndGap delivery pump (group 60)

One committed note for rc run d88de419, chunk `antiphon-all-group-60`, rc/2026-11-16 =
`6f8741d872d63507827014f972ccf71196722d4a`. Windows reported `TurnEndGapRestartDeliveryTests`
2 failed: `C1205_Restart_recovers_each_delivery_handoff(render-committed, True)` and
`(render-committed, False)`, each at exactly the 90 s harness budget. The class passed in run
6591e378 (rc `ac6d00022`), which does not contain the introducing commit. CARD-1781.

## Failure

Classification: test fixture defect, deterministic, not Windows-specific, not a load flake.
The two cases are red on Linux at master `17ebc0a32` (31 run, 2 failed, both 1m 30s, the same
`TaskCanceledException` from `ControlledTimeProvider.WaitForTimerAsync` at
`TurnEndGapDeliveryHarness.JoinDeliveryAsync`, called from `TurnEndGapRestartDeliveryTests.cs:316`).

CARD-1505 routed interrupted `Sent` recovery (`RecoverDeliveryRunLockedAsync`) through
`ClassifyTypedComposerAsync`. A readable, empty Claude box is now read twice,
`PostEvidenceSettleMs` (default 500 ms) apart, by `Task.Delay(settle, _timeProvider)` in
`ComposerStaysEmptyAsync`. That is intended product behaviour (CARD-0650 double read). The
harness injects a manual `ControlledTimeProvider`, and `JoinDeliveryAsync` advanced only the
20 ms Enter gap and the 50 ms poll timers. Only the `render-committed` cut reaches this path
(a `Sent` row with one attempt, no submission, an empty box), so
`FlushStrandedQueuesAsync` parked on the 500 ms timer until the harness stop token fired.

Fix (`6be4e2051deb0d9e37735cea9f1b84291470e5cf`, test-only): the pump advances the queue's
own delays read from the configured `DeliveryVerification` settings (20 ms, `PollIntervalMs`,
clamped `PostEvidenceSettleMs`). A stop-token hang now throws a `TimeoutException` naming the
timers the pump never advanced. Cancellation disposes the parked delay's timer, so the list is
not filtered on live state. No assertion or budget changed.

Evidence on server2 (Linux):

| Run | Commit | Result |
|---|---|---|
| Class before fix | `17ebc0a32` | 31 total, 2 failed (render-committed True/False, 1m 30s each), 4m 09s |
| Positive control: settle entry removed from the pump | `6be4e2051` minus the settle line | 16 total, 2 failed; message names `timer 5 due 500 ms` |
| Class after fix | `6be4e2051` | 31/31 passed, 1m 07s |
| Guard union (CARD-1505 Review CP-2 filter) | `6be4e2051` | 135/135 passed |

Native Windows proof is owed: the class row on the desktop at the fix tip.

## Introducing commit/card

introducing commit/card: `7c6af459fd31e20ffd2529394b9c112f4caebc41` on master (rc pick
`5aaae94e66b9ab35c65819426915ec61493b0cde`), CARD-1505, Code task `78d7c152`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `471eab6e` (report `321278967`), scope Full, selection CP-1
  `/*/*/(SessionMessageQueueWedgedHeadTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(ExpectationDirectDeliveryTests*)/*`
  (91/91) and CP-2 guard union
  `(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)`
  (135/135). There was no CARD-1505 plan or checkpoint table; the closed list was the brief's
  named classes plus the guard union Code reported.

## Outside the selection

why the failing test was outside that selection (lane/category/path): lane Antiphon.Tests
Integration/Slow, category `Integration`,`Slow`, path
`tests/Antiphon.Tests/Application/TurnEndGapRestartDeliveryTests.cs`.

- The hand table `tests/impacted-classes.json` has no row for
  `server/Application/Services/SessionMessageQueueService*.cs`; on the CARD-1505 range it selects
  only `test-lane-category`.
- The derived index (CARD-1820) caps both changed files: `SessionMessageQueueService.cs` and
  `.Expectations.cs` reach 150 classes against a cap of 25, 125 owed and not run.
- The class has no direct edge anyway. The test file never names `SessionMessageQueueService`;
  it reaches the queue through `TurnEndGapDeliveryHarness.Queue`, and the derived index edge for
  the harness file fires only when the harness itself changes. Re-running `impacted-classes.ps1
  -Range b8812bcb1..7c6af459f` today still does not select it.
- The defect class is "a new `Task.Delay` on the injected `TimeProvider` in a delivery path".
  Tests that hand-pump a manual clock by exact due time hang on any new delay; the CP-1 classes
  use real or auto-advancing time and could not see it.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Application/Services/SessionMessageQueueService*.cs` -> `/*/*/(TurnEndGapRestartDeliveryTests*)/*`

About 70 s on server2. `PermissionWaitFixture.JoinDeliveryAsync` hand-pumps the same 20/50 ms
set and is the next candidate for this row if its classes start reaching stranded recovery
with a readable empty box; it was green in this run.

## Exemption

same cause as an already written note: none.
