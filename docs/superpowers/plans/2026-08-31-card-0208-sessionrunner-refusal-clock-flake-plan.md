# CARD-0208 — Stabilize the SessionRunner refusal-clock test without weakening its contract

**Date:** 2026-08-31
**Status:** Plan only — investigation complete; no product code changed.

## Finding and decision

The intermittent failure is a test-harness timing defect, not evidence that
`CodexTranscriptTailer` starts the refusal clock from the child process start time.  The tested
tailer records `refusingSince` on its first locate-loop observation after `SessionInputLog.Append`.
It then emits `UnboundSeconds` after the injected 400 ms delay, with a normal 250 ms locate poll
between observations.  In a busy full assembly, thread-pool/GC/process-launch scheduling can delay
the second observation after that correct clock has begun.  The current fixed `1.5`-second upper
bound therefore rejects a legitimate delayed observation even though the measured duration starts
at the input.

The test must retain a bound: a start-from-child regression would make `UnboundSeconds` include the
1.3 seconds deliberately spent waiting before the input.  Capture a `Stopwatch` immediately after
the input is appended and compare the reported duration to that measured post-input elapsed time
instead.  Retain the 0.3-second lower bound, which proves that the injected 400 ms grace was
honoured.  Use `elapsedSinceInput + 100 ms` as the upper bound: it admits scheduler delay before
the report, but cannot admit time that predates the input.

The full assembly also lacks the sibling `Antiphon.Agents.Pty.Tests` contention control from
CARD-0128.  Its real process/pty-host tests currently have only unrelated `NotInParallel` groups,
which do not serialize the complete population of launchers with one another.  Add the same
assembly-local, one-wide `ParallelLimiter<ProcessSpawnLimit>` to the audited launcher classes.  It
reduces concurrent host, child, PowerShell, and dummy-process pressure without serializing ordinary
tailer/unit tests.  The focal tailer test remains free to run with normal tests; the measured upper
bound, rather than serial execution, is what makes its assertion truthful.

Do **not** use a new broad `NotInParallel` group, increase the 400 ms product/test grace, change
the tailer poll interval, or make any production `CodexTranscriptTailer` / `TranscriptTailer`
change.  A broad group would mask the timing assumption and needlessly serialize non-launch work.

## Current evidence

- CARD-0208's historical evidence remains the strongest reproduction: the test failed in two
  234-test CARD-0163 branch full runs and in one of two 226-test plain-master full runs, while three
  isolated runs passed.  That establishes intermittent suite contention rather than a CARD-0163
  regression.
- This planning pass did not reproduce it: three isolated target invocations and one current full
  `Antiphon.SessionRunner.Tests` invocation completed with exit code 0.  The captured TUnit runner
  output did not include an aggregate test count, so this pass must not replace the card's recorded
  226/234 counts with an invented value.
- `CodexTranscriptTailer.MaybeReportRefusal` computes `UnboundSeconds` as `UtcNow - refusingSince`;
  `refusingSince` is assigned only after `InputDelivered` is true and the current locate verdict has
  refusals.  `DefaultLocatePollInterval` is 250 ms.  This code path explains both the valid
  post-input lower bound and full-suite overshoot.
- `TranscriptAdoptionSafetyTests` has the Claude-tailer's mirrored refusal-clock test with the same
  400 ms delay, 1.3-second pre-input wait, 5-second event timeout, and fixed `0.3..1.5` assertion.
  It needs the same correction so the equivalent flake cannot simply move providers.
- CARD-0128 established that a one-wide `IParallelLimit` is the appropriate per-assembly control
  for real launchers.  It limits launchers relative to launchers; it is not a substitute for a
  semantically accurate wall-clock assertion.

## Implementation slices

1. **Make both refusal-clock assertions measure the correct interval**

   In `tests/Antiphon.SessionRunner.Tests/CodexTranscriptTailerTests.cs`, update
   `First_input_starts_the_refusal_clock_from_the_input_not_the_child_start`:

   - Append the input exactly as today, then start a `Stopwatch` immediately, before inspecting
     `UnboundReason` or awaiting the fault.
   - Keep the 5-second `WaitForAsync` timeout and the lower `UnboundSeconds >= 0.3` assertion.
   - Replace the literal 1.5-second maximum with
     `elapsedSinceInput.TotalSeconds + 0.1`, and explain in the assertion message that this allows
     only post-input scheduling time.  Do not use `Task.Delay` measurements or start the stopwatch
     before `Append`, because either would reintroduce a false pre-input allowance.

   Apply the exact same pattern to the corresponding Claude-tailer's test in
   `tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs`.  Keep the two tests as
   provider-specific contract coverage; do not consolidate them or alter their stale-transcript
   setup.

2. **Introduce an assembly-local launcher limiter**

   Add `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimit.cs`, matching the sibling assembly:
   a public `ProcessSpawnLimit : IParallelLimit` with `Limit => 1` and a short explanation that the
   type protects real OS child/pty-host launch tests only within this assembly.

   Add `[ParallelLimiter<ProcessSpawnLimit>]` in addition to any existing, more specific
   `NotInParallel` attribute on these classes, each of which currently starts a real process
   directly or reaches the pty-host launch path:

   - `DaemonLogRotationTests`
   - `FirstWriteRaceTests`
   - `HerdrAdoptionSweepTests`
   - `HerdrAttachTests`
   - `HerdrPaneChildKillTests`
   - `PtyBackendSeamTests`
   - `PtyHostAdoptionTests`
   - `SessionBufferBoundsTests`
   - `SessionCpuWatchdogTests`
   - `SessionLivenessTests`
   - `TranscriptAdoptionSafetyTests`

   `HerdrAttachTests` is included because the current source now directly starts a dummy OS
   process; it did not exist in the earlier CARD-0128-style SessionRunner scope.  Do not tag
   fake-named-pipe-only Herdr suites (`HerdrClientTests`, `HerdrEventPumpTests`,
   `HerdrLaunchShapeTests`, `HerdrRunnerSessionTests`, and `HerdrStatusPushTests`), pure registry
   tests, or assembly fixtures.  Re-audit a test if its launch shape changes rather than applying
   the limiter by filename convention.

3. **Pin the limiter population and the test-safety rule**

   Add `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests.cs`.  Its explicit expected
   `Type[]` must be the eleven classes above; inspect each class for
   `ParallelLimiterAttribute<ProcessSpawnLimit>` and assert exact set equality.  This prevents a
   future real launcher from silently escaping the cap and prevents indiscriminate spreading of the
   attribute to the complete assembly.

   In `AGENTS.md`, extend the **Tests and builds** guidance alongside the existing TUnit command
   rule to state explicitly that process-spawning tests, including SessionRunner pty-host and
   direct-process tests, carry an assembly-local `ParallelLimiter<ProcessSpawnLimit>`.  This makes
   the CARD-0128 rule discoverable for this second assembly; it must say “per assembly,” not imply
   a single limiter shared across test projects.

## Validation for the later code pass

1. Run both focused refusal-clock tests through TUnit, using an isolated output directory if a
   standing local process has the normal output locked:

   ```powershell
   dotnet run --project tests/Antiphon.SessionRunner.Tests -- --treenode-filter "First_input_starts_the_refusal_clock_from_the_input_not_the_child_start"
   ```

   Confirm each retains the semantic failure mode by temporarily reasoning through (or adding a
   narrowly scoped local mutation for) a child-start clock: its roughly 1.3-second pre-input
   duration must exceed the new measured upper bound.  Do not commit such a mutation.

2. After slice 1, run `Antiphon.SessionRunner.Tests` clean ten times sequentially.  Record each
   aggregate test count and every failure; a pass is not just an exit code when runner output is
   available.  This distinguishes a corrected assertion from a remaining contention problem.

3. After slices 2 and 3, run the full `Antiphon.SessionRunner.Tests` clean at least three more
   times sequentially.  Verify zero failures, no orphan child/pty-host process, and no global
   serialization of non-launch tests.  If failures remain, capture the actual `UnboundSeconds`,
   elapsed-since-input, machine load, and concurrently running launcher class before widening any
   threshold again.

4. Run the repository-required test lanes sequentially, never through `dotnet test`:

   ```powershell
   dotnet run --project tests/Antiphon.Tests
   dotnet run --project tests/Antiphon.Agents.Pty.Tests
   ```

   Use the isolated-output and process-safety procedure in `docs/testing-and-build.md` if normal
   `bin/` paths are locked.  Do not run those two assemblies concurrently.

## Explicit non-goals

- No production-tailer, polling, refusal-delay, child-start, input-log, or runner-runtime behavior
  change.
- No unconditional 1.5-second-to-larger-literal widening, test skip, retry-only test policy, or
  removal of the upper bound.
- No suite-wide parallelism reduction, global TUnit setting, or broad `NotInParallel` group.
- No change to the existing test's stale transcript evidence, pre-input delay, reason assertions,
  or five-second event-delivery timeout.
