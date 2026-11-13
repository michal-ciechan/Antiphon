# Run 97150a0b, group 76: ChannelInboundRecoveryTests.C593_Restart_DrainsToEligibleAndBusyRecipients (Windows)

Read-only extract, 2026-11-13. Run 97150a0b on `rc/2026-11-19` = `ab7af12c699039270af7e22a5d7fd794035bc325`, desktop.
Test checkout: `C:\Antiphon\releases\checkout-1d7cc1fe`. The live run was not touched.

## Verdict

This is a **timing/load flake, not a product regression**. The test has a hard-coded 8-second wall-clock deadline
(`WaitForAsync`, `ChannelInboundRecoveryTests.cs:634`). It applies that deadline to the wake worker's
**first complete drain pass** in the `signal:false` leg (`RecoveryTriggerAsync`, line 2107). That leg uses
`ClockSpeed = 5`. One pass makes about a dozen Postgres round trips. The deadline does not come from the
clock seam or from a completion signal.

- The test passed in **18 of 18** earlier Windows Full runs (10.6 to 13.2 s; table below). This is its first failure.
- **No code on the failing path changed in the new cut.** `git diff 56e96eabf..ab7af12c6` touches none of these:
  `ChannelBridgeService*`, `ChannelInbound*`, `ChannelInboundRecoveryTests*`, `BridgeQueueHarness`, `ScaledTimeProvider`, `TestDbFixture`, `AlertService`.
  The only change in a nearby file is additive: `SessionMessageQueueService.CancelPendingIfUntypedAsync` (CARD-0747,
  aged queue rows). The bridge drain and this test never call it.
- **The whole chunk ran 20 to 48 % slower than in the two previous runs.** The table under "Load evidence" has the figures.
- **What the pass was blocked on cannot be seen.** The test builds the bridge with `NullLogger<ChannelBridgeService>`
  (line 576). A swallowed `"Durable channel inbound drain failed; retrying"` exception and a slow pass look the same.
  The TRX and diag logs contain only the assertion.

The general slowdown (~1.4x) does not explain an ~8x overrun on its own. The usual pass takes about 1 s wall:
it waits out a 1 s budget at 5x (0.2 s), then one 0.4 s poll delay, plus the DB I/O. So there was a **local stall
in this one window** (07:53:21 to 07:53:29Z). It was either a Postgres or thread-pool stall under host load, or one or
more failed passes retried every `WakeIdleTick` (10 s scaled = 2 s wall). Everything before the wait in the same
test ran at normal speed: about 9.5 s, against 10.6 to 13.2 s for the whole test in passing runs. The test design
lets one transient stall fail the test with no diagnosis. That design is the defect.

## Failure

| Field | Value |
|---|---|
| Test | `Antiphon.Tests.Application.ChannelInboundRecoveryTests.C593_Restart_DrainsToEligibleAndBusyRecipients` |
| Categories | Integration, Slow |
| Duration | 17.547 s (start 07:53:11.669Z, end 07:53:29.216Z) |
| Assertion | `ShouldAssertException: await predicate() should be True but was False` |
| Additional info | `durable handoff did not complete before the diagnostic deadline` |
| Expected / actual | `true` / `false` for `worker.CompletedDrainIterations > 0` |
| Chunk result | 215 total, 214 passed, 1 failed, run wall 5 m 07 s |

Top stack frames, from `antiphon-all-group-76-tests.log` (async rethrow frames collapsed):

```
at ChannelInboundRecoveryTests.WaitForAsync(Func`1 predicate)            ChannelInboundRecoveryTests.cs:640
at ChannelInboundRecoveryTests.RecoveryTriggerAsync(Boolean signal)      ChannelInboundRecoveryTests.cs:2107
at ChannelInboundRecoveryTests.RecoveryTriggerAsync(Boolean signal)      ChannelInboundRecoveryTests.cs:2121   (x2: await-using disposals)
at ChannelInboundRecoveryTests.C593_Restart_DrainsToEligibleAndBusyRecipients()  ChannelInboundRecoveryTests.cs:2087   (x4)
--- TUnit internals omitted ---
```

Line 2087 is `await RecoveryTriggerAsync(signal: false);`, so the `signal: true` leg on line 2086 had already passed.
The failing leg uses `ClockSpeed = 5` and `Settings(timeout: 1)`. Line 2107 is
`await WaitForAsync(() => Task.FromResult(worker.CompletedDrainIterations > 0));`: the wait for the first full
drain pass. The handoff assertion further down (lines 2113-2120) never ran.

Stdout contained only the line `ANTIPHON_TEST_PHASE_ID ...C593_Restart_DrainsToEligibleAndBusyRecipients.1.1.0`.
Stderr was empty, the TRX has no application log, and the diag log (`antiphon-all-group-76-execution\log_261113075048817.diag`)
has only the TUnit node updates repeating the same stack.

Files read (name filter `antiphon-all-group-76*`): `antiphon-all-group-76.trx`, `antiphon-all-group-76-tests.log`,
`antiphon-all-group-76.timing.json`, `antiphon-all-group-76-execution\log_261113075048817.diag`.

## What the wait is waiting on

This is not a Hangfire job or an outbox publisher. It is the `ChannelBridgeService` wake worker
(`RunWakeWorkerAsync`, `server/Application/Services/ChannelBridgeService.cs:202`), started directly by the test
with `worker.StartAsync`. `_completedDrainIterations` increases only after `DrainPendingAsync` **returns**
(line 212). If a pass throws, the exception is swallowed with a Warning, the counter does not move, and the worker
sleeps `WakeIdleTick` (10 s on the harness clock, 2 s wall at 5x) or until the next wake signal.

The first pass of the `signal:false` leg:
1. Takes `_drainGate` and reads the pending agent heads and their members from `ChannelInbounds`.
2. Calls `ProcessAgentGroupAsync`, which runs `pg_try_advisory_lock` (non-blocking), then `EnsureAgentSessionAsync`.
   The session is `Starting` (line 2102), so it polls `Agents`, `AgentSupervisionStates` and `AgentSessions`, waits
   `SessionPollInterval` (2 s scaled = 0.4 s wall), passes the 1 s scaled deadline, and returns null.
3. Applies the `ChannelWakeTimeout` disposition: `RecordWakeIncidentAsync` (`SELECT ... FOR UPDATE` on the inbound row,
   with a dedup return because the direct call at line 2104 already stamped it), then `AlertService.RaiseAsync`
   (DB insert; the harness uses `NullAlertRouter`). Then it releases the advisory lock.
4. Sweeps owned Pending queue rows on Running sessions. There are none, because the session is `Starting`.
5. Calls `PassCompleted`, and the counter increases.

None of these steps waits on anything external with a long timeout, so the normal pass takes about 1 s wall.
To miss the 8 s deadline, either the DB or the thread pool stalled for several seconds, or several passes in a row
threw and were retried. The `NullLogger` hides which.

## Passing siblings

All 47 other `ChannelInboundRecoveryTests` in the chunk passed:

C767_ConcurrentDrains_ClaimMissRecoversWithoutDuplicatePrompt (21.7 s), C1716_BusyPass_PublishesHeartbeatAndPreventsPrematureStall,
C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick, C1716_HostedService_StartsIndependentStallCheckerAndRearmsAfterRecovery,
C1716_HeldQueueLock_OtherSessionQueuedWithinOnePass(enqueue|sweep), C1716_BusySession_KeepsOrderWhenLockFreesMidPass_AndRetriesNextPass,
C1716_StalledWakeWorker_RaisesOneChannelIncidentPerStall, C768_AgedHoldRaisesChannelIncidentAtThreshold (x3),
C768_HoldEpisodeDeduplicatesAcrossDrainsAndRestart (x3), C768_BrokerReplayAfterPayloadPurgeKeepsNativeIdentity (x2),
C768_AgedHoldIgnoresOwnedAndUnknownRefusals (x2), C768_HoldIncidentsUseExistingAttentionAndPaging, C768_HoldIncidentAndStampCommitAtomically (x6),
C767_CancelledPermitWait_DoesNotOverRelease, C767_HealthyAgent_ReceivesBeforeFailedWakeDeadline(2|64), C767_MalformedDelivery_IsAcknowledgedAndNextMessageArrives,
C767_AgentHeadPaging_WrapsPastHeldAgents, C767_CompetingBridge_MultiPageClaimMissResumesOldestCompletePrompts,
C767_FailedPage_ResumesOldestPendingBeforeLaterRows, C767_ParkedPage_ResumesOldestCompletePromptsBeforeLaterRows(herdr|timeout|capacity),
C767_Stop_AwaitsCancelledGroupsAndRestartRecovers, C767_FailedAgent_WakesOncePerPass, C767_AgentConcurrency_IsBoundedAndAllGroupsAreAwaited,
C593_ChannelWake_StartsNonAlwaysOnAutomaticallyThroughExhaustedQuota, C593_WakeTimeout_PreservesEnvelopeAndCriticalIncident,
C593_TransferAndUncertainWrite_RequireCompleteRecipientReceipt, C593_ChannelWake_IsAutomaticWithQuotaOverride,
C593_HoldsAndConflicts_ParkWithoutRerouting, C593_QueuedLaunch_WaitsOnceAndRecognizesTerminalFailure,
C593_AcceptanceAckAndQueueCommitCuts_AreIdempotent, C593_CrashAfterQueueOwnership_RecoveryFlushesCompleteRecipientPrompt (ran just before it),
C593_DebounceCrash_RetainsEveryMemberInOrder (ran just after it).

These siblings bound the cause:
- **C593_WakeTimeout_PreservesEnvelopeAndCriticalIncident** and the **C767 timeout / FailedAgent** pages run the same
  `ChannelWakeTimeout` disposition path through the wake worker, and they passed.
- **C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick** proves the idle-tick and retry cadence.
- The **signal:true leg of the failing test itself**, on the real clock, completed its first pass and its signal-driven handoff.
- The first half of the failing test (restart recovery, replacement-session rebinding, busy recipient through `OnTurnEndAsync`)
  passed all its assertions.

So the drain logic, the wake-timeout disposition and the periodic tick are functionally intact on this cut.

The other classes in the chunk all passed: AgentTaskLandReceiptTests (42), AgentTaskLandRegistrationSafetyWindowsTests (4),
AgentTaskLandTargetRaceFakeTests (6), ApiErrorRecoveryServiceTests (40), ExpectationDirectDeliveryTests (14),
ReviewEvidenceSettlementTests (11), RoutingPinCandidateDispatchTests (12), RoutingPinServiceTests (13), RunnerCompletionProgressTests (12),
StandingResumeReceiptReadinessTests (6), TaskParkPublicationTests (4), VerificationCleanupServiceTests (3).

## What ran at the same time

The run executes chunks one at a time: group 75 ended at 07:50:48, group 76 ran from 07:50:48 to 07:55:57, and group 78
started at 07:55:57. Two tests from the same process overlapped the failing test:

| Test | Window (Z) | Duration |
|---|---|---|
| C1065_WorkspaceModesPreservePublicationAuthority | 07:53:00.771 to 07:53:26.555 | 25.8 s (17.0 s and 17.5 s in the previous two runs; slowest test in the chunk) |
| C1065_CommitInstructionsAndRefusalsRespectOverrides | 07:53:26.555 to 07:53:31.602 | 5.0 s |

The same C1065 test overlapped this one in both previous runs, where both passed, so the overlap alone is not the cause.
The AppHost restart (05:52Z) was two hours earlier. I did not inspect other desktop processes (read-only rule).

## Load evidence (group 76, same 215 tests in all three runs)

| Run | Run wall | p50 | p95 | max | C593_Restart |
|---|---|---|---|---|---|
| ae84478d (rc/2026-11-17) | 257.0 s | 2.83 s | 10.85 s | 21.85 s | Passed 11.06 s |
| 0b292fa4 (rc/2026-11-18) | 249.9 s | 2.67 s | 10.80 s | 21.55 s | Passed 10.80 s |
| **97150a0b (rc/2026-11-19)** | **308.6 s** | **3.74 s** | **13.27 s** | **25.78 s** | **Failed 17.55 s** |

C767_ConcurrentDrains: 21.7 s, against 17.6 s and 17.0 s. C593_CrashAfterQueueOwnership: 1.59 s, against 1.05 s and 1.20 s.

## History across all Windows Full runs (read-only sweep of `C:\Antiphon\releases\candidates\rc-2026-1*\logs`)

| Run | Chunk | Outcome | Duration |
|---|---|---|---|
| d2777e21 (10-22) | group-16 | Passed | 12.00 s |
| a779085c (10-24) | group-28 | Passed | 10.66 s |
| 28bbb58e (10-28) | group-75 | Passed | 11.07 s |
| d47848cc (10-30) | group-74 | Passed | 12.46 s |
| c8a7bcee (11-01) | group-75 | Passed | 11.27 s |
| 7f8e9fd1 (11-02) | group-75 | Passed | 13.23 s |
| 0b05506b (11-03) | group-73 | Passed | 12.24 s |
| 766e3df5 (11-04) | group-73 | Passed | 11.51 s |
| b19a9880 (11-04) | group-75 | Passed | 12.78 s |
| 472c7495 (11-06) | group-73 | Passed | 11.05 s |
| 8e938b6d (11-06) | group-73 | Passed | 10.84 s |
| e737e9fe (11-08) | group-73 | Passed | 11.54 s |
| a6410818 (11-08) | group-74 | Passed | 11.26 s |
| dce78ee0 (11-09) | group-75 | Passed | 11.20 s |
| 6591e378 (11-10) | group-75 | Passed | 12.60 s |
| d88de419 (11-11) | group-76 | Passed | 11.30 s |
| ae84478d (11-11) | group-76 | Passed | 11.06 s |
| 0b292fa4 (11-12) | group-76 | Passed | 10.80 s |
| **97150a0b (11-13)** | **group-76** | **Failed** | **17.55 s** |

## File history

- `3c6975de2` (2026-09-26) `test(card-0593): require signal and periodic recovery after held scan` added `RecoveryTriggerAsync`
  with `ClockSpeed = signal ? null : 5` and the `CompletedDrainIterations > 0` wait.
- `579892a4a`, `a56c3a7e7` (2026-09-26, CARD-0593) added `WaitForAsync` with the 8 s `DateTime.UtcNow` deadline.
- `72004bb4e` (2026-10-09, CARD-1159) fixed the same kind of fragility in the sibling
  `C593_ChannelWake_StartsNonAlwaysOnAutomaticallyThroughExhaustedQuota`: at ClockSpeed 5 a wake budget shrinks
  to one real second. That fix did not cover this wait.
- No commit on `56e96eabf..ab7af12c6` touches the test or the services it exercises.

## Why it was missed before the rc run

It is a latent, load-dependent deadline that has been in the suite since 2026-09-26 and passed 18 times in a row on
Windows. No pre-rc gate could have turned it red without extra host load. Two gaps let it reach the rc gate as a
blind failure:
1. **The wall-clock deadline does not come from the work it waits on.** The 8 s is not tied to the clock seam or to a
   completion signal.
2. **The bridge under test logs to `NullLogger`.** A swallowed pass exception and a slow pass cannot be told apart.

The CARD-1159 review fixed one instance of the ClockSpeed-5 class without auditing the other `WaitForAsync`
callers in the same class.

## Proposed fix (smallest, test-only; Debug stage)

Do not lengthen the 8 s deadline or add a sleep.

1. **Make the first-pass wait deterministic.** Replace line 2107 with a wait that ends only on a worker event:
   - a completed pass (`worker.CompletedDrainIterations > 0`, or `ChannelInboundWakeSignal.LastPass` not null); or
   - a recorded pass failure, which the test reports at once with the exception.

   The backstop is the test's own timeout or a cancellation token, not an arbitrary 8 s. To get the failure event,
   pass `Bridge()` (line 572) a small recording `ILogger<ChannelBridgeService>` (the C1716 file already has the
   `C1716BusyHookLogger` pattern) that captures `Warning` and above. `WaitForAsync` can then append the captured
   warnings to its failure message, which makes every C593 deadline failure diagnosable.
2. **Optional, if a bounded wall wait must stay for a hang check:** derive it from the work. Use `h.Clock`-scaled
   budgets plus a fixed DB allowance, and print the recorded warnings and `LastPass` on expiry.

**Windows red witness (commit it before the fix):** use `HarnessOptions.ConfigureDbContext` to add an EF
`DbCommandInterceptor` that holds the wake worker's first `ChannelInbounds` head query (the `GROUP BY "AgentId"`
select) on a test-owned `SemaphoreSlim`, and releases it only after 9 s of wall time. The current helper goes red with
the same message, `durable handoff did not complete before the diagnostic deadline`, at
`RecoveryTriggerAsync:2107`. After the fix the same witness goes green, because the wait ends on the pass
completing. A second witness makes the head query throw once. It should fail fast with the captured
`"Durable channel inbound drain failed; retrying"` warning in the message, not after 8 s.

Suggested filter: `--treenode-filter "/*/*/ChannelInboundRecoveryTests/C593_Restart_DrainsToEligibleAndBusyRecipients"` plus the new witness.

No build or test was run for this extract, to keep load off the desktop while the live run is in progress.
