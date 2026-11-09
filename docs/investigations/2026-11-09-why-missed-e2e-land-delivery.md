# Why missed: E2E land delivery scan clock

One committed note for the rc/2026-11-12 run a6410818 chunk `e2e-all-split-5` failure of `AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy)`. The Windows TRX extract is `docs/investigations/2026-11-09-run-a6410818-e2e-split-5-windows-failure.md` on `origin/feat/card-task-a82b3848`. The failure message was `owed.Headline should contain "busy" but was actually "Land Aged notification: Queued; not yet enqueued"` at `AgentTaskLandDeliveryE2ETests.cs:299`.

## Introducing commit/card

introducing commit/card: `4d91773c122186930a3da485dd2a3221a1e960cd` (CARD-1759, cherry-pick of `bf6d0e263e019654471b84808eafe95e979e8f24`)

Before that commit, the hosted land-notification scan paged every non-terminal note. `ReconcileAsync` then compared `NextAttemptAt` with its injected clock. CARD-1759 moved that check into the compiled page filter (`LandNotificationReconcileQueries.DueNoteIds`: `QueueMessageId != null || NextAttemptAt <= now`). The filter's `now` comes from the scan scope's `TimeProvider`.

The C467 fixture (`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`) passes its file-driven `LandClock` only to the land, protocol, monitor, notification and attention services. DI's `TimeProvider` stays `TimeProvider.System`. The monitor stamps the Warning Aged notice with `NextAttemptAt` = land-clock now, which is real time plus 301 s. The scan compares that with real time, so it skips the notice until 301 real seconds have passed. The test reads attention seconds after the Error notice appears, so in the `busy` arm the Warning is always `Queued; not yet enqueued`. The `attempt` arm expects exactly that headline, so it still passes.

Classification: a fixture defect that the product change exposed. Production gives both sides the same `TimeProvider`, so the product is not affected. The failure is deterministic, not a race, and it is not specific to Windows. It only shows up on Windows because the class asserts `OperatingSystem.IsWindows()` and needs ConPTY and a native `fakegrok.exe`. The extract's history fits this: the `busy` arm passed in all nine earlier desktop runs, all at cuts without `4d91773c1`. Its 73 s duration on a6410818 was its fastest ever. Earlier rc cuts 74a8004f4 (run e737e9fe) and 3364197bf (run 4e0d1159) already contained `4d91773c1`, but the extract found no TRX for this test in those runs. So a6410818 was the first E2E execution of this class with the commit.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `53194ebc`, round 1, scope Full, CLEAN. Subject `40cd61ba85cdafcad88f7a7406222b3ec0367b95`; report commit `2b2d8cf8192634de12ab861bd7adfa6ac504d582` (`.antiphon/task-53194ebc.md`). Selection: CP-1 `/*/*/AgentTaskLandNotificationReconcileLoadTests/*` 4/4, CP-2 `/*/*/AgentTaskLandNotificationRecoveryTests/*` 49/49, CP-3 `/*/*/AgentTaskLandNotificationPersistenceTests/*` 12/12, CP-4 guard batch 75/75, all on Linux (server2). Its advisory A-3 accepted "the page reads the scope's `TimeProvider` once" as a latency question. It did not consider a host that injects a different clock into the service than into DI.

## Outside the selection

why the failing test was outside that selection (lane/category/path): E2E lane, category `OptIn`, path `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs` (fixture `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`).

The review ran Integration classes in `Antiphon.Tests` on Linux. `AgentTaskLandDeliveryE2ETests` is in another assembly. It cannot run on Linux (its fixture asserts Windows, ConPTY and a native FakeGrok) and it is `OptIn`, so only the Windows rc E2E roster runs it. The CARD-1759 diff does not touch the E2E project, so a touched-class selection does not name it. The Linux-runnable `LandDeliveryFixtureProbeTests` covered the fixture's verifier wiring but not its clock wiring.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs`, `server/Application/Services/AgentTaskLandNotification*.cs`, `server/Application/Services/LandNotificationReconcileQueries.cs`, `server/Application/Services/AgentTaskLandMonitorService.cs` -> `/*/*/AgentTaskLandDeliveryE2ETests/*` (Windows required), plus the Linux probe `/*/*/LandDeliveryFixtureProbeTests/*`

The Windows row is the one that exercises the real path, and it needs a Windows host with ConPTY and a staged native FakeGrok. It cannot run on server2. A pre-land check on Linux can only be the fixture probe. After this repair, `LandDeliveryFixtureProbeTests/Notification_scan_reads_the_land_clock` fails whenever the scan's scopes do not read the land clock. This note does not edit `tests/impacted-classes.json`. CARD-1801 owns adding the row, and that file's witness suite pins its contents.

## Exemption

same cause as an already written note: none

## Repair

Test-only, on `feat/card-task-61d7498e` from master `ccd3909780fe3e7a177effac627a6e25be42f0d7`. No product code or assertion changed.

- Red `026f497d053536bc42d943d7889a4302f75e0d75`: `LandDeliveryFixtureProbeTests.Notification_scan_reads_the_land_clock` 1 total, 1 failed (`ahead should be greater than 00:05:00 but was -00:00:00.0000002`).
- Fix `680fecc47464aacbf307de71b8f212ebc99e27f9`: `LandDeliveryOptions.ConfigureServices` replaces the `AgentTaskLandNotificationHostedService` registration in place. The new registration hands the scan a scope factory whose scopes resolve `TimeProvider` to the `LandClock`. The rest of DI keeps `TimeProvider.System`.
- Green at `680fecc47`, Linux: `/*/*/LandDeliveryFixtureProbeTests/(Notification_scan_reads_the_land_clock*)|(Fixture_wraps_the_registered_verifier*)` 2 total, 2 passed. The Windows E2E `C467_V32` was not run. Server2 cannot run it.
