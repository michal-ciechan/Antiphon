# Run a6410818 profile chunk 1543: OutputCleanupTests stamp-back assertion fails on Windows

- Run: a6410818 (rc/2026-11-12, candidate `rc-2026-11-12-5fef2c23b894`), Windows desktop `DESKTOP-KTLKPIF`.
- Qualification checkout: `C:\Antiphon\releases\checkout-c9cb914c` at `5fef2c23b89414b8818aaf40db9ad738bce9524c`.
- Chunk: `all-profile-1543` (one-test profile chunk). The requested file name
  `2026-11-08-run-a6410818-group-131-windows-failure.md` already holds the group-131
  ServerRepositoryMirrorScriptTests read-out (commit `cad776695`), so this read-out uses its own name.
  This test is not in `antiphon-all-group-131.trx`; it ran only in `all-profile-1543`.
- Sources read (read-only):
  `C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\logs\2026-11-08-1749-a6410818\`
  `antiphon-all-profile-1543.trx`, `antiphon-all-profile-1543-tests.log`,
  `antiphon-all-profile-1543.timing.json`, `antiphon-all-profile-1543-selection.discovery.json`,
  `antiphon-all-profile-1543-execution\log_261108231307038.diag`.
  The diag holds the same message and stack as the TRX and nothing else relevant.
- Chunk result: total 1, failed 1, succeeded 0, skipped 0; test-run duration 9s 047ms; exit code 2.
- Runtime: TUnit v1.44.0.0, .NET 9.0.22, Microsoft Testing Platform v2.2.2, win-x64, Windows 10.0.19045.
- Tool versions on the desktop interactive PATH (read 2026-11-08, not from the test process environment):
  `git` = 2.50.1.windows.1 (`C:\Program Files\Git\cmd\git.exe`), `pwsh` = 7.6.9
  (`...\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe`). The failing assertion runs after
  `InitGitRepo()`, but the failure comes from no git or pwsh output.

## Failed test

| Test | Duration | Start (UTC) | End (UTC) |
|---|---|---|---|
| `Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_set_clock_and_foreign_dacl_are_not_adopted` | 00:00:05.0979389 | 2026-11-08T23:13:10.9190165 | 2026-11-08T23:13:16.0169554 |

Timing receipt: `startedAt` 23:13:06.647Z, `completedAt` 23:13:16.686Z, `runWallSeconds` 10.003,
`buildSeconds` 577.437, `processSpawnSeconds` 0.229, `buildSlot` not-used.

### Message (verbatim)

```
ShouldAssertException: SafeWrite.MatchesCreation(stamped)
    should be
False
    but was
True
```

### Stack (verbatim, complete as recorded)

```
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_set_clock_and_foreign_dacl_are_not_adopted() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 1390
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
--- End of stack trace from previous location ---
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Standard output (verbatim)

```
[CARD-0204] SessionRunner__BaseUrl=http://127.0.0.1:1, Delegation__CheckInterpreterEnabled=false, Delegation__DiagnoseEnabled=false and Delegation__OutputDistillerEnabled=false for every Program boot in this assembly (inherited SessionRunner__BaseUrl='<unset>') - test hosts never launch on the production session-runner.
[CARD-0298] Hangfire__ServerEnabled=false for every Program boot in this assembly - test hosts never start a Hangfire worker (no WMI census, no production-runner list).
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Checkpoints.OutputCleanupTests.1.1.windows_set_clock_and_foreign_dacl_are_not_adopted.1.1.0
```

Path / separator / ACL / symlink / CRLF / git / pwsh / chmod text in the logs: none. The only
ACL-adjacent text is the test name itself (`foreign_dacl`); the DACL half of the test
(lines 1396 onward) never ran because the assertion at line 1390 failed first.

## Code at the failing line (checkout 5fef2c23b, read-only)

`tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:1378-1390`:

```csharp
var clockRoot = InitGitRepo();
var clockOutput = Path.Combine(clockRoot, "bin-c1318");
SafeWrite.Require(SafeWrite.CreateFreshDirectory(clockOutput, SafeWrite.Under(clockRoot)));
var stamped = Path.Combine(clockOutput, "app.dll");
File.WriteAllText(stamped, "COMPILER");
var created = SafeWrite.FreshRootCreated(clockOutput)!.Value;
var clockExit = CheckpointFixtures.AfterFileClock(stamped);
File.WriteAllText(stamped, "TAMPERED");
var inside = created + (clockExit - created) / 2;
File.SetCreationTimeUtc(stamped, inside.UtcDateTime);
File.SetLastWriteTimeUtc(stamped, inside.UtcDateTime);
SafeWrite.Require(OwnedOutputLedger.Seal(clockOutput, clockExit));
SafeWrite.MatchesCreation(stamped).ShouldBeFalse();   // line 1390
```

`CheckpointFixtures.AfterFileClock` (`tests/Antiphon.Tests/Checkpoints/CheckpointTestSupport.cs:160`)
returns `DateTimeOffset.UtcNow` once the file's LastWrite/Creation times are not ahead of it; it
does not ensure that a later metadata change gets a change time strictly after the returned value.

`src/Antiphon.HostCleanup/Checkpoints/OutputCustody.cs:38-56` (adoption rule):

```csharp
if (entry.Changed < window.RootCreated && !CopiedInsideWindow(entry, window)) return "changed-before-root";
if (entry.Changed > window.BuildExited) return "changed-after-build";
if (entry.Modified > window.BuildExited) return "modified-after-build";
return null;
...
private static bool CopiedInsideWindow(Entry entry, Window window) =>
    OperatingSystem.IsWindows()
    && entry.Created >= window.RootCreated
    && entry.Created <= window.BuildExited
    && entry.Changed <= window.BuildExited;
```

The header comment (lines 18-20) states the property the test relies on: "Stamping creation or
last-write time moves change time to now, past an already observed exit, so that stamp-back stays
refused." Lines 12-14 name the boundary: "a write within one kernel timestamp tick after the
observed exit [is] indistinguishable from the build."

## History of this test in Windows qualification TRX files

| Run | Chunk | Outcome | Duration | Failure |
|---|---|---|---|---|
| 27a1b370 (rc-2026-10-20) | group-49 | Failed | 21.1s | line 1339 `cleaned.Refusals` had `left in place: ...\.checkpoint-output-ledger.json` (different mode) |
| a779085c (rc-2026-10-24) | group-61 | Failed | 11.4s | line 1339 `cleaned.Refusals` had `git-timeout` (different mode) |
| db2c2088, 8f055f87, 28bbb58e, d47848cc, 79b742de, 7f8e9fd1, a074ff9a, 0b05506b, b19a9880, 472c7495, 8e938b6d, e737e9fe | various | Passed (12 runs) | 6.6s-17.5s | - |
| a6410818 (rc-2026-11-12) | profile-1543 | Failed | 5.1s | line 1390 stamp-back adopted (this failure, first occurrence of this mode) |

## Diagnosis

`windows_set_clock_and_foreign_dacl_are_not_adopted` failed at line 1390: after `File.WriteAllText`
("TAMPERED") and the creation/last-write stamp-back, `Seal(clockOutput, clockExit)` adopted
`app.dll`, so `MatchesCreation` returned True. Adoption needs `entry.Changed <= window.BuildExited`
(`OutputCustody.cs:44`, and `:56` for the copy branch). With Modified and Created stamped to `inside`
(within the window), the only thing that should refuse is the NTFS change time landing after
`clockExit`. So on this run the change time from the TAMPERED write and the two `SetFile*Time`
calls was at or before `clockExit`.

Most likely cause: a timestamp-granularity race in the test. `clockExit` is `DateTimeOffset.UtcNow`
(precise system time), taken a few milliseconds before the tamper and stamps. NTFS records change
time from the coarser kernel tick clock (up to about 15.6 ms with the default timer resolution), so
a metadata change in the same tick as `clockExit` can carry a change time no later than `clockExit`.
That is the "within one kernel timestamp tick after the observed exit" boundary the source names
(`OutputCustody.cs:12-14`), which the test never steps over: `AfterFileClock` only waits until file
times are not *ahead* of now (`CheckpointTestSupport.cs:160-178`). It passed in 12 earlier Windows
runs. That fits a race that depends on whether the tick boundary falls between `clockExit` and the
stamps, which in turn depends on the host's effective timer resolution at that moment (23:13Z,
desktop otherwise loaded by the qualification run). This chunk's 5.1 s run time is also the
shortest on record (the others ran 6.6-21 s). The logs carry no path, ACL, symlink, CRLF, git or
pwsh evidence, and nothing points to a product regression in `OutputCustody`/`SafeWrite`. The
earlier failures, at line 1339 (`git-timeout`, ledger left in place), are different modes.

Unconfirmed from the logs: the file's actual ChangeTime against `clockExit`. The logs record no
timestamps for the file. A Debug pass can confirm by reading `GetFileInformationByHandleEx`
`FILE_BASIC_INFO.ChangeTime` against `clockExit` in a loop on the desktop (no code change is
proposed here).
