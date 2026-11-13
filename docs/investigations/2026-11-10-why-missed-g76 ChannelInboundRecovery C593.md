# Why missed: fixed wall deadline on the C593 periodic-recovery first pass (run 97150a0b g76)

Debug task 7caf9760, branch `feat/card-task-7caf9760` from master `ab67e69b7`. Failing run: 97150a0b on
`rc/2026-11-19` = `ab7af12c699039270af7e22a5d7fd794035bc325`, Windows chunk `antiphon-all-group-76`.
Evidence: the desktop read `docs/investigations/2026-11-10-run-97150a0b-g76 ChannelInboundRecovery C593-windows-failure.md`
on `a54cb69be` (branch of the sibling read task). CARD-1781 format.

## What failed

`ChannelInboundRecoveryTests.C593_Restart_DrainsToEligibleAndBusyRecipients` (17.5 s), in its last leg
`RecoveryTriggerAsync(signal: false)`: `await WaitForAsync(() => Task.FromResult(worker.CompletedDrainIterations > 0))`
gave `durable handoff did not complete before the diagnostic deadline`. `WaitForAsync` polls against a fixed
`DateTime.UtcNow.AddSeconds(8)` deadline. Here it waited for the first complete pass of the `ChannelBridgeService`
wake worker (a direct `StartAsync`, not Hangfire, not an outbox). A normal pass takes about 1 s of wall time
(ClockSpeed 5: a 1 s scaled wake budget is 0.2 s, plus one 0.4 s session poll and about a dozen Postgres round trips).

Classification: **test fixture/timing defect, load flake.** It is not a product regression and it is not an
expected Windows skip.

- 18 of 18 earlier Windows Full runs passed (10.6 to 13.2 s). This is the first failure.
- `git diff 56e96eabf..ab7af12c6` touches nothing on the path: `ChannelBridgeService*`, `ChannelInbound*`,
  the test class, `BridgeQueueHarness`, `ScaledTimeProvider` and `TestDbFixture` are all unchanged.
- The whole chunk ran 20 to 48 % slower than in the two previous runs (p50 3.74 s against 2.67 to 2.83 s;
  wall 308.6 s against 250 to 257 s). The rest of the same test, its `signal: true` leg and all 47 sibling
  tests passed. Those siblings cover the same wake-timeout disposition, the idle-tick retry and the busy retry.
- The bridge logged to `NullLogger`, so a swallowed `Durable channel inbound drain failed; retrying` and a
  slow pass could not be told apart. That is the second half of the defect.

## Introducing commit/card

introducing commit/card: 3c6975de24481f4cd03aa1a7f548ae51c463c07e (CARD-0593). The commit
`test(card-0593): require signal and periodic recovery after held scan`, 2026-09-26, added
`RecoveryTriggerAsync` with `ClockSpeed = signal ? null : 5` and the first-pass wait. The fixed 8 s
`WaitForAsync` came from the same card, in `579892a4a` and `a56c3a7e7`. CARD-0593 landed linearly as
`a56c3a7e7` on 2026-09-26.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- df7e2e7b, scope Full (Opus), selection: the plan's closed checkpoint list
  (`docs/superpowers/plans/2026-09-26-card-0593-plan.md` `### Checkpoints`): CP-1 `/*/*/ChannelInboundRecoveryTests/*`,
  CP-2 `/*/*/ChannelBridgeTests/*`, CP-3 `/*/*/KafkaInboundCommitTests/*` (Antiphon.Messaging.Tests). It found two
  test gaps (D1 crash-recovery flush, D2 automatic wake and quota override) and a fix round added them.
- 8a3a78a0, scope Full (final), selection: the same CP-1/CP-2/CP-3 (10/10, 40/40, 2/2) plus Unit 3363 passed, and
  three re-run mutations (recovery flush removed, quota override off, `automatic` false).

## Outside the selection

why the failing test was outside that selection (lane/category/path): **it was not outside it.** Integration lane,
categories Integration and Slow, `tests/Antiphon.Tests/Application/ChannelInboundRecoveryTests.cs`, run by CP-1 in both
Reviews. It passed there, and in 18 Windows Full runs, because nothing else loaded the host. The miss is the
assertion design, not the selection:

1. The 8 s wall deadline was not tied to the work it waited on. It was neither a clock-seam budget nor a
   completion signal, so one transient stall of a Postgres round trip or the thread pool failed the test.
2. No witness held the worker past the deadline, so no review run could show that the wait, not the product,
   decided the verdict.
3. CARD-1159 (`72004bb4e`, 2026-10-09) fixed the same ClockSpeed-5 fragility in the sibling
   `C593_ChannelWake_StartsNonAlwaysOnAutomaticallyThroughExhaustedQuota`. Its review did not audit the other
   `WaitForAsync` callers in the class.
4. `NullLogger<ChannelBridgeService>` made the failure undiagnosable from the TRX.

## Fix (this branch, test-only)

- `RecoveryTriggerAsync` now waits through `WaitForWorkerAsync`. The wait ends as soon as the worker's own
  progress satisfies the condition (the completed-pass counter, then the durable handoff rows). The only bound is
  `WorkerHangBackstop` (30 s, about 2x the slowest observed whole-test duration). That bound is a hang detector, not
  a timing assertion. On expiry the failure prints the completed pass count, `ChannelInboundWakeSignal.LastPass`
  and every Information-or-higher line the bridge logged (`C593WorkerLog`), so the next stall is classifiable.
- Red witness `C593_RecoveryTrigger_SlowFirstPassStillHandsOff` holds the first wake pass's head query for 9 s of
  wall time (the existing `C1716WakeScanGate`). That is longer than the old 8 s deadline, as the loaded host did.
  It was red at the witness commit with the run's own message, and it is green after the fix.
- Other `WaitForAsync` callers in this class keep the 8 s helper. They wait on real-clock handoffs that are not
  part of this failure, and changing them is out of scope.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Application/Services/ChannelBridgeService*.cs` -> `/*/*/ChannelInboundRecoveryTests/C593_RecoveryTrigger_SlowFirstPassStillHandsOff`

The impacted-classes row for the same glob still runs the whole class (`/*/*/ChannelInboundRecoveryTests/*`). This
method is the load proxy: it puts the stall that only a loaded Windows host produced into an ordinary run.

## Exemption

none
