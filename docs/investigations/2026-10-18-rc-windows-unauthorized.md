# Windows rc UnauthorizedAccess cluster, 2026-10-18

Task `2f8d7b71`. Investigation only. No product or test source was edited. ACLs were not changed. The AppHost and the production runner were not restarted. No suite-wide run was started.

The gate under study is run `42d2c78424dd4612af43eed3afeaecc2` at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` (`rc/2026-10-18`). Its classification is `docs/investigations/2026-10-18-rc-cut-and-gate.md` (`f2bc263f7e123ebf91f28cbf82266da45a23cd49` on `feat/card-task-9d6824b0`). Logs: `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs\2026-10-18-1647-42d2c784`. The process owner on the log file and the checkout ACL is `DESKTOP-KTLKPIF\lndco`, the same account as this session.

## Root cause

`Directory.Delete(path, recursive: true)` on Windows throws `UnauthorizedAccessException` when the tree contains a read-only file. Git writes loose objects with `FILE_ATTRIBUTE_READONLY` (attribute value 33, `ReadOnly, Archive`). .NET 9.0.21 `FileSystem.RemoveDirectoryRecursive` calls `DeleteFile` and, on any error other than file-not-found, throws `Win32Marshal.GetExceptionForWin32Error(errorCode, fileName)` where `fileName` is only the `WIN32_FIND_DATA` name. It does not clear the read-only bit. Source: `dotnet/runtime` tag `v9.0.21`, `src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs`, method `RemoveDirectoryRecursive`. The host runtime recorded in the gate diagnostic is .NET 9.0.21.

Measured on this machine, outside either checkout:

| Step | Result |
|---|---|
| `git hash-object` of the bytes `source` (no trailing newline) | `1d3a06547c706bd0f75b130edecf4832295bba6b` |
| `DeleteFileW` on that loose object while read-only | `ok=False`, Win32 error 5 (`ERROR_ACCESS_DENIED`) |
| Clear the read-only bit, then `DeleteFileW` | `ok=True`, error 0, file gone |
| `Directory.Delete` of a `git init` + commit + `git clone` tree | fails in 17–35 ms: `Access to the path '3a06547c706bd0f75b130edecf4832295bba6b' is denied.` The directory remains |
| Same clone with `--no-hardlinks` | the same exception and the same name |
| Clear read-only on every file, then `Directory.Delete` | the tree is removed |
| `cmd rmdir /s /q` of the same tree | exit 0, tree removed |

The bare name in the exception is the 38-character loose-object file name. The fanout directory is `1d`, so the object id is `1d3a06547c706bd0f75b130edecf4832295bba6b`. That is the blob `CompletedCardRunnerFixture` writes at `tests/Antiphon.SessionRunner.Tests/CompletedCardRunnerFixture.cs:44` (`File.WriteAllText(..., "source")`) and commits before `git clone` (`:49`). Every fixture repo contains it, so most failures name that same file. A repo whose first refused object is a different loose object names that file instead. Hardlinks are not required.

This is not an ACL on `C:\Antiphon\releases\checkout`. `icacls` on that directory is inherited `Modify` for `Authenticated Users` and `Full` for `Administrators` and `SYSTEM`. The owner is `lndco`. The failing delete is under `%TEMP%` (`C:\Users\lndco\AppData\Local\Temp`), and the same command fails for a repo created there with no reference to the releases checkout.

Controlled Folder Access is off (`EnableControlledFolderAccess=0`). Real-time protection is on. It is not the cause: the failure is immediate, repeats on every clone, and disappears when the read-only bit is cleared before `DeleteFile`.

## Group 1 — read-only git objects, fixture cleanup

Environment: Windows git objects are read-only. Product behaviour: the tests call `Directory.Delete` and do not clear that bit. The failure is in test cleanup, on this Windows host, for any checkout. It is not a production defect and not specific to the releases clone.

### 1a. `CompletedCardRunnerFixture.DisposeAsync`

`tests/Antiphon.SessionRunner.Tests/CompletedCardRunnerFixture.cs:171` is `Directory.Delete(Root, recursive: true)`. `Root` is `%TEMP%\c1032-fence-<guid>` (`:13`).

Session-runner console log: 91 failures, all with that frame. Eighty name `3a06547c706bd0f75b130edecf4832295bba6b`. The other eleven, one test each, are `04f633fc0dc3ccd5d50111a7ecb2ac726381e9`, `28d98e3eac98ba03bb45df1120a8d39655964e`, `47f0079891e9d45afbf1a9da58bda2a4498d5f`, `52af58fa2f37e0bbeb35299b6663f14ca7aeb1`, `7dbaeaab8632142350884e39c4f9e69086b329`, `8a93b25a1031e82fad594f1f2cc0e61d362ac5`, `b2c0193100c0775b37490b5c9a8da1179227c6`, `c54236937183efb147b3ce37d9a8deb4f806f3`, `cd6082c74a34a2b929acc45c1b5b376f480713`, `f3a44d6a6e5375739a969d574c523604fd90d8`, `ff51373d8de2be800feb77fd9aa5c2567ddcc5`.

Antiphon console log: `C1032_TransportClientUnsupported` fails the same way. The stack is `CompletedCardRunnerFixture.DisposeAsync` line 171, called from `PhoneHomeCompletedCardTransportTests.World.DisposeAsync` (`tests/Antiphon.Tests/Application/PhoneHomeCompletedCardTransportTests.cs:197`). The path is the same source blob.

A leftover fence from the re-run below had that object at `origin\.git\objects\1d\3a06547c706bd0f75b130edecf4832295bba6b` with attributes `ReadOnly, Archive`.

### 1b. `C1213_SourceLandingKeepsLegacyIdentity`

`tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.CardBranchNames.cs:207` is `Directory.Delete(remoteRoot, true)` in a `finally`. `remoteRoot` is a temp directory into which the test clones a local origin (`:187–191`). One antiphon console failure. The exception names `f54a7a750c081c0ad256b2ff0c14afab1361db`, a 38-hex loose-object file name. Same API and same exception shape as group 1a. This method was not re-run on its own.

### 1c. `RunnerSeatReleaseFixture.DisposeAsync`

`tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs:644` deletes each directory in `_roots`. Those roots are harness temp directories (`:266`, `:547`). Six antiphon console failures, all `BlockedTaskParkDeliveryTests`, all `RemoveDirectoryRecursive` naming a 38-hex file:

| Test | Path named in the exception |
|---|---|
| `C1065_FullAnswerRequiresNewSessionUserPrompt` | `66c3fa002a7449e826e341b9c75db60a77181c` |
| `C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `78a58f91262067f22662d4542cdaed4d803010` |
| `C1065_PinnedFollowupCannotStealParkedIdentity` | `78a58f91262067f22662d4542cdaed4d803010` |
| `C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply` | `78a58f91262067f22662d4542cdaed4d803010` |
| `C1065_ParkedReviewReplyBindsFreshEvidence` | `ae9ef60d8dd206e5659ddd719d3d6fc9be21b4` |
| `C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize` | `ae9ef60d8dd206e5659ddd719d3d6fc9be21b4` |

The last two are thrown from `ReviewWorld.StartAsync` (`BlockedTaskParkDeliveryTests.cs:958`), which disposes the fixture. These six were not re-run. The stack and the bare 38-hex names match group 1a. `ParkedPredecessor.DisposeAsync` (`BlockedTaskParkDeliveryTests.cs:1057`) is a second `Directory.Delete` of a directory under a git repo root, but it is not the frame in these six stacks.

Group 1 is 91 session-runner console failures and 8 antiphon console failures. The gate's other inherited reds (the rest of the 119 and 72 console lines) are different exceptions and are outside this cluster.

## Group 2 — directory symlink, one test

`Symlinked_auth_file_is_not_presence_but_a_linked_home_is_judged_by_its_file` failed in 17 ms. The stack is `FileSystem.DeleteFile` at `tests/Antiphon.SessionRunner.Tests/CodexAuthProbeTests.cs:130`, path `C:\Users\lndco\AppData\Local\Temp\codex-auth-probe-563d5a7e572644c28500b886b8354096\home\auth.json`.

Line 124 creates a directory symlink at `auth.json` (`TryCreateSymlink(..., directory: true)`). Line 130 calls `File.Delete` on that path. `TryCreateSymlink` (`:317–330`) catches `UnauthorizedAccessException` and returns false, and the test would have skipped if creation had failed. Creation succeeded on the gate host, so this is not "Windows refused the symlink."

Reproduced here: a directory symlink (`Attributes=Directory, ReparsePoint`) and `[IO.File]::Delete` throws `UnauthorizedAccessException: Access to the path '<temp>\home\auth.json' is denied.` The link remains. `DeleteHome` (`:333–336`) catches only `IOException`, so the same exception at dispose would also escape, but the gate frame is line 130.

This is a test bug, one case, independent of git objects and of the releases checkout.

## Representative re-run

The filter `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionR3Bare(missing-objects)*` selects the whole argument set. TUnit ran all 7 rows. That is one method, not a suite. Both runs were through `scripts/build-slot.ps1`.

| Place | SHA | Build | Result |
|---|---|---|---|
| `C:\Antiphon\releases\checkout` | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` | gate exe already at `tests\Antiphon.SessionRunner.Tests\bin\Debug\net9.0\Antiphon.SessionRunner.Tests.exe` (written 18/10/2026 17:48) | lease `119c7592-1b94-4b2a-9224-4b215cc6eea3`, held 15 s. total 7, failed 7, succeeded 0, exit 2, 14 s 483 ms |
| `C:\src\Antiphon` | detached `61117dd1663d2f47ce4708c18b609b8936491986` | the default exe was dated 2026-07-16, so it was not used. `dotnet build` to `bin-uae2f8d7b71/` (forward slash), lease `a0d7eb07-ae00-460f-8359-ee95a27dd9c6`, 0 errors. Those directories were removed afterwards | lease `fd58c516-a076-4972-af89-c3b45a0e2880`, held 14 s. total 7, failed 7, succeeded 0, exit 2, 13 s 656 ms |

Every row failed in `CompletedCardRunnerFixture.DisposeAsync` line 171 with `UnauthorizedAccessException` and a 38-hex object name. On the desktop checkout the stack file is `C:\src\Antiphon\tests\Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs:171`. The cluster is the Windows `Directory.Delete` behaviour, not the releases checkout. `C:\src\Antiphon` is a detached checkout of `rc/2026-10-15`, not a linked worktree. It was not switched or pulled.

The HTML report written under the releases exe `TestResults` directory by this re-run was deleted. The releases checkout's only untracked file after that is the pre-existing `.antiphon-nightly-owned`.

## Timeouts and missing trx

The two suite timeouts are the profile watchdogs, not the access-denied cleanup and not a hung test.

| Suite | Watchdog | Diagnostic span (UTC) | Wall |
|---|---|---|---|
| antiphon | `3600000` ms (`tests/test-execution-policy.json` `watchdogs.antiphon`) | `2026-10-18T16:49:54.8307046Z` to `2026-10-18T17:49:43.2813220Z` | 59 min 48 s |
| session-runner | `1200000` ms (`watchdogs.session-runner`) | `2026-10-18T17:50:01.1174464Z` to `2026-10-18T18:10:00.0417675Z` | 19 min 59 s |

`scripts/lib/nightly-owned-process.cs:167–175` sets `TimedOut` when the elapsed time reaches the budget and then `TerminateJobObject`. The exit code is forced to 1 (`:188`). `summary.json` records `timedOut: true`, `exitCode: 1`, `evidenceReasons: ["missing-trx"]`, and reasons `timeout antiphon` and `timeout session-runner`. TUnit writes the trx as the process finishes. The job kill does not give it that chance, so `antiphon-all.trx` and `session-runner-all.trx` were not created. The console logs stop without a TUnit summary footer.

A uid recount of the same diagnostic files (a state counts only when `TestNodeUid` and the state property are on the same line):

| Suite | Uids seen | Passed | Skipped | `FailedTestNodeStateProperty` | InProgress and no Passed/Skipped/Failed |
|---|---|---|---|---|---|
| antiphon | 2366 | 2205 | 4 | 0 | 153 |
| session-runner | 1397 | 1292 | 18 | 0 | 85 |

The gate writeup recorded 154 and 126 for those last two cells. This recount is 153 and 85. Failures are printed on the console (72 antiphon, 119 session-runner) and are not flushed as `FailedTestNodeStateProperty`, so console failures stay in the non-terminal set. The antiphon diagnostic's last records are `Passed` transitions at `17:49:38`–`17:49:43Z`, seconds before the kill. The suites were still finishing tests. The access-denied rows themselves return in about 2–30 seconds; they do not consume the hour.

The missing trx and the InProgress rows are the watchdog kill of a suite that had not finished. They are not a second manifestation of the read-only delete.

## Ranked fixes

Not done, noted: none of these were implemented.

1. One test helper that clears `FileAttributes.ReadOnly` on the tree, then calls `Directory.Delete`. Use it at `CompletedCardRunnerFixture.DisposeAsync`, `RunnerSeatReleaseFixture.DisposeAsync`, and the `C1213` `finally`. That is the whole of group 1. It is small enough for the rc line. `cmd rmdir /s /q` also removes the tree; clearing the attribute keeps the delete inside the .NET call the fixtures already make.
2. At `CodexAuthProbeTests.cs:130`, remove the directory symlink with a directory or link delete, not `File.Delete`. One test.
3. Leave the watchdogs for a separate decision. Antiphon's hour and session-runner's 20 minutes are shorter than these suites on this 8-core host. Raising a watchdog or splitting a suite does not fix the reds above, and the reds do not explain the timeout.

## Closed

Group 1 is confirmed and reproduced on both the releases checkout and `C:\src\Antiphon`. Group 2 is confirmed from the gate stack and reproduced with `File.Delete` on a directory symlink. The timeouts are the watchdogs. No source file was changed.
