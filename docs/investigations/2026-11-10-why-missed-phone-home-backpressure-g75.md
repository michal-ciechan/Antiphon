# Why missed: phone-home byte window during held catch-up

One committed note for the rc/2026-11-15 group-75 failure of `PhoneHomeBackpressureTests.Finite_stall_preserves_connection_order_and_delivery` (run 6591e378). CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Run 6591e378, chunk `antiphon-all-group-75`, line rc/2026-11-15 = `ac6d00022e940ee3b90584c7d313567b4491c498`. 1 failed of 241. The failing result is the post-stall poll inside `ByteWindowDuringCatchupAsync`: `PendingEvents == 1` stayed false for the whole `PhoneHomeSignalWait` (20s). The test duration was 32.297s. There was no `C1523 RED endpoint reason=` line and no harness timeout. Six sibling methods in the class passed. Windows read-out: `docs/investigations/2026-11-10-run-6591e378-phone-home-backpressure-g75-windows-failure.md` on `origin/feat/card-task-354b0623`.

## Failure

`SessionRunnerEventHub.Publish` snapshots subscribers and drops the event when the list is empty. `PhoneHomeHostedRunner.CreateAsync` returns from `WaitLiveAsync` when the server accepts the socket. That accept happens before `PhoneHomeConnectionService.RunConnectedAsync` reaches `SubscribeBounded`. The non-hold path then waits for `DispatchEligible`, which is after subscribe. `holdCatchup: true` skips that wait. `ByteWindowDuringCatchupAsync` then called `Output` immediately. Under the Windows chunk (67 other results overlapping) the runner continuation was late, the publish was delivered to nobody, and `PendingEvents` stayed 0. A longer `PhoneHomeSignalWait` cannot replay a dropped event.

`PendingEvents` can become 1 during a held catch-up once the frame is sent after subscribe. The catch-up hold blocks List/transcript and `StartPump`. It does not gate the receive loop. The 2N-1 byte cap is not the Windows miss. `RunnerOutputEvent` is `(Guid SessionId, long Sequence, string Text)`. For the fixture text `é😀日本語`, default JSON, `PhoneHomeEventPayload.Prepare`, and the framed admission measure are the same size (111 UTF-8 bytes). Cap `2 * 111 - 1` admits one event and refuses two. A Windows path or a CRLF sequence in that same text still fits a cap measured from the payload that is actually published. PascalCase versus the Web framing options does not change the byte count of this payload: the encoder escapes non-ASCII either way, and the property names are the same length.

## Introducing commit/card

introducing commit/card: `cadfdb156f215b143d4a9b027a9ca09e55d7b075` (CARD-1523)

On this history that commit adds `ByteWindowDuringCatchupAsync` (`test(phone-home): complete ordinary guard coverage and document bounded ingestion`, author p, 2026-10-23). `Finite_stall` already calls it at the end of the method. The approving review's source `b481f2d9532d96854535b881d0348606fcf86bcf` is not an ancestor of HEAD; the land rewrote the commits. That SHA already ends `Finite_stall` with `await ByteWindowDuringCatchupAsync()`. CARD-1523 is `e4fb6da6-83e7-469a-803b-3fc61e521e03`. The review text covers CARD-1523 and CARD-1524 together. `f2e61e9b0e62449370b14e70e1e2f6ca49cd1b1c` later set `PhoneHomeSignalWait` to 20s and left the lost publish in place.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `563fd07f-9017-48e8-8368-3977b65c83e1` (outcome `b9473610-02a6-4899-960a-e9e8d658258c`), scope Full, commissionedRound Final, reviewedSourceClean true, reviewedSourceSha `b481f2d9532d96854535b881d0348606fcf86bcf`, ref `refs/heads/feat/card-task-ccf88b5d`. Selection actually run: plan CP-6 through CP-15, ten rows, 11 passed / 0 failed / 0 skipped, receipt `20261023-012712-08b8`. CP-6 is `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Finite_stall_preserves_connection_order_and_delivery`. CP-7 through CP-15 are the other nine plan rows (CP-13's wildcard is two argument cases). All rows are Linux managed loopback, serial, one method. The review did not rerun the historical red build.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was not outside the filter. CP-6 is that method. Lane: Linux managed loopback, serial, one quiet method. Category: `Integration`. Path: `tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.cs` calls `ByteWindowDuringCatchupAsync` in `tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.Bounds.cs`.

The miss is the execution conditions. A quiet Linux method run subscribes before `Output` often enough to pass. The Windows rc chunk runs the class inside a 241-test group. Re-running the same CP-6 filter on a quiet Linux host still misses the drop. `tests/impacted-classes.json` has no row for this class.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.Bounds.cs` -> `/*/*/PhoneHomeBackpressureTests/*`

Also map `tests/Antiphon.Tests/TestHelpers/PhoneHomeHostedRunner.cs` and `src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs` to that class filter. The class filter is required: CP-6 alone does not include `Held_catchup_admits_the_byte_window_when_subscribe_is_late`, which holds subscribe and fails if the helper publishes first. A quiet rerun of CP-6 stays green.

Proposed row (not applied; `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`):

```json
{
  "id": "phone-home-byte-window-subscribe",
  "globs": [
    "tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.Bounds.cs",
    "tests/Antiphon.Tests/TestHelpers/PhoneHomeHostedRunner.cs",
    "src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs"
  ],
  "classes": [
    "PhoneHomeBackpressureTests"
  ]
}
```

## Exemption

same cause as an already written note: none
