# Why missed — Windows script harness root before handle probe

Windows Full run `acca11ba` on `rc/2026-12-04` at
`6ed4cb58cd75fe386e94965165d96752452431a8` reported one failure in
`antiphon-all-group-136`. `ScriptHarnessWindowsOwnershipTests.Only_standard_handles_are_inherited`
threw `InvalidOperationException: Root exited before handle probe.` The cause is unresolved.
The exception establishes that the probe receipt was absent when checked and the root wait task
had completed by the next check. Both an early child exit and cancellation of the wait fit those facts.

## What the pinned fixture observed

The native test first checks that `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` contains exactly the child
stdin, stdout and stderr handles. The launch adapter creates a suspended PowerShell child and
assigns it to a private job, then calls `BeforeResume`. That hook writes the nonce, private job
handle and unrelated event identity to `probe-input` before the adapter resumes the root. The
`HandleProbe` case loads the staged `Antiphon.ScriptHarnessHost.dll` and calls `WriteReceipt` inside
that root. The probe atomically publishes `probe-receipt` and the successful PowerShell path stays
alive for 20 seconds before returning 0. The parent waits for the receipt, then checks the root's
retained process handle, the event state and all three reported standard handles.
[Test](../../tests/Antiphon.Tests/Scripts/ScriptHarnessWindowsOwnershipTests.cs) ·
[Launch adapter](../../tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs) ·
[Probe implementation](../../tests/Antiphon.ScriptHarnessHost/Program.cs)

`DirectOwner` starts a five-second cancellation timer before fixture setup and native launch.
`WaitProbeAsync` starts its own five-second stopwatch later. It checks `RootExit.IsCompleted`
before checking that stopwatch. The earlier timer can cancel the root wait while the child is
still alive, and `CaptureAsync` would return that cancellation as the inner exception. If the
process actually exits, `RootExit` returns its integer exit code and `CaptureAsync` discards it.
The direct fixture drains both child streams without retaining their text and deletes its observer
directory during cleanup. The usual successful probe branch emits no stdout or stderr.
[Direct fixture](../../tests/Antiphon.Tests/Scripts/ScriptHarnessWindowsProcessFixture.cs) ·
[Capture helper](../../tests/Antiphon.Tests/Scripts/ScriptHarnessProcessFixture.cs)

The desktop artifacts were unavailable in the Linux mirror and its evidence directories. The
source is `C:\Antiphon\releases\candidates\rc-2026-12-04-6ed4cb58cd75\logs\2026-11-24-0922-acca11ba\`
with `antiphon-all-group-136.trx`, `antiphon-all-group-136-tests.log` and
`antiphon-all-group-136-execution`. The dispatch supplied the outer exception only. Child stdout,
stderr, exit code, lifetime and any inner exception remain unobserved. The TRX test duration,
if present, covers test setup and cleanup as well as the probe. It cannot establish child lifetime.

## Introducing commit/card

`0297736580f3ed1e58e14f2222e4e3e3b68d2d0e` (CARD-1047 S2) introduced W7 and the
ambiguous early-exit diagnostic. The commit that introduced this run's failure is unknown.
The later CARD-1047 repair `be16e6c35b5fd9ced3622c2b3e313ae836f416f8`, already in the
rc pin, compares the underlying event identity to account for process-local handle numbers.

Between the rc pin and the inspected master `fb0538b7609f7a2ef7b49eec2642d26c2bc04cb1`,
unit 35 (`f3d1f8338713bb47474806bd3010772f9b366b3f`) replaced `pwsh -File` with
`dotnet <staged Antiphon.ScriptHarnessHost.dll>`. The port removes the PowerShell assembly-load
step and calls the same probe from `HarnessRoot`. The W7 assertions, before-resume input write,
receipt and cancellation logic are unchanged. The port postdates the failing pin. Whether master
reproduces the failure still requires native execution.
[Port record](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)

## Approving reviews

No approving review ID, scope or executed selection for W7 at the failing tip was verified.
The CARD-1047 plan included W7 in Windows-native CP-2 and final CP-5. The checked-in CP-1
comparison shows 14 passes but includes only W1–W3 from the Windows ownership class.
[CARD-1047 plan](../superpowers/plans/2026-10-04-card-1047-windows-script-harness-plan.md) ·
[CP-1 comparison](2026-10-05-card-1047-cp1-comparison.md)

## Outside the selection

`tests/Antiphon.Tests/Scripts/ScriptHarnessWindowsOwnershipTests.cs` is `Integration` and
`Slow`, with an assembly-local `ProcessSpawnLimit`. Its `[Before(Test)]` hook skips the class
on Linux. Even adding the exact method to a Linux roster or a short Linux Merge witness would
only produce a skip. Merge's build checks compilation and its selected witnesses check the
executing platform. The inheritance assertion requires a native Windows launch and job.
[Merge verification scope](../orchestration-loop.md#merge-verification-is-short-2026-11-18)

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/ScriptHarnessWindows*.cs`,
`tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs`,
`tests/Antiphon.Tests/Scripts/ScriptHarnessProcess*.cs` and `tests/Antiphon.ScriptHarnessHost/**`
→ `/*/*/ScriptHarnessWindowsOwnershipTests/*` for native Windows impacted-class selection.
Changes to the pinned PowerShell fixture also warrant the same selection while that fixture is
still in use. For this incident, the exact method below is the first witness. A load witness is
needed to claim a load-dependent failure if the isolated run passes and the original or new
diagnostics point to the wait deadline. Measure launch-to-first-instruction and
first-instruction-to-receipt under controlled Windows load. The reported AppHost restart is
timing context. The original exception alone cannot establish a relationship to host load.

## Native confirmations owed

1. Read the original three artifacts from the desktop or a read-only copy. Extract the W7 result,
   inner exception and any child diagnostics. Distinguish wait cancellation from a completed wait
   with a process exit code. If the artifacts omit process data, capture bounded child
   stdout and stderr, the retained handle's signaled state, exit code if signaled, and process
   lifetime before teardown in the native reproduction. Record first instruction and receipt times
   where available.
2. After the live qualification, use a native Windows checkout at the current master SHA and
   record `git rev-parse HEAD`. Run exactly one method behind the build slot and retain the TRX
   and execution diagnostics. A clean execution is one pass, zero failed and zero skipped. If it
   passes and the original failure remains unexplained, reuse only this filter in a separate
   checkout at the original pin to distinguish the PowerShell and C# paths before concluding
   that the port changed the outcome.

   ```powershell
   $evidence = New-Item -ItemType Directory -Path (Join-Path '.antiphon' ('w7-' + [guid]::NewGuid().ToString('N')))
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label w7-windows-native -- dotnet run --project tests/Antiphon.Tests -- --treenode-filter '/*/*/ScriptHarnessWindowsOwnershipTests/Only_standard_handles_are_inherited' --report-trx --report-trx-filename w7.trx --results-directory $evidence.FullName --diagnostic --diagnostic-output-directory $evidence.FullName
   ```

No test code changed. The candidate failure and master verdict remain open pending the native
evidence. A Linux fake could show how a completed task is handled, but could not identify which
task state occurred in this run or exercise the Windows inheritance assertion.

## Exemption

None. The cause remains unresolved, so no same-cause exemption is claimed.
