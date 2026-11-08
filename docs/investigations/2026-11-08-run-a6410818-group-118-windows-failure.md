# Run a6410818, group 118: Windows failure extract (2026-11-08)

Read-only extract from the desktop (DESKTOP-KTLKPIF). No build, test or cleanup was run.

- Run: a6410818 on rc/2026-11-12, candidate `rc-2026-11-12-5fef2c23b894`, checkout `C:\Antiphon\releases\checkout-c9cb914c` at `5fef2c23b89414b8818aaf40db9ad738bce9524c`.
- Logs: `C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\logs\2026-11-08-1749-a6410818\`
  - `antiphon-all-group-118-tests.log` (5848 B)
  - `antiphon-all-group-118.trx` (565767 B)
  - `antiphon-all-group-118-execution\log_261108201919953.diag` (Trace level; holds the same messages as the log, nothing more)
  - `antiphon-all-group-118.timing.json`
- Host: TUnit v1.44.0.0, Windows 10.0.19045 win-x64, .NET 9.0.22, Microsoft Testing Platform v2.2.2.
- Summary: total 310, failed 3, succeeded 307, skipped 0, duration 1m 04s 545ms. TRX Times: start 2026-11-08T20:19:20.22Z, finish 20:20:24.67Z. Build 577.4 s (`buildSlot: not-used`).

## Desktop tool versions (resolved on PATH)

- git: `/mingw64/bin/git` (also `/cmd/git`), `git version 2.50.1.windows.1`
- pwsh: `C:\Program Files\PowerShell\7\pwsh` and a WindowsApps alias, `7.6.9`
- `core.autocrlf`: system `true`. Checkout `server/Infrastructure/Git/LandingGit.cs` is `i/lf w/crlf` (CRLF on disk).

## Failed tests

| Test | Class (category) | Duration | Start (UTC) |
|---|---|---|---|
| Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity | ServerImagePushCredentialTests (Unit) | 1.941 s | 20:19:51.954 |
| C1432_every_server_git_start_site_disables_auto_maintenance | ServerGitProcessTests (Integration) | 0.206 s | 20:19:53.954 |
| Identity_free_rebase_fails_without_git_config_global_and_passes_with_it | ServerImagePushCredentialTests (Unit) | 2.313 s | 20:19:52.061 |

### 1. Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity

```
failed Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity (1s 941ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path '047b9e4d44e671898df1450313ff03fe549eaf' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.DeleteRoot(String root) in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Infrastructure\ServerImagePushCredentialTests.cs:469
    at Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Infrastructure\ServerImagePushCredentialTests.cs:192
    at Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.1.1.Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity.1.1.0
```

### 2. Identity_free_rebase_fails_without_git_config_global_and_passes_with_it

```
failed Identity_free_rebase_fails_without_git_config_global_and_passes_with_it (2s 312ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path 'a172845e82e054e415110e0eb777643144be7a' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.DeleteRoot(String root) in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Infrastructure\ServerImagePushCredentialTests.cs:469
    at Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.Identity_free_rebase_fails_without_git_config_global_and_passes_with_it() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Infrastructure\ServerImagePushCredentialTests.cs:160
    at Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.Identity_free_rebase_fails_without_git_config_global_and_passes_with_it()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests.1.1.Identity_free_rebase_fails_without_git_config_global_and_passes_with_it.1.1.0
```

Leftover roots in `%TEMP%` (not removed), timestamps match the failures:

```
C:\Users\lndco\AppData\Local\Temp\card-1776-970f6a6de01a41e5a740f5f7b1b0e9c7   (mtime 20:19:53.88Z, test 1)
  repo\.git\objects\18\047b9e4d44e671898df1450313ff03fe549eaf   attrib: A    R
C:\Users\lndco\AppData\Local\Temp\card-1776-0fe5c8813ed84920a9e2928f0bb332ba   (mtime 20:19:54.36Z, test 2)
  repo\.git\objects\15\a172845e82e054e415110e0eb777643144be7a   attrib: A    R
```

Source at the checkout (`tests/Antiphon.Tests/Infrastructure/ServerImagePushCredentialTests.cs`):

```csharp
// :160 and :192 — the test bodies' finally blocks
finally { DeleteRoot(root); }

// :466-470
private static void DeleteRoot(string root)
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}
```

Both test bodies completed (the stack starts in `DeleteRoot`, called from `finally`), so the rebase assertions passed. Only the cleanup threw.

### 3. C1432_every_server_git_start_site_disables_auto_maintenance

```
failed C1432_every_server_git_start_site_disables_auto_maintenance (206ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: missing
      should be empty but had
  1
      item and was
  ["server/Infrastructure/Git/LandingGit.cs:246 InProcessEligible/probe: eligibility probe must never launch or escape to a launch helper"]
    at Antiphon.Tests.Infrastructure.ServerGitProcessTests.C1432_every_server_git_start_site_disables_auto_maintenance() in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.Tests\Infrastructure\ServerGitProcessTests.cs:107
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
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Infrastructure.ServerGitProcessTests.1.1.C1432_every_server_git_start_site_disables_auto_maintenance.1.1.0
```

Production source at the checkout (`server/Infrastructure/Git/LandingGit.cs:243-259`):

```csharp
private bool InProcessEligible()
{
    if (_inProcessEligible is { } known) return known;
    var probe = new ProcessStartInfo("git");            // :246
    bool eligible;
    try
    {
        ConfigureProcess(probe);
        ApplyIdentity(probe);                           // added by 4128a884a (CARD-1776)
        _childEnvironment = new Dictionary<string, string?>(probe.Environment, ...);
        eligible = InProcessGitReads.ChildReadsSameRepository(probe);
    }
    ...
}
```

Scanner allow-list (`tests/Antiphon.Tests/Infrastructure/ServerGitProcessTests.cs:229-238`): a `probe` identifier may appear only as a member-access receiver or as the argument to `ConfigureProcess`, `ServerGitProcess.DisableAutoMaintenance`, or `InProcessGitReads.ChildReadsSameRepository`. Any other use counts as an escape and adds `eligibility probe must never launch or escape to a launch helper`.

History (checkout): `4128a884a` "fix(card-1776): give the Linux server a read-only git identity" (author antiphon-server2-runner, 2026-11-08 07:51Z, cherry-pick of `cef2481a8`) added `ApplyIdentity(probe)` to `InProcessEligible`. It changed `ServerImagePushCredentialTests.cs` (+140) but not `ServerGitProcessTests.cs`, which last changed in `cb4e278ad` on 2026-11-06.

No path-separator, symlink, chmod, pwsh, or ACL text appears in any of the three failures. CRLF is not involved: line 246 comes from counting `\n`, and the failure names the right line.

## Diagnosis

1. **Both ServerImagePushCredentialTests failures are a Windows-only cleanup failure, not a product failure.** Git for Windows writes loose objects with the read-only attribute (`attrib: A R` on `.git\objects\18\047b9e4d…` and `.git\objects\15\a172845e…`, which are the exact paths named in the exceptions). On Windows, `Directory.Delete(root, recursive: true)` at `ServerImagePushCredentialTests.cs:469` throws `UnauthorizedAccessException` on a read-only file. On Linux, deleting a file depends only on whether its directory is writable, so the same code passes there. Both tests were added by CARD-1776 (`d6c63c922`, `4128a884a`), which was authored and verified on server2 Linux, so their cleanup never ran on Windows before this rc run. The repository already has read-only-aware cleanup helpers, such as `tests/Antiphon.Tests/TestHelpers/GitFixtureCleanup.cs` and `GitTreeTemplate.DeleteTree`. This class uses its own `DeleteRoot` instead.
2. **C1432_every_server_git_start_site_disables_auto_maintenance is not Windows-specific.** It is a cross-platform source-scanner regression from CARD-1776. `4128a884a` added `ApplyIdentity(probe)` inside `LandingGit.InProcessEligible` (line 246 site). `ApplyIdentity` is not on the scanner's allow-list at `ServerGitProcessTests.cs:229-238`, so it counts as a probe escape. The test parses source text with Roslyn and reads no OS state, so the same failure should occur on Linux. It was missed before the rc run because the CARD-1776 slice ran only its own `ServerImagePushCredentialTests` selection and not `ServerGitProcessTests`, which audits every server git start site. The task branch base / origin/master `57bbfc99d` still has `ApplyIdentity(probe);` at `LandingGit.cs:251` and has no `ApplyIdentity` entry in the scanner.
