# Why missed: g111 CheckpointStepDeadline startup witness (Windows teardown)

rc run 34d41b7e (rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4), chunk
`antiphon-all-group-111`, native Windows desktop, 2026-11-14 15:08:45Z:
`CheckpointStepDeadlineTests.step_child_that_never_finishes_a_step_times_out_naming_startup`
failed in 4.52 s. The test body passed; the failure is the `[After(Test)]` hook:

```
After(Test) hook failed: checkpoint fixture teardown failed:
C:\Users\lndco\AppData\Local\Temp\c723-01a1da9a930d78dda0921a617bf56c9c: The process cannot access the
file '\\?\C:\Users\lndco\AppData\Local\Temp\c723-01a1da9a930d78dda0921a617bf56c9c' because it is being
used by another process.
  at CheckpointTestScope.DisposeAsync(Exception originalFailure) CheckpointTestScope.cs:189
```

## Classification

TEST FIXTURE/TIMING DEFECT, Windows and load sensitive. Not a defect in `CheckpointStepDeadline`
and not a product defect.

- The deadline logic worked: the run was cancelled at the 3-s startup budget (3 s + up to 200 ms
  polling + kill, 4.52 s total) and every assertion in the body (TimeoutException, "before the first
  step finished", elapsed < 30 s) passed; the three FakeTimeProvider siblings passed in 0.3-0.5 ms.
  `CheckpointStepDeadline` is a test helper (tests/Antiphon.Tests/Checkpoints), not tool code.
- The test started `pwsh` with `WorkingDirectory` = the scope's temp root. Windows keeps a process's
  current directory open, so the root cannot be removed while any process has it as its current
  directory. `pwsh` started with `CreateNoWindow` gets a `conhost.exe` child that inherits that
  directory. After `CheckpointProcessRunner` terminates the job and `pwsh` reports `HasExited`, the
  console host is still exiting.
- Standalone measurement on this desktop (same start info, job object, TerminateJobObject, then
  `Directory.Delete(root)` at once): 20/20 and 9/10 immediate deletes failed with the same "being used
  by another process" message; the directory became removable 15-60 ms after `pwsh` exited when
  idle, and `conhost.exe` was still alive about 200 ms later in 2 of 6 instrumented iterations.
- The real test has more code between the child's exit and the teardown delete (exception
  propagation, assertions, hook start, root inventory), so it passes idle (0/3 failures on this
  desktop, before and after adding the immediate-delete check) and fails only when the host is
  loaded. Group 111 at the time: 126 tests in parallel, p95 10.1 s, max 61.8 s, 12.9 s of process
  spawn time.
- The test does not measure real elapsed time against a tight tolerance: its only wall-clock check
  is `< 30 s` against a 3-s budget (10x headroom). Timing is not the cause.

## Fix (master)

The child's working directory is now `CheckpointFixtures.RepoRoot`, as in the validator corpus
child that `RunStepsAsync` was written for, so nothing the child or its console host holds is under
the fixture tree. The test also deletes its fixture subdirectory immediately after the cancelled run
returns (`Should.NotThrow`), which guards the property the teardown needs. No timeout or budget
changed. On Linux nothing changes (no console host; `pwsh` runs in its own session and is killed by
process group).

Not changed: `scripts/lib/CheckpointProcessRunner.cs` returns when the direct child has exited; on
Windows the console host can still hold the child's working directory for tens to hundreds of ms.
No product caller deletes a cancelled child's working directory at once, so this is recorded here
rather than changed.

## Introducing commit/card

introducing commit/card: 477b7a501ef550fed48f7d14f265dbbe396c68b8 (cherry-pick of 9c37fb9ae,
"witness a slow-starting progressing fixture child", rc-g109 repair for rc run 97150a0b) and
3dcd63199 (moved the witnesses into `CheckpointStepDeadlineTests`); Debug task 7bc00f17, no card
number (rc failure repair).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran: none. Under the
standing rule "rc fixes direct to rc, no review" the Debug task landed directly. Its own selection
was the `CheckpointStepDeadlineTests` and `CheckpointSourceExecutionTests` classes on the Windows
desktop, run while that host was not loaded at the moment of this test; the test passed.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was inside the class
selection and the platform; it was outside the load profile. The race loses only when the host
is busy enough that the console host's exit outlasts the time between the child's exit and the
teardown delete. Nothing checks that a fixture child's working directory lies outside the
directory the fixture deletes.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Checkpoints/CheckpointFixtureProcess.cs`,
`tests/Antiphon.Tests/Checkpoints/CheckpointStepDeadline*.cs`, `scripts/lib/CheckpointProcessRunner.cs` ->
`/*/Antiphon.Tests.Checkpoints/(CheckpointStepDeadlineTests*)|(CheckpointSourceExecutionTests*)|(CheckpointFixtureProcessTests*)/*`

That row selects the class but would usually pass idle. The check that catches this cause is a
review rule: a test that starts a process with `WorkingDirectory` under a fixture root that its
teardown deletes must either wait for the process tree to be gone or run the child from a directory
outside that root.
