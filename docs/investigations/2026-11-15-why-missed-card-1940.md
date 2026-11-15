# Why missed: C544 report fixture root follows a temp checkout

One committed note for CARD-1940. `ReportWorkspace` places its owned git root under the
solution checkout. A checkout under `Path.GetTempPath()` makes that root non-persistent, and
`VerificationRoundDeliveryTests.C544_ReportRegeneratedFromSnapshot` then cannot regenerate the
snapshot report. CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Debug task `12311088` at master `d7ad08b71a19fa79e744284e5ffe7f0168de561a`.
Production `AgentReportStore.IsPersistent` still refuses a root under the temp directory.
No live qualification run was read or changed.

## Failure

Red, before the fixture change, detached checkout `/tmp/card-1940-c544-12311088` at
`d7ad08b71a19fa79e744284e5ffe7f0168de561a`. Native output, `UseAppHost=false`.
Build lease `fda398b2-2bfe-41e6-9238-f8d9b2a8a352` (waited 60s, held 319s, 0 errors).
Test lease `f77d483b-d854-41ab-a19a-789a3bed632b` (waited 165s).
Filter `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/C544_ReportRegeneratedFromSnapshot`.
1 executed, 1 failed, 0 skipped. The case took 18s 333ms; the host took 2m 16s.
Start loadavg `22.89 25.05 22.94`.

Assertion: `TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson)` should not
be null but was. Additional info: `regenerated: frozen rendering`.
`AssertReceivedOnceAsync` line 1115, called from the test at line 829. The settled report path
was already non-null; the restart did not freeze a delivery after that file was deleted.

## Introducing commit/card

introducing commit/card: `1ba060b669c60e1e6a55318e51476b0c5dcc5980` (CARD-0419)

That commit puts `ReportWorkspace.Root` under the directory that contains `Antiphon.sln`.
`82ee1056c3731113962718799a6e129b1ccc40aa` (CARD-0544) constructs that workspace for report
regeneration and states that the fixture is a persistent checkout. The store refuses a root
under `Path.GetTempPath()`, so the statement is true only when the solution checkout is not
under temp.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `7f4d3dc4-6537-484a-96b2-f3b2c238aa30`, scope Full, outcome Clean, stage outcome
  `b81dc8b3-32df-4788-a329-d579f0804c72`, recorded 2026-09-17. Selection: the
  `VerificationRoundDeliveryTests` Q group, three whole-class runs 20/20, including
  `C544_ReportRegeneratedFromSnapshot`. Land tip named in that report:
  `ba8e0717f57f457846f6ca3fa1b62d2376b6d4ee`.
- `cec313dd-43cf-4728-ab8d-04ff565b9c58`, scope Interim, outcome Clean,
  `reviewedSourceSha` `ee2130f52ff91823b63c3a35bbae5e03203a7227`, `reviewedSourceClean` true,
  stage outcome `305dd38c-fa34-4e64-bb70-235d6b0ec1d0`. Selection: CARD-1605 CP-2 through CP-7.
  CP-4 is `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/C544_ReportRegeneratedFromSnapshot`
  and passed 1/1. The same review also ran `VerificationRoundDeliveryTests` with
  `CompletionWarningDeliveryTests`, 32/32.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the method was inside
both selections. Lane is the ordinary Integration class
`tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs`; the root choice is
`tests/Antiphon.Tests/TestHelpers/ReportWorkspace.cs`. Both reviews ran from worktrees outside
the temp directory. There the checkout parent is persistent, the canonical store accepts it,
and regeneration rewrites the snapshot path. A solution checkout under temp is not a separate
method, category, or filter operand.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/ReportWorkspace.cs` -> `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/C544_ReportRegeneratedFromSnapshot`

Run that filter once with the solution checkout under `Path.GetTempPath()` and once from a
checkout outside it.

## Exemption

same cause as an already written note: none

The temp-source cleanup refusal in
[2026-11-10-why-missed-g97-c0824-ref-only-cleanup-red.md](2026-11-10-why-missed-g97-c0824-ref-only-cleanup-red.md)
is a different fixture (`CompletedCardCleanupFixture` / `c1017_checkout_under_temp`).
