# Debug 4b4da169: phone-home byte window during held catch-up

Outcome: test fixture defect. Windows rc run 6591e378 group 75 failed because `ByteWindowDuringCatchupAsync` published before `SubscribeBounded`. The hub drops that event, so `PendingEvents` stays 0 for the whole 20s poll. This is not a product admission bug and not a Windows skip. `PhoneHomeSignalWait` stays 20s.

`PendingEvents` can become 1 during a held catch-up once the frame is sent after subscribe. The hold blocks List/transcript and `StartPump`. It does not gate the receive loop. The 2N-1 cap is not why the first event was missing. For `é😀日本語`, default JSON, `PhoneHomeEventPayload.Prepare`, and the framed admission measure are all 111 UTF-8 bytes. One event fits `2*111-1`. Two do not. A Windows path or CRLF in that same text still fits a cap measured from the published payload. `RunnerOutputEvent` has no path and no timestamp.

## Fix

`PhoneHomeConnectionService.BeforeEventSubscriptionForTest` is awaited at the start of `RunConnectedAsync`, before `SubscribeBounded`. The helper signals live, then waits until `ProducerPressure` is set (first heartbeat, which is after subscribe), then publishes. `Held_catchup_admits_the_byte_window_when_subscribe_is_late` holds that gate. Without the wait the publish is lost.

## Red, then green

Red witness `15aa81dc1b1fcc45bb68305b64e4aa24d773fdb1`. Filter `/*/*/PhoneHomeBackpressureTests/Held_catchup_admits_the_byte_window_when_subscribe_is_late`. Build lease `71191707-ffba-458f-aa80-51329ea3b460` (0 errors). Test lease `5b06758d-f5ca-4574-ad03-f1c6e1e662dd`. Result: 1 failed, 0 passed, 35s 011ms. Shouldly at `PhoneHomeHostedRunner.UntilAsync` line 186: `first byte-window event admitted during catch-up` was false. The pressure-null and pending-0 assertions before that poll passed.

Fix `71c195e4e4e1fcbbb02fd70d22bd7c1df9dcc02e`. Build lease `5926a414-1595-4b47-b280-84244c571737` (0 errors). Test lease `591926ca-405a-4cf3-8003-9d6ed13129bc` on that SHA. Filter is the guard union plus `ProcessSpawnLimitTests*` plus `PhoneHomeBackpressureTests*`. Result: 144 passed, 0 failed, 0 skipped, duration 3m 10s.

| Class | Passed |
|---|---|
| TestClassificationPolicyTests | 41 |
| TestClassificationGuardTests | 1 |
| SlowTestRegistryMergeTests | 5 |
| SlowTestTripwireTests | 2 |
| NativeLinuxPolicyWitnessTests | 3 |
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| C1729LayeringGuardTests | 9 |
| TestLaneCategoryGuardTests | 1 |
| ProcessSpawnLimitTests | 57 |
| PhoneHomeBackpressureTests | 9 |

`Finite_stall_preserves_connection_order_and_delivery` 23669ms. `Held_catchup_admits_the_byte_window_when_subscribe_is_late` 19801ms. `Early_publish_is_dropped_and_the_byte_cap_admits_one_windows_shaped_payload` 960ms, including the admitted-size pin of 111.

## Commits

- `15aa81dc1b1fcc45bb68305b64e4aa24d773fdb1` red witness
- `71c195e4e4e1fcbbb02fd70d22bd7c1df9dcc02e` subscribe wait and why-missed note

`git merge-tree --write-tree origin/master HEAD` against `a8d830d12f0258a8f133364e58b8de4eb25d77bd` exited 0, tree `ed1136b1db3b120e796c9d5dcc9d4f4614fdbd29`. No conflicts. Not merged. Next is review: `PhoneHomeConnectionService` changed.

## WHY-MISSED

introducing commit/card: `cadfdb156f215b143d4a9b027a9ca09e55d7b075` (CARD-1523). That commit adds `ByteWindowDuringCatchupAsync` on this history. Review source `b481f2d9532d96854535b881d0348606fcf86bcf` is not an ancestor of HEAD; it already ends `Finite_stall` with that helper.

approving review(s) with scope (Full/Interim) and the selection they actually ran: task `563fd07f-9017-48e8-8368-3977b65c83e1`, outcome `b9473610-02a6-4899-960a-e9e8d658258c`, Full, Final, reviewedSourceClean true. Selection: plan CP-6..CP-15, 11 passed / 0 failed, receipt `20261023-012712-08b8`. CP-6 is `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Finite_stall_preserves_connection_order_and_delivery`.

why the failing test was outside that selection (lane/category/path): it was not outside the filter. Lane Linux managed loopback, serial, one quiet method. Category Integration. Path `tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.Bounds.cs`. The Windows chunk (241 tests, 67 overlapping) loses the subscribe race. A quiet rerun of CP-6 still passes.

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782: `tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.Bounds.cs`, `tests/Antiphon.Tests/TestHelpers/PhoneHomeHostedRunner.cs`, and `src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs` -> `/*/*/PhoneHomeBackpressureTests/*`. Proposed id `phone-home-byte-window-subscribe`. Not applied. `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`.

same cause as an already written note: none

Note: `docs/investigations/2026-11-10-why-missed-phone-home-backpressure-g75.md`.
