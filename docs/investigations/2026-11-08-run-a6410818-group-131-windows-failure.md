# Run a6410818 group 131: ServerRepositoryMirrorScriptTests fail on Windows

- Run: a6410818 (rc/2026-11-12, candidate `rc-2026-11-12-5fef2c23b894`), Windows desktop `DESKTOP-KTLKPIF`.
- Qualification checkout: `C:\Antiphon\releases\checkout-c9cb914c` at `5fef2c23b89414b8818aaf40db9ad738bce9524c`.
- Sources read (read-only):
  `C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\logs\2026-11-08-1749-a6410818\`
  `antiphon-all-group-131.trx`, `antiphon-all-group-131-tests.log`, `antiphon-all-group-131.timing.json`,
  `antiphon-all-group-131-execution\log_261108210225635.diag`.
- Group result: total 121, failed 3, succeeded 118, skipped 0, duration 16m 16s 908ms.
- Runtime: TUnit v1.44.0.0, .NET 9.0.22, Microsoft Testing Platform v2.2.2, win-x64, Windows 10.0.19045.
- Tool versions on the desktop interactive PATH (read 2026-11-08, not from the test process environment):
  `pwsh` = 7.6.9 (`...\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh`), `git` = 2.50.1.windows.1.
  Git is never reached by these tests (the script refuses before any git call).
- Script under test: `scripts/server2-repository-mirrors.ps1`, introduced by
  `dff78584d636fa49eca37652f2c3f14a48002a92` (2026-11-08 06:33Z, "feat(card-1776): mount the Linux land credential and refuse a missing token").
- Keyword scan of the tests log for chmod/symlink/CRLF/ACL/separator: no hits. The only platform-specific
  signal is the path form (Windows drive-rooted temp path), shown below.

## Failed tests

| Test | Duration | Start (UTC) | Assert line |
|---|---|---|---|
| WhatIf_maps_desktop_paths_and_does_not_create_them | 773 ms (00:00:00.7730205) | 21:15:46.497 | ServerRepositoryMirrorScriptTests.cs:31 |
| Execute_without_a_token_refuses_before_clone | 733 ms (00:00:00.7335961) | 21:15:48.069 | ServerRepositoryMirrorScriptTests.cs:73 |
| Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing | 865 ms (00:00:00.8657946) | 21:15:48.804 | ServerRepositoryMirrorScriptTests.cs:94 |

Passed in the same class: `Empty_antiphon_remote_is_unrecorded` (792 ms), `Both_switches_are_ambiguous_and_secrets_and_volumes_are_refused` (2941 ms).

### Messages and stacks (verbatim from antiphon-all-group-131-tests.log)

```text
failed WhatIf_maps_desktop_paths_and_does_not_create_them (773ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: run.Exit
      should be
  0
      but was
  2

  Additional Info:
      SERVER2-MIRRORS refuse exit=2 reason=path-forbidden

    at Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests.WhatIf_maps_desktop_paths_and_does_not_create_them() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Scripts\ServerRepositoryMirrorScriptTests.cs:31
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    (the same three frames repeat three more times)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests.1.1.WhatIf_maps_desktop_paths_and_does_not_create_them.1.1.0
```

```text
failed Execute_without_a_token_refuses_before_clone (733ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: run.Output
      should contain (case insensitive comparison)
  "reason=credential-absent"
      but was actually
  "SERVER2-MIRRORS refuse exit=2 reason=path-forbidden
  "
    at Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests.Execute_without_a_token_refuses_before_clone() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Scripts\ServerRepositoryMirrorScriptTests.cs:73
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    (the same three frames repeat three more times)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests.1.1.Execute_without_a_token_refuses_before_clone.1.1.0
```

```text
failed Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing (865ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: run.Output
      should contain (case insensitive comparison)
  "reason=mirror-parent-missing"
      but was actually
  "SERVER2-MIRRORS refuse exit=2 reason=path-forbidden
  "
    at Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests.Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Scripts\ServerRepositoryMirrorScriptTests.cs:94
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    (the same three frames repeat three more times)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests.1.1.Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing.1.1.0
```

The TRX (`UnitTestResult ... computerName="DESKTOP-KTLKPIF" outcome="Failed"`) and the .diag
(`TrxExceptionProperty`, lines ~996-1144) carry the same messages and stacks; no extra stderr.

## Relevant source (checkout 5fef2c23b)

`scripts/server2-repository-mirrors.ps1`:

```powershell
102  $root = $ReposRoot.TrimEnd('/').TrimEnd('\')
103  if ($root.Length -eq 0 -or -not $root.StartsWith('/')) { Stop-Mirror 'refuse' 2 'path-forbidden' }
...
163  if (-not (Test-MirrorFileUsable $TokenFile)) { Stop-Mirror 'refuse' 2 'credential-absent' }
...
169      if (... -not (Test-Path -LiteralPath $parent -PathType Container)) {
170          Stop-Mirror 'refuse' 2 'mirror-parent-missing'
```

`tests/Antiphon.Tests/Scripts/ServerRepositoryMirrorScriptTests.cs`:

```csharp
24   var repos = Path.Combine(root, "repos");
30   var run = await RunAsync("-TableFile", table, "-ReposRoot", repos);
71   var run = await RunAsync("-Execute", ..., "-ReposRoot", repos, "-TokenFile", token, "-PolicyFile", policy);
92   var run = await RunAsync("-Execute", ..., "-ReposRoot", repos, "-TokenFile", token, "-PolicyFile", policy);
166  var root = Path.Combine(Path.GetTempPath(), "card-1776-mirrors-" + Guid.NewGuid().ToString("N"));
```

## Diagnosis

Common cause for all three: the test passes `-ReposRoot` built from `Path.GetTempPath()`. On Windows that is
`C:\Users\<user>\AppData\Local\Temp\card-1776-mirrors-<guid>\repos` — drive-rooted, backslash-separated, and it
does not start with `/`. The script's ReposRoot guard (line 103) accepts only a POSIX absolute path
(`StartsWith('/')`), so it refuses with `reason=path-forbidden`, exit 2, before reading any table row. On Linux
`/tmp/...` starts with `/`, so the guard passes there. The script was designed for the server2 Linux mount
(`/work/repos`); the tests run on both platforms with no OS gating, so this is a test/script platform
mismatch, not a product regression on the desktop.

1. `WhatIf_maps_desktop_paths_and_does_not_create_them` — expected exit 0 / `reason=planned` (test line 31);
   got exit 2 `path-forbidden` from script line 103. The test's later assertions also build
   `"after=" + repos + "/antiphon"` (lines 33-35), i.e. a mixed-separator Windows path, which the script would
   only produce if line 103 accepted a Windows root.
2. `Execute_without_a_token_refuses_before_clone` — expected `reason=credential-absent` (test line 73, from
   script line 163); the line-103 guard fires first, so the token check is never reached.
3. `Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing` — expected
   `reason=mirror-parent-missing` (test line 94, from script line 170); the line-103 guard fires first.
   The canary token was not printed (only the refusal line appears in the output).

The two passing tests in the class do not pass `-ReposRoot`, so they use the default `/work/repos`, which
satisfies the guard; that is why only the three `-ReposRoot` tests fail.

No chmod, ACL, symlink, CRLF or git involvement: every failure stops at the first path guard, under 1 s,
before any git process (git is not invoked in any of the three runs).

## Why it was missed before the rc run

The script and tests arrived in `dff78584d` (CARD-1776) on 2026-11-08 06:33Z, the same day as the cut. Inferred,
not verified from task records: they were authored and run on Linux (server2), where `Path.GetTempPath()` returns `/tmp/`, which satisfies the
`StartsWith('/')` guard. No Windows run of `Antiphon.Tests.Scripts` covered the class before rc qualification,
and the test class has no Windows/Linux gate.
