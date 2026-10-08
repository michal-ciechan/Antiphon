# CARD-1137: warm-up remedy for the HTTP-arm runner seat

Date: 2026-10-08. Code task `b2051427`. Input: the S1 investigation
`docs/investigations/2026-10-08-test-hardening-1137-1130-timing.md` (commit `a5ea05f60`).
Test-side only. No production change, no migration.

## Problem (from S1, not re-measured here)

`RunnerSeatReleaseFixture.LiveSeat` starts a fresh in-process `WebApplication` for every
HTTP-arm seat. ASP.NET Core builds all of an app's endpoints, and the JSON contracts of their
request delegates, on the app's first request. The fixture handed `Client` to the test with no
request made. So each seat's first test call paid that build inside the 10 s
`LoopbackBudgetSeconds`. The combined 56-result filter starts 10-11 such seats together. It was
red in 3 of 16 runs, with 8-11 HTTP-arm methods timing out within 92 ms of each other.

## Decision

- **D-1. Remedy: a per-seat warm-up in the fixture.** After `_app.StartAsync()`, and before
  `Client` is created, `LiveSeat.WarmUpAsync` sends one `GET /sessions` through its own
  `HttpClient`. That client has a separate `StartupBudgetSeconds = 60` budget. The fixture
  awaits a success status. It runs inside `StartTransportAsync`, so the recreated app after
  `RestartRunnerAsync` and `RestartServerTransportAsync` is warmed too. The app has no
  authentication middleware, so the request carries no credential. The phone-home arm already
  completes a real `ListAsync` before it hands out its client, and is unchanged.
- **D-2. Per seat, not once per test host.** The endpoint data source and the minimal-API
  `JsonOptions` belong to each app's own service provider, so part of the cost is paid once per
  app. A process-wide one-time warm-up would leave every later app cold. A per-seat warm-up
  also covers the process-wide share (JIT, reflection caches) on the first seat. The split was
  measured with the guard test's `C1137-WARMUP` line (see Results).
- **D-3. The 10 s test budget stays as it is.** `LoopbackBudgetSeconds` still governs the
  handed-out client, the read factory and `ListTimeoutSeconds`/`RequestTimeoutSeconds`. The
  warm-up does not wait longer for test behaviour. It moves a one-time startup cost that is not
  under test out of that window. No sleep, retry or stub.
- **D-4. The warm-up is not serialized across seats.** Seats warm up concurrently, as they did
  before. Only the budget that absorbs the contention changes. A cross-seat lock would add a
  queue wait that also has to fit the budget, without any benefit that can be measured.
- **D-5. A deterministic regression guard.** The app's middleware records each response as
  `"<method> <path> <status>"` when the response starts. `ServedBeforeClient` is a snapshot taken
  just before `Client` is created. The new `RunnerSeatLiveSeatWarmupTests` asserts that the
  snapshot is exactly `GET /sessions 200` for a first app, a second app, and the apps recreated
  after a server-transport restart and a runner restart. It then proves that the handed-out
  client still works: one `ListAsync` and one `SubmitAsync`. If the warm-up is removed, or moved
  after the hand-out, the snapshot is empty and the test fails. It does not depend on timing.

## Verification design

| ID | Test | Promise |
|---|---|---|
| V-1 | `RunnerSeatLiveSeatWarmupTests.Http_seat_serves_its_warmup_before_handing_out_the_client` | Every HTTP-arm app (first, second, after a server-transport restart, after a runner restart) has served exactly `GET /sessions 200` before `Client` exists. The client then lists the seat and delivers input. |
| V-2 | Combined `(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)` | 56 results, 0 failed, in each of 20 serial repetitions under ambient host load. The baseline was 3 of 16 red. |
| R-1 | `RunnerSeatOrphanSweepTests` (17), `TerminalRunnerSeatReleaseTests` (39), the named method alone | No behaviour change from the warm-up request. |
| R-2 | `TestClassificationGuardTests`, `SlowTestTripwireTests` | The new Integration class is classified, with no Slow entry needed. 3 results. |

### Negative controls

| PC | Temporary mutation | Exact method filter | Expected red, then restored green |
|---|---|---|---|
| PC-1 | Delete `await WarmUpAsync(uri);` in `RunnerSeatReleaseFixture.LiveSeat.StartTransportAsync`. | `/*/*/RunnerSeatLiveSeatWarmupTests/Http_seat_serves_its_warmup_before_handing_out_the_client` | `ServedBeforeClient` is empty, not `GET /sessions 200` ("the first app in this process"). Restore and it is green. |
| PC-2 | Move `await WarmUpAsync(uri);` to after `Client = new SessionRunnerHttpClient(...)`, leaving the snapshot where it is. | same | Same assertion fails. Restore and it is green. |

A timeout under uncontrolled load is not a deterministic control. V-2 is a repetition record,
not a positive control.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1137/` | linux-warmup-guard | `/*/*/RunnerSeatLiveSeatWarmupTests/*` | V-1 | exact 1 results, 0 failed/skipped | 1 | 6 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-2 | S1 | `CP-1` | linux-named-alone | `/*/*/RunnerSeatOrphanSweepTests/Discovery_request_uses_runner_owned_delivery_evidence*` | R-1 | exact 1 results, 0 failed/skipped | 1 | 2 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-3 | S1 | `CP-1` | linux-orphan-class | `/*/*/RunnerSeatOrphanSweepTests/*` | R-1 | exact 17 results, 0 failed/skipped | 17 | 3 | true | n/a |
| CP-4 | S1 | `CP-1` | linux-release-class | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-1 | exact 39 results, 0 failed/skipped | 39 | 3 | true | n/a |
| CP-5 | S1 | `CP-1` | linux-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-2 | exact 3 results, 0 failed/skipped | 3 | 2 | true | n/a |
| CP-6 | S1 | `CP-1` | linux-combined-01 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-7 | S1 | `CP-1` | linux-combined-02 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-8 | S1 | `CP-1` | linux-combined-03 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-9 | S1 | `CP-1` | linux-combined-04 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-10 | S1 | `CP-1` | linux-combined-05 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-11 | S1 | `CP-1` | linux-combined-06 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-12 | S1 | `CP-1` | linux-combined-07 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-13 | S1 | `CP-1` | linux-combined-08 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-14 | S1 | `CP-1` | linux-combined-09 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-15 | S1 | `CP-1` | linux-combined-10 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-16 | S1 | `CP-1` | linux-combined-11 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-17 | S1 | `CP-1` | linux-combined-12 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-18 | S1 | `CP-1` | linux-combined-13 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-19 | S1 | `CP-1` | linux-combined-14 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-20 | S1 | `CP-1` | linux-combined-15 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-21 | S1 | `CP-1` | linux-combined-16 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-22 | S1 | `CP-1` | linux-combined-17 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-23 | S1 | `CP-1` | linux-combined-18 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-24 | S1 | `CP-1` | linux-combined-19 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-25 | S1 | `CP-1` | linux-combined-20 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-2 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |

Not in scope: the whole Unit lane, namespaces, the full assembly and `Antiphon.Agents.Pty.Tests`.
No AppHost or runner restart.

## Results

Pending: filled in after the checkpoint run.
