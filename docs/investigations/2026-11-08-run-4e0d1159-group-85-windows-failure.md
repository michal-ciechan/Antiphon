# Run 4e0d1159 group 85 Windows failure (LandGraphLandRefreshTests)

Read-only extraction from the desktop (DESKTOP-KTLKPIF) qualification run 4e0d1159 on
rc/2026-11-11 (checkout `C:\Antiphon\releases\checkout-0297fa9c` at
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`). Task cee910dc. No build or test was run.

Sources (in `C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\`):

- `antiphon-all-group-85-tests.log` (19759 bytes)
- `antiphon-all-group-85.trx` (963516 bytes)
- `antiphon-all-group-85-execution\log_261108113009478.diag` (1909085 bytes; it holds only the
  same TestNode messages and stacks, with no child-process stdout or command lines)
- `antiphon-all-group-85.timing.json`

## Run summary

```
TUnit v1.44.0.0 | 64-bit | Microsoft Windows 10.0.19045 | win-x64 | .NET 9.0.22 | Microsoft Testing Platform v2.2.2
Test run summary: Failed! - C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll (net9.0|x64)
  total: 508
  failed: 12
  succeeded: 496
  skipped: 0
  duration: 3m 53s 924ms
```

Timing JSON: started 2026-11-08T11:30:09Z, completed 11:34:05Z, runWallSeconds 235.8,
buildSeconds 631.1. Filter (diag line 10) includes `(LandGraphLandRefreshTests*)` among other
`Antiphon.Tests.Application` classes. All 12 failures are in `LandGraphLandRefreshTests`.

## Desktop tool versions (PATH resolution)

- pwsh: `7.6.9`, `C:\Users\lndco\AppData\Local\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe`
- git: `git version 2.50.1.windows.1`, `C:\Program Files\Git\mingw64\bin\git.exe`
- `core.autocrlf` = `true`, from `file:C:/Program Files/Git/etc/gitconfig` (system). No global value.
- Git for Windows writes loose objects read-only (`-r--r--r--`), confirmed in a scratch repo.

## All LandGraphLandRefreshTests results (TRX)

| Test | Duration | Outcome |
|---|---|---|
| real_audit_property_refusal_does_not_refresh | 00:00:02.561 | Failed (family B) |
| real_audit_changed_definition_refreshes_commits_only_land_graph_files_and_second_check_exits_zero | 00:00:28.772 | Passed |
| instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden | 00:00:00.006 | Passed |
| failed_card_note_does_not_block_the_refresh | 00:00:05.439 | Failed (family A) |
| current_graph_does_not_refresh | 00:00:02.989 | Failed (family A) |
| inherited_only_mismatch_warns_and_does_not_refresh | 00:00:03.564 | Failed (family A) |
| check_still_failing_after_refresh_fails_the_land | 00:00:06.380 | Failed (family A) |
| pin_change_outside_the_batch_files_is_outside_batch | 00:00:04.312 | Failed (family A) |
| inherited_mismatch_mixed_with_a_batch_change_is_outside_batch | 00:00:03.749 | Failed (family A) |
| malformed_manifest_does_not_refresh | 00:00:03.205 | Failed (family A) |
| property_or_construct_refusal_does_not_refresh | 00:00:03.259 | Failed (family A) |
| changed_pin_on_a_batch_file_is_rewritten_and_committed | 00:00:06.729 | Failed (family A) |
| new_project_refreshes_and_reports_the_new_count | 00:00:06.697 | Failed (family A) |
| changed_csproj_refreshes_commits_only_land_graph_files_and_notifies | 00:00:07.095 | Failed (family A) |

The 12 failures do not share one message. There are two families.

## Family A (11 tests): read-only git object blocks `Fixture.Dispose`

Message (object name varies: `47a139e2691d0b91fe8cfc80f190e885a30de8` in 9 tests,
`e4bd6416af6288fc894de3bcfb00939f045094` in `pin_change_outside_the_batch_files_is_outside_batch`
and `changed_pin_on_a_batch_file_is_rewritten_and_committed`):

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path '47a139e2691d0b91fe8cfc80f190e885a30de8' is denied.
  at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
  at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
  at Antiphon.Tests.Application.LandGraphLandRefreshTests.Fixture.Dispose() in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:762
  at Antiphon.Tests.Application.LandGraphLandRefreshTests.current_graph_does_not_refresh() in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:272
  at Antiphon.Tests.Application.LandGraphLandRefreshTests.current_graph_does_not_refresh() in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:261
  at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
  at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
     --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
Standard output
  ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.LandGraphLandRefreshTests.1.1.current_graph_does_not_refresh.1.1.0
```

Every family-A stack is this shape: frame 1-2 `RemoveDirectoryRecursive`/`RemoveDirectory`, frame 3
`Fixture.Dispose()` at `LandGraphLandRefreshTests.cs:762`, then the test method at its closing
`using` line and its declaration line:

| Test | Call-site lines |
|---|---|
| failed_card_note_does_not_block_the_refresh | 290 |
| current_graph_does_not_refresh | 272, 261 |
| inherited_only_mismatch_warns_and_does_not_refresh | 256, 242 |
| check_still_failing_after_refresh_fails_the_land | 237, 221 |
| pin_change_outside_the_batch_files_is_outside_batch | 216, 196 |
| inherited_mismatch_mixed_with_a_batch_change_is_outside_batch | 191, 175 |
| malformed_manifest_does_not_refresh | 170, 154 |
| property_or_construct_refusal_does_not_refresh | 149, 133 |
| changed_pin_on_a_batch_file_is_rewritten_and_committed | 128, 105 |
| new_project_refreshes_and_reports_the_new_count | 100, 85 |
| changed_csproj_refreshes_commits_only_land_graph_files_and_notifies | 80, 61 |

Source at the failing line (`LandGraphLandRefreshTests.cs:762`, identical to this task's HEAD):

```csharp
public void Dispose() => Directory.Delete(Root, recursive: true);
```

`Fixture` creates `%TEMP%\c1753-<guid>\repo` with `git init -b main` and commits into it. The
38-hex names in the message are loose-object file names under `repo\.git\objects\<2 hex>\`.
After the run, 11 `c1753-*` directories remain in `%LOCALAPPDATA%\Temp`, each with only
`repo\` left (the deleted siblings `audit.ps1`, `pins.ps1`, `audit.log` are gone), which matches
a recursive delete that stopped inside `.git\objects`.

The sibling fixture `LandGraphEvaluationTests.AuditRepo.DisposeAsync` (`LandGraphEvaluationTests.cs:597-602`)
already uses `StartRefGit.DeleteDirectory(Root)` and did not fail in this run.

Note: `using var fixture` disposes in a `finally`. If a test body had also thrown, the dispose
exception replaced it, so these 11 results do not show whether the test assertions passed.

## Family B (1 test): audit wrapper refused "source differs from HEAD"

```
failed real_audit_property_refusal_does_not_refresh (2s 560ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: check.Text
      should contain (case insensitive comparison)
  "-Update cannot approve it"
      but was actually
  "audit refused: source differs from HEAD; commit source and pins before auditing
  "
    at Antiphon.Tests.Application.LandGraphLandRefreshTests.real_audit_property_refusal_does_not_refresh() in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:431
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandGraphLandRefreshTests.real_audit_property_refusal_does_not_refresh() in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:437
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    (the two preceding frames repeat four more times)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.LandGraphLandRefreshTests.1.1.real_audit_property_refusal_does_not_refresh.1.1.0
```

Test lines 421-432:

```csharp
await using var repo = await LandGraphEvaluationTests.ScriptRepoAsync(new()
{
    ["App/App.csproj"] = RealSdkProject("<CustomAfterMicrosoftCommonTargets>../docs/evil.md</CustomAfterMicrosoftCommonTargets>"),
});
await ConfigureGit(repo.Root);          // sets core.autocrlf=false AFTER the fixture commit
var head = (await GitText(repo.Root, "rev-parse", "HEAD")).Trim();
var check = await LandGraphEvaluationTests.RunScriptAsync(repo.Root, "-Check");
check.ExitCode.ShouldNotBe(0, check.Text);
check.Text.ShouldContain("-Update cannot approve it");   // line 431 fails
```

Child command (from `LandGraphEvaluationTests.RunScriptAsync`, working directory = fixture root):

```
pwsh -NoProfile -NonInteractive -Command "Set-StrictMode -Version Latest; & '<root>\scripts\audit-land-verification-graph.ps1' -Check"
```

The exit code was nonzero (line 430 passed); its value is not in any log.

The message comes from `scripts/audit-land-verification-graph.ps1:138-147`:

```powershell
function Assert-HeadSource([string]$Head, [bool]$AllowPendingPins = $false) {
    ...
    $run = Invoke-AuditChild 'git' (@('diff', '--quiet', 'HEAD', '--', '.') + $exclude)
    if ($run.Code -ne 0) { throw 'source differs from HEAD; commit source and pins before auditing' }
}
```

and is printed by the catch at line 253 (`'audit refused: ' + $_.Exception.Message`). In `-Check`
mode `Assert-HeadSource` runs at line 203 (before the pre-check) and line 249 (after
`Assert-EvaluatedGraph`). The output contains no pre-check text. The pre-check's refusal text
(`-Update cannot approve it`) would have been echoed at line 211 if it had run and refused, so the
refusal happened before the pre-check refused: either at line 203, or at line 249 after a silent
pre-check pass.

Line-ending facts relevant to the fixture (`AuditRepo.CreateAsync`, `LandGraphEvaluationTests.cs`):

- It copies `global.json` from the Windows checkout (`git ls-files --eol`: `i/lf w/crlf`) and
  writes `Antiphon.sln` with explicit `\r\n` (`Solution()`, lines 644-660).
- It runs `git init` / `add -A` / `commit` before `ConfigureGit`, so the commit uses the desktop's
  system `core.autocrlf=true`. CRLF files are stored as LF. `ConfigureGit` then sets
  `core.autocrlf=false`.
- On Linux there is no system `autocrlf`, so CRLF is stored as-is.

A scratch experiment (CRLF file committed under system `autocrlf=true`, then
`core.autocrlf=false`, then `git diff --quiet HEAD -- .`) returned exit 0, because the index stat
cache hides the content difference. The sibling `real_audit_changed_definition...` test, which has
the same fixture, the same `ConfigureGit` and the same first `Assert-HeadSource`, passed in 28.8 s.
The CRLF/autocrlf state alone therefore does not prove the line-203 path. It becomes a tracked diff
only if a tracked file is rewritten (re-stat'd) before `git diff`.

## Why it was missed before the rc run

`LandGraphLandRefreshTests` is new (`e2358f2dd` 2026-11-07, `ffc443b79`/`3fa874306`/`3e0a1e2bf`
2026-11-08) and carries no `NativeLinuxOnly` marker. Its authoring and review runs were on Linux,
where `Directory.Delete` ignores a file's read-only bit (Unix deletion needs only directory write
permission) and where git has no system `core.autocrlf=true`. Group 85 is the first Windows
execution of the class. The existing Windows-safe delete helper (`StartRefGit.DeleteDirectory`,
already used by `AuditRepo`) was not reused for the new `Fixture`.
