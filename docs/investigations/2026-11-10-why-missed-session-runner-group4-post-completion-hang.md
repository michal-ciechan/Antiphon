# Why missed: session-runner group-4 stayed green and held the launch watchdog

One new rc-chunk cause. Qualification run `dce78ee0a35a4b08be30d8e0e8ef0295`,
rc/2026-11-14 = `7bee43614495293afbae1b0f4add019d11dacb9c`, chunk
`session-runner` / `all-group-4`, Windows. CARD-1781.
Debug task 4bcf77b3. The Windows read-out is
`docs/investigations/2026-11-10-rc-run-dce78ee0-end-unexecuted-outcomes.md`
on `origin/feat/card-task-276564a2`. The unexecuted rows and the exit-8
chunks in that note are other causes.

## Failure

Chunk `all-group-4` executed 119, passed 119, notExecuted 0, in 8m 23s.
TRX `outcome="Completed"`. The diagnostic tail recorded `total ran: 119`
and `request execute stop`. The test-host process tree stayed alive until
the harness watchdog at 1801.264 s (2026-11-09T22:33:18.7869242Z to
2026-11-09T23:03:20.1053292Z). The row was `timedOut=true`, `exitCode=1`,
`result=FAIL`, evidence reason `process-exit-1`, suite reason
`timeout session-runner`. Slowest test
`C1065_LocalSourceModesVerifyFreshAtRelease`, 175.879 s, in
`WorkspaceSourceVerificationWireTests`. That class does not call
`Process.Start`.

The 1801 s figure is the launch watchdog (suite seconds for an
unmeasured-cost group, plus a little overhead), not the 8-minute test
duration. `NativeProcessOwner` forces `ExitCode = 1` when `TimedOut` is
set, then `ConvertTo-NightlyNativeSuiteVerdict` adds `process-exit-1`.
`Invoke-NightlyScheduledChunk` then records `timeout {suiteId}`.

## What held the tree

`NativeProcessOwner` creates the test host with an explicit handle list,
so the host inherits the stdout and stderr write ends, and assigns it to
a job whose only limit flag is `KILL_ON_JOB_CLOSE` (no breakaway). A child
the host starts without its own handle list inherits those write ends and
stays in the job. The owner waits until the root has exited and the job
active count is 0. A leftover child, or a host that does not exit, holds
the watchdog after TUnit has already reported `request execute stop`.

The desktop note does not quote `RootExitedAtUtc` or descendant images, so
this run does not prove whether the root or a child was the survivor.

Group membership was not in a committed chunk listing. A source-count
first-fit at `7bee43614` (default 0.5 s, max 30 operands; the cost table
has no `Antiphon.SessionRunner.Tests.*` rows) puts group 4 at about 97
source tests against the 119 executed cases. That reconstruction is not a
TUnit discovery replay. Live process starts inside it:

- `BlockedParkWireTests` starts `git` (`Process.Start` at lines 1067, 1077,
  and 1120, the last in a `using`).
- `BuildSlotEndToEndTests` starts `pwsh` (`ProcessStartInfo` at line 167).
- `DaemonLogRotationTests` starts `pwsh.exe` (`using var process`, line 164)
  and is skipped off Windows.

`PtyBackendSeamTests` only mentions `ProcessStartInfo` in a comment.
`WindowsShellGuardAuditTests` quotes `cmd.exe /k` as source text. No
`IsBackground = false` thread exists under `tests/Antiphon.SessionRunner.Tests`.
`SingleThreadContinuationPump` sets `IsBackground = true`.

`PtyHostLeakSweep.After(Assembly)` runs for every filtered session-runner
group, Windows only, and `WaitForExit` is 10 s. That bound is not a
22-minute hang. `CompletedCardRemovalCrashWorker.Before(Assembly)` waits
only when `ANTIPHON_C1032_REMOVAL_CHILD_INPUT` is set.

No Linux fixture reproduced a child that keeps the host stdout open after
`total ran`. The harness change is the diagnosis for the next Windows run,
not a fix of one test class.

## What the gate does now

After `total ran: N` and `request execute stop`, with TRX outcome
`Completed`, `executed == total`, and `notExecuted == 0`, the native owner
waits `NightlyCompletionGraceMilliseconds` (60 s) and then kills the job.
It writes `<log>.hang.txt` before `TerminateJobObject`: root pid, whether
the root has exited, each descendant pid and image, and open handles from
`NtQuerySystemInformation` class 64 (`SystemExtendedHandleInformation`).
Handle names are not queried. A missing or partial TRX, a `total ran`
that disagrees with the counters, or a tail without `request execute stop`
stays on the launch watchdog.

The chunk stays `result=FAIL` and `timedOut=true`. Coverage stays
incomplete and tests stay failed. The outcome class is `TESTS`. The suite
reason is `completed-but-hung {suiteId} children=pid=/image=...`. The
`process-exit-*` reason for the kill this path applied is dropped. A real
`failed-row` reason is kept. The PowerShell capture used when the native
file is absent says `handles=not-enumerated`.

Linux proof of the bound: `Invoke-NightlyCompletionGrace` with `/bin/sleep 30`,
finished immediately, grace 300 ms, watchdog 5000 ms, returned
`CompletedButHung` in under 2 s while the sleep was still alive. The
unfinished case hit the 250 ms watchdog and was not a completed hang.
`scripts/test-nightly-tests.ps1 -Case Group4CompletedHang` is 25/25.
The native owner file compiled as a net10 class library, 0 warnings.
`Add-Type` of that owner remains Windows-only.

## Introducing commit/card

introducing commit/card: `34f81022019a97f6c4284ecd33e4d3bfab043037` (no card id in the message; cherry-pick of `5b9dd4a649bd369d4eb5e9927e4e800a01755323`)

`fix: pair launch watchdogs with admitted cost and record hang progress`.
`Get-NightlyWatchdogTimeoutDisposition` took `TrxPresent` and always
returned `watchdog-timeout`, with coverage and tests failed. The timeout
branch never compared that flag, or the TRX, with `total ran`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none named on `34f81022019a97f6c4284ecd33e4d3bfab043037` or on
  `5b9dd4a649bd369d4eb5e9927e4e800a01755323`. Neither message records a
  review task id or a filter.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Windows Full rc harness, qualification chunk `session-runner` / `all-group-4`, path `scripts/lib/nightly-watchdog-admission.ps1` and `scripts/lib/nightly-tests-impl.ps1`.

The disposition does not read a completed TRX. No script case asserted
`ResultSummary outcome="Completed"` plus `total ran` against the timeout
reason. A review of the admission formula would not launch this chunk.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/nightly-watchdog-admission.ps1` -> `scripts/test-nightly-tests.ps1 -Case Group4CompletedHang`

There is no TUnit class for this gate. `tests/impacted-classes.json` was
not edited. The same case covers `scripts/lib/nightly-owned-process.cs`
and the timeout branch in `scripts/lib/nightly-tests-impl.ps1`. It does
not build `Antiphon.SessionRunner.Tests`.

## Exemption

same cause as an already written note: none.
