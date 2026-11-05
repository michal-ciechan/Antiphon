# Red-prove: CARD-1716 survivor witnesses (Code c1e2a660)

Task fada17c6 (Claude; author of the witnesses is Codex). Subject branch
`feat/card-task-c1e2a660` tip `a84d93a804273b5808215d9246113ad9619492d5`.
Survivors from Mutation a4933b4f (`docs/investigations/2026-11-05-mutation-1716-1719-1709.md`
on `feat/card-task-a4933b4f`): W5 / PC-1, W7 / PC-2, W8 / PC-3.

## Verdict

| Witness | PC | Mutant (production, `server/Application/Services/ChannelBridgeService.cs`) | Result |
|---|---|---|---|
| `C1716_BusyPass_PublishesHeartbeatAndPreventsPrematureStall` | W5 / PC-1 | `DrainPendingAsync`: skip `_wakeSignal.PassCompleted(summary)` when `summary.SessionsSkippedBusy > 0` | **KILLED** |
| `C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick` | W7 / PC-2 | `RunWakeWorkerAsync`: `delay = WakeIdleTick` instead of `NextWakeDelay(pass)` | **KILLED** |
| `C1716_HostedService_StartsIndependentStallCheckerAndRearmsAfterRecovery` | W8 / PC-3 | `ExecuteAsync`: replace `RunWakeWorkerStallCheckAsync(workers.Token)` with a cancellation-bound never-completing task (`Task.Delay(Infinite, token).ContinueWith(_ => {})`) | **KILLED** |

Each mutant killed only its own witness; the other six C1716 methods stayed green under
every mutant. All three witnesses are green at the restored original tip.

## Method

One mutant build: the three mutants were applied to `ChannelBridgeService.cs` behind
`Environment.GetEnvironmentVariable("C1716_MUT") == "W5" | "W7" | "W8"` (a switch unset
leaves original behaviour). One leased build (`dotnet build tests/Antiphon.Tests
-p:UseAppHost=false`, native output, 0 errors, held 267 s), then four leased runs of
`--treenode-filter "/*/*/ChannelInboundRecoveryTests/C1716_*"` (`--no-build
-p:UseAppHost=false`, `TEMP=/tmp`), one per switch value plus an unset control. The file
was then restored with `git checkout --`, rebuilt (one lease) and the same selection run
green (one lease).

| Run | Selection total | Failed | Failing method |
|---|---:|---:|---|
| C1716_MUT=W5 | 7 | 1 | BusyPass_PublishesHeartbeat... |
| C1716_MUT=W7 | 7 | 1 | HostedWorker_RetriesBusyAtFiveSeconds... |
| C1716_MUT=W8 | 7 | 1 | HostedService_StartsIndependentStallChecker... |
| switch unset (mutant build) | 7 | 0 | - |
| restored tip a84d93a80, fresh build | 7 | 0 | - |

## Red messages

- **W5**: `ShouldAssertException: signal.LastPass should not be null but was` /
  `busy-heartbeat: a busy pass is still a completed pass` (C1716.cs line 54).
- **W7**: `ShouldAssertException: retry should be 00:00:05 but was 00:00:10` /
  `busy-loop-delay: the actual worker must use the busy retry` (line 90).
- **W8**: `OperationCanceledException: The operation was canceled.` from
  `C1716LoopClock.NextTimerAsync` (line 188) called at C1716.cs line 139 — the hosted
  service never registered the one-minute stall-check timer within the 10 s observation bound.

## Witness reading

**W5 (heartbeat).** Asserts the behaviour, not a code path: `LastPass` is asserted null
first (no stale value), then the published pass's `CompletedAt` equals the fake clock and
its counts are 1 seen / 0 queued / 1 busy, the row stays unowned, and the stall check one
minute later (five minutes after startup) raises nothing. Calls `DrainPendingAsync`
directly, which is correct here because the mutant lives inside the pass. Deterministic:
fake clock for the heartbeat and stall age; the only real wait is the production 1 s
`ScanQueueLockWait` on the held lock (at, not over, the one-second line), bounded by the 8 s
pass diagnostic. Cannot pass vacuously.

**W7 (busy retry wiring).** Drives the real hosted worker; a `TimeProvider` wrapper records
each timer's due time with the completed-pass count at registration, so the assertion reads
the delay the loop actually chose after pass 1. Then checks no pass at 4.999 s, the skipped
row owned after 5 s, the next timer 10 s, and no pass at 9.999 s. Under the mutant every
candidate timer (wake and the withheld sibling) is 10 s, so the red is guaranteed, not
timing-dependent. Fake time throughout; waits are channel reads bounded at 10 s.

*Advisory A-1 (green-side flake risk, not vacuity):* the withheld-incident sibling loop
(`ChannelBridgeService.WithheldIncident.cs:101`) also registers 10 s timers. Its first timer
is registered after its own DB scan; if that scan finishes after wake pass 1 completes
(i.e. it takes longer than the ~1 s busy lock wait, possible on a saturated host) the
recorder sees a `(10 s, passes=1)` sibling timer that matches the `due == 5 || due == 10`
predicate, and `retry.ShouldBe(5 s)` would fail on correct code. Not observed in the two
green runs here. A tighter filter (e.g. record the creating stack/callback target, or have
the matcher prefer the wake loop's timer) would remove it; no change needed to kill W7.

**W8 (stall-check startup).** Holds the wake worker's first grouped scan with a one-shot
`DbCommandInterceptor` (stall-check query is a different, ungated query), then requires
the one-minute checker timer, a scheduled incident after 5 fake minutes, dedupe after a
further minute, re-arm after the released pass, and a second incident while the next pass
is held (`CompletedDrainIterations` still 1). It never calls `CheckWakeWorkerStallAsync`
directly, so only the hosted checker can satisfy it. Deterministic: fake time; real waits
are the 10 s gate/timer bounds and an 8 s 25 ms-poll `WaitForAsync`.

*Advisory A-2 (diagnostic):* the W8 red surfaces as a bare `OperationCanceledException`
from the timer recorder, with no label naming the missing stall checker. A
`ShouldNotThrow`/labelled timeout message at line 139 would make a future red self-explaining.

## Cleanup

`server/Application/Services/ChannelBridgeService.cs` restored with
`git checkout -- <exact path>` (status clean before the green rebuild). No product or test
file in the subject branch was changed; this document is the only commit on
`feat/card-task-fada17c6`. Build outputs under the worktree's `bin/`/`obj/` were deleted
after the final run; TRX/logs stayed in the session scratchpad.
