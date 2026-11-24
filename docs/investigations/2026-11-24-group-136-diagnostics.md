# Group 136 handle-probe exit diagnostics

Windows rc run `acca11ba` at `6ed4cb58cd75fe386e94965165d96752452431a8`
failed `ScriptHarnessWindowsOwnershipTests.Only_standard_handles_are_inherited`.
The PowerShell root lived 645.8 ms, within both five-second fixture budgets.
The run did not retain the exit status, child output or receipt publication state.
See the [native evidence](2026-11-24-group-136-native-evidence.md) and
[why-missed write-up](2026-11-24-why-missed-script-harness-windows-ownership-root-exit.md).

## Launch and readiness

The Windows owner supplies the three child pipe ends explicitly, launches the root
suspended, assigns the private job and synchronously writes the probe input before
resuming the root. The parent then closes its copies of the child pipe ends. That
leaves the child's handles installed and gives it EOF on stdin. The old PowerShell
invocation used `-NoProfile -NonInteractive -File`, and its probe had no stdin
read. Current source launches the staged C# fixture directly with `dotnet`, so
PowerShell profile and execution policy are absent from this path.

The observer directory is unique to the invocation. The root writes
`probe-receipt.tmp` and then renames it to `probe-receipt`. That ordering prevents
the parent from reading an in-progress receipt. A filesystem failure or a native
probe error, including in the unrelated-event check, could still exit before
publication. The 2026-11-24 evidence does not identify which operation failed.

Readiness now uses the retained process handle to confirm actual exit independently
of cancellation of the fixture's wait task. A live root that reaches the existing
readiness deadline still produces a timeout. Each case launches once. The first
published receipt from a live root still goes through the same handle assertions
and an unrelated-event signal fails that invocation. A startup retry would lack a
reliable way to distinguish a transient startup failure from an error in the
inheritance probe before publication.

## Next Windows rc

From the next rc checkout that contains this change, the exact native selection is
one method with one expected pass and zero skips. Record the candidate SHA and keep
the TRX and attachment directory. For a focused rerun when the Windows slot is
available, use the checkout's native Debug output.

```powershell
git rev-parse HEAD
$evidence = New-Item -ItemType Directory -Path (Join-Path $env:TEMP ('group-136-w7-' + [guid]::NewGuid().ToString('N')))
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group-136-w7-build -- dotnet build tests/Antiphon.Tests
if ($LASTEXITCODE -ne 0) { throw 'Group 136 witness build failed.' }
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group-136-w7-native -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/ScriptHarnessWindowsOwnershipTests/Only_standard_handles_are_inherited' --report-trx --report-trx-filename group-136-w7.trx --results-directory $evidence.FullName --diagnostic --diagnostic-output-directory $evidence.FullName --detailed-stacktrace
```

If the root exits before the probe completes, the test log and TRX will show
`InvalidOperationException: Root exited before handle probe completed.`, followed
by the PID, exit code, native lifetime in milliseconds, UTC process start time,
argument vector, and the existence of the final receipt, temporary receipt and
first-instruction marker.
Each output stream reports its drain status and up to its last 4,096 characters.
The same message is written to `handle-probe-<nonce>.txt` in the run's results
directory and registered as a test artifact before scratch cleanup. A leaked
handle still fails the inheritance assertion. Linux witnesses cover the early
exit evidence and assertion behavior with a synthetic receipt. The Windows
process and kernel handle checks are owed in the next rc.
