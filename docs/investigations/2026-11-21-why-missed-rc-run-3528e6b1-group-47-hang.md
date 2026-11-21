# Why missed: rc run 3528e6b1 group-47 hung for an hour with no TRX

One new rc-chunk cause. Qualification run `3528e6b1`, rc/2026-11-30 tip
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`, Windows, chunk
`antiphon-all-group-47`. The chunk started 15:27:48Z, wrote TUnit diagnostics
until 15:31:11Z, then nothing until the supervisor's one-hour chunk timeout at
16:27:50Z. `timing.json` recorded `why=timeout`, `runWallSeconds=3601`,
`buildSeconds=493`, `testCount=0`, `trxMissing=true`. The hang capture
`antiphon-all-group-47-tests.log.hang.txt` (354 KB) is on the desktop and was
not read here. The dispatch reports its first line as the test-host root with
`active=2` and about 90 descendant `git` / `conhost` processes, all exited
except one `conhost`, and the last diagnostic node in
`ReviewEvidenceResettlementTests`.

## What hung

`active=` in that capture is the Windows job's active process count
(`NativeProcessOwner.Count`, `JOBOBJECT_BASIC_ACCOUNTING_INFORMATION`), not a
count of TUnit tests. Two active processes is the test host plus the surviving
`conhost`. `ReviewEvidenceResettlementTests` is `[NotInParallel("MessageQueue")]`,
so only one of its methods runs at a time. This checkout cannot see the
diagnostic file, so a second in-flight method in the same host is not named.

The class has ten tests and no `[Timeout]`. The waits it writes itself use a
30-second `WaitAsync`. The unbounded wait is `LandingGit.ExecuteProcessAsync`:
after `WaitForExitAsync` observes a normal exit it awaits both redirected
reads with no second deadline. That is deliberate for a grandchild that closes
the pipe a few seconds later (`C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget`,
an 8-second holder). A `conhost` that inherits the pipe and never exits keeps
`ReadToEndAsync` pending for the rest of the chunk. The budget kill path is
already bounded (`git_timeout` plus `KillDrainGrace`); it does not run once the
root has exited. `git-receive-pack` and `sh` in the capture are the local push
this class performs through that helper.

On Linux at `e4243def97326dfc8edf05df08d5dd4a3a07c068` the class does not hang.
One build-slot run, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `--timeout 570s`,
Testcontainers Postgres (not production): 10 passed, 0 failed, 0 skipped,
session duration 54.079s.

| Test | Duration |
|---|---|
| `C1043_ConfirmedNoPushBinds` | 13.164s |
| `C1043_FinalReportWins` | 7.318s |
| `C1043_AppendPreservesHistory` | 0.995s |
| `C1043_HeaderSnapshotAndGetAgree` | 2.400s |
| `C1043_ContinuingSyncFailure` | 0.701s |
| `C1043_AutomaticEligibility` | 5.748s |
| `C1043_SettlementsSerialize` | 1.662s |
| `C1043_PreparationBeforeLock` | 0.947s |
| `C1043_SettlementRollback` | 1.732s |
| `C1082_PendingContinuationRefusesStrictRebind` | 0.878s |

`scripts/lib/nightly-class-costs.json` records this class as measured, not a
hang: 10 nodes, 358 serial test-seconds, run `79b742de`, chunk `all-group-33`.
There is no `tests/slow-tests*` quarantine entry. The class is `[Category("Slow")]`
and `[Category("Integration")]`.

The repair bounds that success-path drain at `LandingGit.SuccessPipeCloseBound`
(30 seconds). The call then fails with `TimeoutException` message
`git_pipe_timeout` and abandons the pipes the same way as the kill path. A
holder that closes inside the bound still succeeds, so the 8-second
CARD-1076 case stays green. `HungPipeHolderFailsWithGitPipeTimeout` is the
Linux witness: a double-forked sleeper that does not exit.

## Why the 20-minute idle budget never fired

`scripts/qualification/supervise-rc.ps1` writes the heartbeat the dispatch
saw. `Update-QualificationSupervisorHeartbeat` is called with
`-PlannedSeconds $null` and `-DefaultIdleMinutes` equal to `-HangMinutes`
(default 20). `Get-QualificationHeartbeatIdleBudget` therefore always returns
20 minutes and `default-unknown-group`. `Get-QualificationGroupPlannedSeconds`
is not called. `-DiagnosticProgress` is not passed, so a silent diagnostic
log does not affect the flag.

`hangSuspected` becomes true only when a sample is measurable and all of the
following hold for the whole budget: launch-tree CPU under 5 cpu-seconds per
minute, the count of `.trx` files under `LogRoot` and `StateRoot` unchanged,
and the sum of every file length under those two roots unchanged.
`ChunkRootPid` is the `launch-rc.ps1` process, not the current chunk's test
host. CPU is the sum of that process and every live descendant
(`Get-QualificationProcessTable` / `Get-QualificationHeartbeatSubjects`).

While group-47's test host sat in the pipe read, the chunk owner was still in
`NightlyCompletionGrace.Wait` with a 10ms poll that calls `Count` and
`CaptureDescendants` on the job (`NativeProcessOwner`, the `10` argument).
That owner is inside the launch tree. Its poll of a job that had held about
90 processes keeps the tree above 5 cpu-seconds per minute, so `quiet` stays
false, `idleSinceUtc` never accumulates, and the 20-minute budget never
starts. Exited `git` processes drop out of `Win32_Process`; the polling owner
does not. Any growth of any file under the run's log or state root resets the
same clock. The diagnostic stopping at 15:31:11Z is invisible to this
supervisor.

A true `hangSuspected` only appends `HANG_SUSPECTED` and
`hang-suspected.txt`. The supervisor never stops a process. The chunk still
runs until the one-hour watchdog, which is the timeout this run hit. That is
why `hangSuspected` stayed false for the whole hour.

## How the supervisor should treat this chunk

Card text for the follow-up: when the current chunk's test-host process (the
job root, not the launch tree and not the owner's 10ms census) stays under a
small CPU delta and that chunk's own diagnostic offset and TRX are unchanged
for 20 minutes, fail the chunk. Write the hang capture and stop that job.
Do not let another file under `LogRoot`/`StateRoot`, or the owner's poll, reset
the idle clock. Pass the active chunk's planned seconds into the budget when
`active-chunk.json` proves them; keep the 20-minute unknown-group budget only
when they are absent. Leave a host that is still appending diagnostic lines
alone. Today the flag is observation-only, so the same hang still waits for
the hour watchdog even after `hangSuspected` becomes true.

## Introducing commit/card

introducing commit/card: `90dd667ab1c0ecc38cbcd431548f59f83b6540da` (no card id
in the message; cherry-picked as `f9de5cfd7ef7a43f6dd6a432ac5efc683e52d450`).
Message: `fix: keep the git child journal until an abandoned pipe closes`.
The paired test commit is `ab7f8fd7de6d2991797af580f1eb9733856a94fa`
(`76d42a663372f385122898b4ebb1f4a0bcfbea0a` on this line). It requires a
successful return after an 8-second holder and does not cover a holder that
never exits.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No review commit in git names `90dd667ab` or `ab7f8fd7`. The behavior landed
  as those two commits from `antiphon-server2-runner` on 2026-10-29. A review
  that ran `C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget` would
  still miss this hang: that test expects the call to stay in flight and then
  succeed.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the
Windows Full rc lane, categories Integration and Slow,
`tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs`. The
pipe witness that existed is a Linux double-fork of a holder that exits.
Nothing in that selection starts a grandchild that keeps the pipe open past
the chunk watchdog.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Git/LandingGit.cs` -> `/*/*/LandingGitTests/HungPipeHolderFailsWithGitPipeTimeout*`

## Exemption

same cause as an already written note: none.

`docs/investigations/2026-11-10-why-missed-session-runner-group4-post-completion-hang.md`
is a different cause. That chunk had a completed TRX and `total ran` before
the watchdog. This chunk wrote no TRX; the host was still inside one test.
