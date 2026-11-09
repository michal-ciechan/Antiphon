# Run 231f573c group-117: RepositoryMutationLeaseTests C448_C24 Windows failure

Date: 2026-11-09. Read-only investigation of the Windows Full qualification run 231f573c on
rc/2026-11-13 (candidate `rc-2026-11-13-653f1145de5b`, checkout `C:\Antiphon\releases\checkout-0b36ae35`
at `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`). Nothing was built, run or changed on the desktop.

Sources (under `C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b\logs\2026-11-09-0440-231f573c\`):

- `antiphon-all-group-117.trx` (404916 bytes)
- `antiphon-all-group-117-tests.log` (3965 bytes)
- `antiphon-all-group-117.timing.json`
- `antiphon-all-group-117-execution\log_261109091811041.diag` (801664 bytes; failure at lines 2289-2306)

## Verdict

**Deterministic. Not load or timing.** The test's PowerShell worker builds the real
`LandingGit` by reflection with `Activator.CreateInstance(type, [object[]]@($null))`, which binds
only a public constructor with exactly one parameter. CARD-1776
(`dff78584d636fa49eca37652f2c3f14a48002a92`, 2026-11-08 06:33Z, "mount the Linux land
credential and refuse a missing token") changed the constructor from
`LandingGit(CardFileBoardLookup? cardFiles = null)` to
`LandingGit(CardFileBoardLookup? cardFiles = null, IOptions<GitSettings>? git = null)`.
`Activator.CreateInstance` does not apply C# optional-parameter defaults, so it throws
`MissingMethodException` ("Constructor on type ... not found"). The worker dies at once and never
journals a Git child. The test then reports the misleading `record should not be null`.
The failure will occur on every platform where the test runs. It is not Windows-specific.

## Failed test

- Name: `Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced`
- Categories: `Integration`, `[ParallelLimiter<ProcessSpawnLimit>]`
- Outcome: Failed. Duration `00:00:01.9888419` (start 09:22:20.2034996Z, end 09:22:22.1923415Z), computer DESKTOP-KTLKPIF
- Chunk: 208 total, 207 passed, 1 failed, 0 skipped. Chunk duration 4m 30s 532ms. Run wall 271.9 s, build 468.6 s

### Message (ANSI escapes stripped)

```
ShouldAssertException: record
    should not be null but was

Additional Info:
    ParentContainsErrorRecordException: C:\Users\lndco\AppData\Local\Temp\antiphon-c448-a7f46e0e47bc45b9aed481a2183523d4\crash-worker.ps1:17
Line |
  17 |  $git = [Activator]::CreateInstance($server.GetType('Antiphon.Server.I .
     |  ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | Exception calling "CreateInstance" with "2" argument(s): "Constructor on type
     | 'Antiphon.Server.Infrastructure.Git.LandingGit' not found."
```

### Stack (complete; TUnit omitted its internals)

```
   at Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Infrastructure\RepositoryMutationLeaseTests.cs:line 618
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Infrastructure\RepositoryMutationLeaseTests.cs:line 671
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Infrastructure\RepositoryMutationLeaseTests.cs:line 671
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

StdOut: `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests.1.1.C448_C24_KilledWorkerLeavesLiveGitChildFenced.1.1.0`

### Process, git, pwsh and path text

- Worker: `pwsh -NoProfile -File <fixture>\crash-worker.ps1 <Antiphon.Server.dll path> <fixture.Source>`
  (`RepositoryMutationLeaseTests.cs:578-580`), with `GIT_CONFIG_NOSYSTEM=1`, pinned identity,
  `commit.gpgSign=false` and `core.hooksPath=<fixture>\hooks` (hook `pre-commit` = `sleep 90`).
- Fixture root: `C:\Users\lndco\AppData\Local\Temp\antiphon-c448-a7f46e0e47bc45b9aed481a2183523d4\`
- Failing script line 17 is the `CrashScript` line in
  `tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs:22`:
  `$git = [Activator]::CreateInstance($server.GetType('Antiphon.Server.Infrastructure.Git.LandingGit', $true), [object[]]@($null))`
- The same one-argument call is in `RecoveryScript` at `RepositoryLeaseCrashWorker.cs:54`. It
  fails next after line 22 is fixed.
- Test line 618 is `record.ShouldNotBeNull(process.HasExited ? await error : "child start must be acknowledged")`.
  The 30 s journal poll budget (`TimeSpan.FromSeconds(30)`) was not exhausted. The loop ended
  because `process.HasExited` became true after about 2 s, and the worker's stderr became the
  assertion message.
- Constructor in the run checkout: `server/Infrastructure/Git/LandingGit.cs:57`
  `public LandingGit(CardFileBoardLookup? cardFiles = null, IOptions<GitSettings>? git = null)`.
  Before `dff78584d` it was `server/Infrastructure/Git/LandingGit.cs:52`
  `public LandingGit(CardFileBoardLookup? cardFiles = null) => _cardFiles = cardFiles;`
- `origin/master` (fetched 2026-11-09) still has both the two-parameter constructor and the
  one-argument worker calls, so master is red too.

## Neighbouring tests in the chunk

| Start (UTC) | Duration | Outcome | Test |
|---|---|---|---|
| 09:22:10.37 | 0.26 s | Passed | C448_C24_StandingJournalFencesAdmissionByStartIdentity(reused) |
| 09:22:15.35 | 1.11 s | Passed | C448_V13_WindowsJunctionAndOtherProcessShareTheLease |
| 09:22:16.46 | 0.20 s | Passed | C448_C24_StandingJournalFencesAdmissionByStartIdentity(alive) |
| 09:22:17.77 | 1.64 s | Passed | C448_V13_LeaseUsesCommonRepositoryIdentity |
| **09:22:20.20** | **1.99 s** | **Failed** | **C448_C24_KilledWorkerLeavesLiveGitChildFenced** |
| 09:22:22.90 | 3.06 s | Passed | C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput |
| 09:22:25.97 | 0.54 s | Passed | C448_C24_UnreadableJournalStateCannotAdmitAWriter(malformed) |
| 09:22:27.51 | 1.34 s | Passed | C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion |
| 09:22:31.58 | 2.17 s | Passed | C448_JournalRemovalFailureKeepsFenceAndRecordsReason(exited) |

The slowest tests in the chunk were `C452_R3_UpdateCrashKeepsLastCommittedFence` (43.8 s),
`C452_R3_FirstSaveCrashLeavesNoAdmissionFence` (24.6 s), `C452_R3_UnknownInsideChildrenRemainsFenced` (13.3 s)
and `C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` (12.6 s). All passed.

## Host load hints

None found that are relevant. Neighbouring process-spawning C448 tests ran in their usual 0.2-3 s.
The chunk finished in 4m 30s. `queueWaitSeconds` was 0. `TESTINGPLATFORM_DEFAULT_HANG_TIMEOUT`
was unset. The diag contains no timeout text for this test. Its only other `timeout` matches,
at lines 401 and 403, are `stop incomplete verified-run exit-timeout` in unrelated test output.
The failure took 2 s against a 30 s budget, so load cannot explain it.

## Why it was missed before the rc run

- CARD-1776 changed a public constructor that the C# compiler checks only at call sites, where
  the new optional parameter is invisible. The only caller that breaks is a reflection call
  inside a PowerShell here-string (`RepositoryLeaseCrashWorker`). No compile error or analyzer
  can see it.
- The CARD-1776 commit's verification covered its own files (`ServerImagePushCredentialTests`,
  `ServerRepositoryMirrorScriptTests`). It did not run `RepositoryMutationLeaseTests`, which is
  `Integration` and spawns processes.
- `rc/2026-11-12` already contained `dff78584d`, but run a6410818 on that line produced no
  group-117 / `RepositoryMutationLeaseTests` TRX before it was superseded. Run 231f573c is the
  first rc run that executed this test since the change.

## Fix direction (for the Debug task, not done here)

Make the reflection construction match the constructor. Either pass two arguments
(`[object[]]@($null, $null)`) at both `RepositoryLeaseCrashWorker.cs:22` and `:54`, or look up the
constructor by its parameter types. A sturdier option is a small test that asserts the worker
constructs `LandingGit`, so the next signature change fails fast with a clear message.
Verify with the single test filter
`/*/*/RepositoryMutationLeaseTests/C448_C24_KilledWorkerLeavesLiveGitChildFenced`.
The fix belongs on master and on the rc line (an rc fix).
