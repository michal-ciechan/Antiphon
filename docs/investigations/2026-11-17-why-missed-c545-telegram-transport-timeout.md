# Why missed: C545 Telegram transport timeout accepted a slept success body

One committed note for the rc run 06863906 failure of
`NightlyWatchdogCoreTests.C545_TelegramTransportRequestShape` (chunk
`antiphon-all-group-134`, checkout `870a93d21f2b18c85a8b0905ab689b28e3099813`,
rc/2026-11-24). The timeout row's `ShouldBeFalse` saw `Accepted=true`.
Additional info was the row label `timeout`. CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Debug task `cd7aa04a` at master `e07e3efe8510fc18c7fa043eb85c6488f1fd1de6`.
`TelegramBotTransport` and `WindmillHttpApi` are unchanged. A 200 body with
`ok:true` is a successful send. `TaskCanceledException` while the caller token
is not cancelled is error class `transport` (Windmill: `Unreachable`).

## Failure

Windows Full, one failed test, duration 5.19s, at
`NightlyWatchdogCoreTests.cs:638`: `result.Accepted should be False but was True`.
The timeout row scripted HTTP 200 `{"ok":true,"result":{"message_id":1}}` after
`DelayMs=2000`, with `HttpClient.Timeout` of 200ms, and expected rejection.

The same bytes for `NightlyWatchdogCoreTests.cs`, `C545World.cs`,
`TelegramBotTransport.cs`, and `WindmillHttpApi.cs` are on
`870a93d21f2b18c85a8b0905ab689b28e3099813` and on master
`e07e3efe8510fc18c7fa043eb85c6488f1fd1de6`. Nightly perf commits `dcc7d9591` and
`e07e3efe8` do not touch those files. The accept can recur on current master.

Linux server2, unfixed binary, filter
`/*/*/NightlyWatchdogCoreTests/C545_TelegramTransportRequestShape`, 20 runs,
`--timeout 570s`, `--no-build`. Lease `c8f6d7b6-c591-4a8f-a491-5baffac150a3`
(waited 0s, held 162s). `fail=0 of 20`. Each run executed 1, failed 0. On this
host the 200ms timer won, so the flake did not reproduce.

The accept path is the stub, not the product. `RecordingHttpHandler` awaited
`Task.Delay(2000, cancellationToken)`. `HttpClient` arms `CancelAfter` for its
timeout and classifies that cancellation only when it wins. If the delay
finishes first, the handler returns the 200 body and `TelegramBotTransport`
accepts it. A handler that returned that same body only after the timeout token
had already fired was rejected (`Accepted=false`, empty message id, 369ms) on
.NET 9.0.23. That probe was removed; it still depended on a 200ms timer. The
kept rows throw `TaskCanceledException` with no sleep. `ShouldBeFalse` and
`ErrorClass=transport` are unchanged.

Not a PowerShell script, not `scripts/lib` or `scripts/nightly-watchdog*`, and
not a live `api.telegram.org` call. The stub is in-process
`tests/Antiphon.Tests/TestHelpers/C545World.cs`. The sibling
`C545_WindmillHttpApiRequestShape` timeout row used the same `DelayMs=2000`
against a 200ms timeout.

## Introducing commit/card

introducing commit/card: `d00ff8bdad5f14d84aadfb61645119708021acab` (CARD-0545)

That commit adds the timeout row. Its message records deploy harness 25/25 and
says the C# tests were not yet run. Plan V-545-14
(`docs/superpowers/plans/2026-09-17-card-0545-independent-nightly-watchdog-plan.md`)
specifies a handler delay beyond the client timeout and tells the test to use
200ms. Blame of the committed `DelayMs=2000` line is still that commit.
Follow-up `32d3bc9957ec2d944c4d094b3aa63cb196a609de` records core 14/14 and
contract 37/38 and does not change the timeout lines. That 14/14 is a host
where the 200ms timer won. The test file has only those two commits.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No CARD-0545 review document is in `docs/superpowers/reviews` or `docs/reviews`,
  and no commit under those trees names CARD-0545. The introducing commit's own
  message is the selection record: C# tests not run.
- Later qualification did not select this method. CARD-1281 CP-11 runs only
  `C545_IntentBeforeNetwork`, `C545_ReceiptIdempotent`, and
  `C545_ReaderHeldWithoutSession` (3/3 at
  `docs/investigations/2026-10-15-card-0487-nightly-qualification.md`).
  `docs/investigations/2026-10-15-card-0544-verification-pilot.md` records that
  full `NightlyWatchdogCoreTests` never started.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration,
`tests/Antiphon.Tests/Scripts/NightlyWatchdogCoreTests.cs`, helper
`tests/Antiphon.Tests/TestHelpers/C545World.cs`. The Windows Unit lane excludes
this class (`docs/investigations/2026-10-15-card-1287-windows-unit-lane-5ca376de.md`:
release-gate script classes and `NightlyWatchdogCoreTests` are Integration and
outside that Unit result). The 2026-10-29 rc speedup census lists this file as
`other/helper` and excludes it from the width-2 process. The race is
load-dependent: Linux 20/20 passed while the Windows Full chunk failed once.
A green class run does not prove the 200ms timer always beats `Task.Delay(2000)`.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/C545World.cs` -> `/*/*/NightlyWatchdogCoreTests/C545_TelegramTransportRequestShape`

The sibling `C545_WindmillHttpApiRequestShape` shares the stub. A change to the
helper should run `/*/*/NightlyWatchdogCoreTests/*`. The guard is the absence of
a wall-clock sleep against `HttpClient.Timeout`, which is what this fix removes.

## Exemption

same cause as an already written note: none
