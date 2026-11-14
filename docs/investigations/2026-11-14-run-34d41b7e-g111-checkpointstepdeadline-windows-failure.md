# Run 34d41b7e, group 111: CheckpointStepDeadlineTests Windows failure (2026-11-14)

Read-only extract. Run 34d41b7e (Windows Full qualification on the desktop) of
`rc/2026-11-20` = `b559bfba2e9a420495122d27171352fe9b7e1ba4`, chunk `antiphon-all-group-111`.
Sources read: `antiphon-all-group-111.trx`, `antiphon-all-group-111-tests.log`,
`antiphon-all-group-111.timing.json`, `antiphon-all-group-111-execution/log_261114150810892.diag`,
and the chunk's owner timing journal
`timing-34d41b7e3ccc4ad4b29ed259749ec847/owner-antiphon-all-group-111-34d41b7e3ccc4ad4b29ed259749ec847-1.json`
(all under `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\`). Source is quoted from
`b559bfba2`. Nothing was changed on the desktop.

## Verdict

**Not a defect in `CheckpointStepDeadline`, and the test body passed.** The test body
passed. The failure is the `[After(Test)]` checkpoint-scope teardown. The teardown could not delete the
test's temp root because the test's own `pwsh` child still had that directory as its working
directory. The child had been cancelled and killed, but the kill had not finished yet. This is a
Windows-only, load-sensitive cleanup race in the test fixture, not a timing failure in the deadline
logic.

## Result

Chunk totals: 126 tests, 125 passed, 1 failed. Run wall time 137.9 s; p50 0.176 s, p95 10.09 s.

| Test | Outcome | Duration | Start (UTC) | End (UTC) |
|---|---|---|---|---|
| `CheckpointStepDeadlineTests.step_child_that_never_finishes_a_step_times_out_naming_startup` | **Failed** | 00:00:04.5239779 | 15:08:45.708 | 15:08:50.232 |

The exact failure message from the TRX and tests.log has no expected/actual pair, because it is not an
assertion:

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TestExecutionException: After(Test) hook failed:
checkpoint fixture teardown failed: C:\Users\lndco\AppData\Local\Temp\c723-01a1da9a930d78dda0921a617bf56c9c:
The process cannot access the file '\\?\C:\Users\lndco\AppData\Local\Temp\c723-01a1da9a930d78dda0921a617bf56c9c'
because it is being used by another process.
 --->  System.IO.IOException: checkpoint fixture teardown failed: ... being used by another process.
    at Antiphon.Tests.Checkpoints.CheckpointTestScope.DisposeAsync(Exception originalFailure) in ...\tests\Antiphon.Tests\Checkpoints\CheckpointTestScope.cs:189
    at Antiphon.Tests.Checkpoints.CheckpointTestBase.CloseCheckpointScopeAsync() in ...\tests\Antiphon.Tests\Checkpoints\CheckpointTestScope.cs:224
    at TUnit.Engine.Discovery.ReflectionHookDiscoveryService....CreateInstanceHookDelegate...
    at TUnit.Engine.Helpers.HookTimeoutHelper....CreateTimeoutHookAction...
    at TUnit.Engine.Services.HookExecutor.ExecuteAfterTestHooksCoreAsync(...)
```

- The only failure is the After(Test) hook. TUnit reports no body exception, so these body
  assertions held:
  - `ShouldBeOfType<TimeoutException>`
  - `Message.ShouldContain("before the first step finished")`
  - `clock.Elapsed < 30 s`
- The path in the error is the root directory itself, not a file inside it. That is the Windows
  sharing violation you get when you delete a directory that a live process uses as its current
  directory.
- Standard output holds only the `ANTIPHON_TEST_PHASE_ID ...` line. On the timeout path,
  `RunStepsAsync` throws before it prints its `step child:` line, which is expected.
- The diag log (lines 998-1007) repeats the same exception and contains no other lines for this test.
- The root is still on disk and empty. Teardown deleted the marker and then failed on the directory.
  No `pwsh ... Start-Sleep -Seconds 600` process was alive at 15:14:41Z. The child was killed in the
  end, so the holder was transient.

## Why the directory was held: the test's own child

The test, at `b559bfba2`, creates the `pwsh` child with the temp root as its working directory:

```csharp
var root = TempDir();
var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root };   // <- cwd = the scope root
... "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 600"
var error = await Thrown(CheckpointFixtureProcess.RunStepsAsync(start, Path.Combine(root, "progress"), ["first", "second"],
    TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)));
```

On expiry, `RunStepsAsync` cancels `RunAsync`. The Windows cleanup in `CheckpointProcessRunner.RunCoreAsync`
(`scripts/lib/CheckpointProcessRunner.cs`) then waits at most 1 s for the process to exit:

```csharp
if (job != IntPtr.Zero) { TerminateJobObject(job, 128); CloseHandle(job); }
try { if (!process.HasExited) process.Kill(true); } catch ...
// Abandon pipes on expiry; never synchronously join a drain here.
try { if (!process.HasExited) process.WaitForExit(1000); } catch (InvalidOperationException) { }
```

The child is never registered with the checkpoint scope (`RegisterCheckpointChild`), so teardown does
not kill it or wait for it. `CheckpointTestScope.DisposeAsync` then calls
`_io.RemoveDirectory(root, recursive: true)` once, with no retry.

### Kernel timestamps from the owner journal

The owner journal's `descendants[]` rows hold kernel times. `exitedAtUtc` is `Process.ExitTime`, the
`GetProcessTimes` exit time, recorded in `scripts/lib/nightly-owned-process.cs` `StampOwnerObservation`.
These are not polling-observation times.

| pid | image | started (UTC) | exited (UTC) | life |
|---|---|---|---|---|
| 117876 | pwsh (this test's child; the only pwsh in the window that matches) | 15:08:47.010 | **15:08:50.239** | 3.23 s |
| 34108 | conhost (its console host) | 15:08:47.027 | **15:08:50.263** | 3.24 s |

Reconstructed timeline:

1. **15:08:45.708, test starts.** `RunStepsAsync` arms the deadline, so the 3 s startup budget is
   counted from here.
2. **15:08:47.010, `pwsh` is created.** That is 1.30 s after the test started, before the child had
   done anything. Under this load, `Task.Run` scheduling and process creation alone used 1.3 s of the
   3 s budget.
3. **About 15:08:48.7-48.9, the deadline expires.** It expires at 3 s plus the 200 ms poll, cancels the
   run, and the runner terminates the job and kills the process tree. The 4.52 s duration fits the
   pattern "3 s budget + poll + the full 1 s `WaitForExit(1000)` bound + teardown". That pattern
   implies the bounded wait ran out with the child still alive.
4. **≤ 15:08:50.232, teardown fails.** The test body's assertions pass, then teardown tries to delete
   the root and gets the sharing violation. TRX `endTime` (15:08:50.232) includes the After hook.
5. **15:08:50.239 / 15:08:50.263, `pwsh` and then `conhost` exit.** Both exits are 7 ms and 31 ms
   after the test, including its teardown, had already ended.

So at the moment of the delete, the directory's holder was the test's own child. It had been killed
but had not yet torn down.

## What ran at the same time (load)

- The class carries `[ParallelLimiter<CheckpointToolSpawnLimit>]`. Tests overlapping
  15:08:45.7-15:08:50.2 in this chunk:
  - `PhaseJournalKillDiagnosticTests.script_killed_row_prints_the_same_diagnostic` ran 15:08:38.4 to
    15:09:05.4 (27.0 s). Its pwsh pids were 119068 (25.7 s) and 138616 (24.2 s).
  - `CheckpointGitEncodingScriptTests.C1608_legacy_code_page_still_reports_non_ascii_tamper` ran
    15:08:41.1 to 15:08:54.1 (13.1 s), with pwsh 62396.
  - `C1608_invalid_utf8_git_output_is_refused` ended at 15:08:45.70, just before this test started.
- Between 15:08:45.7 and 15:08:50.3 the owner journal records a continuous storm of short `git` and
  `conhost` children. That is roughly 150 processes in 4.5 s, each living 30-470 ms, all from the
  C1608 tests. The host is also running the rest of the Full qualification.
- The 1.30 s gap from test start to `pwsh` creation directly measures that load inside this test.

## Sibling tests that passed (bounding the cause)

`CheckpointStepDeadlineTests`. The pure-logic witnesses use `FakeTimeProvider`, so they are
deterministic and have no wall-clock dependency:

| Test | Outcome | Duration |
|---|---|---|
| `step_deadline_expires_a_silent_startup_as_startup` | Passed | 0.0005 s |
| `step_deadline_keeps_a_slow_starting_child_that_keeps_progressing` | Passed | 0.0004 s |
| `step_deadline_expires_a_stalled_step_and_names_it` | Passed | 0.0003 s |

Other passing tests in the chunk that use the same fixture root, child process and teardown machinery
(`CheckpointTestBase` scope):

- `CheckpointFixtureGitEnvironmentTests` (4/4), including `real_driver_keeps_confinement_and_cancellation` (10.1 s)
- `PhaseJournalKillDiagnosticTests` (4/4)
- `CheckpointGitEncodingScriptTests` (3/3)
- `CheckpointWindowsAnchoredPathDeleteWitnessTests` (5/5)
- `UnsafeDeleteRecoveryTests` (5/5)
- `WaitCommandTests` (11/11)
- `CheckpointFreshOutputSealTests` (3/3)

Together these show that:

- the deadline model is correct on Windows;
- the real-child expiry path produced the right `TimeoutException` text well under the 30 s ceiling;
- the scope teardown works whenever the child has fully exited before the delete.

## Deterministic or timing?

**Timing/load, not deterministic.** Evidence:

- It is a sharing violation in teardown (`IOException ... being used by another process`), not an
  assertion on returned state.
- The holder's kernel exit time (15:08:50.239) is after the end of the test (15:08:50.232). The
  outcome depends on whether the killed `pwsh` finishes teardown inside the runner's 1 s
  `WaitForExit(1000)` bound. Under load it did not.
- It is specific to Windows. On Linux, removing a directory that is some process's cwd succeeds, and
  the runner `SIGKILL`s the process group.

**Does the test measure real elapsed time with a small tolerance?** The body asserts
`clock.Elapsed < 30 s` against a 3 s budget, a 10x margin. That margin is generous and was nowhere near
being hit here (about 4.3 s). The startup budget itself is real time, but because the child never makes
progress, expiry is always the correct outcome. Load can only make it expire later, never wrongly. The
only fragile assumption is implicit: that a cancelled `pwsh` has exited within 1 s, before teardown
deletes its working directory.

## Real defect?

- **`CheckpointStepDeadline` and `RunStepsAsync`'s deadline logic: no defect.** Every assertion about
  deadline behaviour passed, on both the fake clock and the real child.
- **Fixture defect, low severity, Windows-only.** `CheckpointProcessRunner.RunCoreAsync` waits at most
  1 s for a terminated child on Windows. The test sets the child's `WorkingDirectory` to the
  scope root. The scope deletes that root once, without waiting for a child it does not know about. A
  slow-dying child therefore fails teardown. In production the checkpoint tool does not delete a
  fixture root under a live child, so this only affects tests.

## Suggested Debug fix (smallest first)

1. **Test-local fix (recommended).** Do not use the scope root as the child's working directory. Use a
   directory outside the root for `WorkingDirectory`, such as `Path.GetTempPath()` or `AppContext.BaseDirectory`.
   `progressPath` can stay under the root, because the child never opens it. Red witness:
   - Make the test wait on, or reproduce, a child that outlives the 1 s bound, for example a
     `beforeDelete` hook or a slow `pwsh`.
   - Or delete the root while a child whose cwd is the root is still alive, and show the sharing
     violation.
2. **Optional hardening in `RunStepsAsync` (test helper).** After a cancel, wait for the job-terminated
   child to exit with a longer bound (for example 10 s) before throwing `TimeoutException`. This needs
   the PID from the runner, and does not join pipes.
3. Do not add a retry loop to `CheckpointTestScope` root deletion just for this. The scope's custody
   rules deliberately refuse to remove roots under uncertain processes.

## Why it was missed before rc

- The test is new. It was added for g109 on 2026-11-13 (`477b7a501`, moved cross-platform in
  `3dcd63199`).
- Its witness runs were on Linux (server2) or on lightly loaded hosts. There, a killed `pwsh` exits
  within the 1 s bound, and Linux allows deleting a cwd directory.
- This Windows Full run is the first time it ran under heavy concurrent process load. In this chunk
  that load included about 150 git/conhost spawns in the 4.5 s window, from the C1608 tests.
- `origin/master` has no newer change to the test, `CheckpointFixtureProcess`,
  `CheckpointProcessRunner` or `CheckpointTestScope`.
