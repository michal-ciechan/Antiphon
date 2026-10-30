# Review cb50bc40: Windows skips A (task 00ae50cf, tip eb77637fc)

Verdict: no defects found. **Scope: Interim** (the brief commissioned that; the whole Unit lane was not rerun). Linux behaviour is unchanged and all five rows still run and pass on Linux. Each Windows arrangement is real, fails if its fixture fails, and has a safe teardown. Three of the four shared-logic mutations went red. Advisory A-1 covers the one that survived.

Reviewed: `eb77637fc2d1a5f8b49fec982845a48ca0fa055b` (clean tree) against rc base `ade54222543381e2e1b31ad79d8ae33a4035f70b`. The diff touches four test files (+ the author's report): no production change, and no change to policy, roster, exclusion or definition files.

## Runs (server2 Linux; every driver through scripts/build-slot.ps1)

Builds: Antiphon.Tests and Antiphon.SessionRunner.Tests, `OutputPath=bin-cb50/`, `UseAppHost=false`, both 0 errors. Tests ran with `dotnet exec --no-build`.

| Selection | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| OutputCleanupTests (class) | 49 | 45 | 0 | 4 (pre-existing Windows-only CARD-1310/1318 tests) |
| CheckpointOwnedOutputTests (class) | 10 | 10 | 0 | 0 (`link` Passed) |
| CheckpointScriptReaderDeadlineTests (class) | 6 | 6 | 0 | 0 (`unreadable`, `tampered-beside-unreadable` Passed) |
| RcRosterRepairTests | 3 | 3 | 0 | 0 |
| WindowsUnitPlatformAdmissionTests (C1210) | 5 | 4 | 0 | 1 (`C1210_Windows_linux_only_outcomes_are_exact`, Windows placement only) |
| C1514LandingSplitBudgetTests (C1404, C1577, C1584, C1410, G2865) | 5 | 5 | 0 | 0 |
| ReleaseGatePlatformTests/LinuxOnlyWindowsExclusions (C1323 gap model) | 1 | 1 | 0 | 0 |
| CompletedCardWorkspaceRemovalTests (class; `case-twin` Passed) | 99 | 99 | 0 | 0 |

`parent_symlink_does_not_delete_a_tracked_stamp` Passed on Linux. `scripts/check-evidence-diff.ps1 -Repository . -BaseRef ade54222 -HeadRef HEAD` reported 4 commits and 0 violations.

## (1) Linux unchanged

- **OutputCleanupTests.parent_symlink.** On Linux the code still creates `Directory.CreateSymbolicLink(link, output)` before the `try`, exactly as at the base. The assertions (exit 0, byte-identical stamp, `REFUSED:reparse-point`, script-order pins) and the `finally` block are unchanged. The gate moved from `!IsLinux` to `!IsLinux && !IsWindows`, so Linux still runs the test. The method now carries the assembly-local `ParallelLimiter<ProcessSpawnLimit>`.
- **CheckpointOwnedOutputTests.changed_inventory(link).** Linux still creates the file symlink and asserts the shared assertions plus `FileInfo.LinkTarget`, then deletes the link in `finally`. Only the Windows branches are new.
- **CheckpointScriptReaderDeadlineTests.** Only the C# gate and its comment changed. `scripts/test-checkpoint-reader-deadline.ps1` is byte-identical.
- **C1032_InspectionHoldShapes(case-twin).** The Linux path is unchanged. Both new lines run only under `OperatingSystem.IsWindows()`.
- No assertion was removed or loosened and no tolerance was widened. No catch-all wraps a new Windows branch. The only new `catch` filters are `Win32Exception` with NativeErrorCode 2 (fsutil missing, which skips with a named reason) and the bounded fsutil timeout, which throws `System.TimeoutException` and therefore fails the test.

## (2) The Windows arrangements are real

- **Junctions (both tests).** The fixture runs `cmd.exe /d /c mklink /J <link> <target>` through `ArgumentList` with a 30 s bound. `Exit.ShouldBe(0, stdout+stderr)` turns a failed mklink into a test failure, never a skip. Each test then asserts that the path carries `FileAttributes.ReparsePoint`. The owned-output test also asserts that `DirectoryInfo.LinkTarget` is non-null, that the target's `keep.txt` still reads `OUTSIDE`, and that `compiler.dll` still reads `COMPILER`.
- **Junction teardown is safe.** Both tests remove the junction with a non-recursive `Directory.Delete(link)`, which removes only the reparse point. In parent_symlink, the recursive `Directory.Delete(linkRoot, true)` runs only after the junction has gone, and .NET's recursive delete does not traverse name-surrogate reparse points in any case. If the junction delete throws, the recursive delete never runs. Neither test can delete the target's contents.
- **Hard-link ledger.** This is the same semantic as Linux, not a substitute. Both platforms run the same unchanged script, which calls `New-Item -ItemType HardLink`. That gives a link count of 2, and the shared `ConfinedFileReader.Refusal` (`linkCount != 1`, i.e. "selected file has multiple links") reports the entry as unreadable. The Linux fixture was already a hard link and never an ACL denial. The script asserts exact lines (`refused: unreadable`, the UNREADABLE line, TAMPERED keep.bin plus its manual command, and ordering).
- **Case-twin capability gate.**
  - If fsutil is missing, or `setCaseSensitiveInfo` exits non-zero, the test throws `SkipTestException` with a specific CARD-1032 reason that carries the fsutil diagnostic. The row is REQUIRED in the policy, so a skip still leaves the rc gate incomplete; it cannot pass silently.
  - Once fsutil has admitted the directory, a collision on create rethrows on Windows (a test failure), and `Directory.GetFiles(bin).Length.ShouldBe(2)` remains as a guard.
  - A collapse of `Twin` and `twin` into one file therefore fails the test.

## (3) Mutations (git-archive scratch copy, dll swap, one batch over independent files and methods)

| Mutation (shared logic) | Test | Result |
|---|---|---|
| `Test-CheckpointComponentLink` no longer returns `reparse-point` | parent_symlink | **RED**: the stamp was deleted through the link (FileNotFoundException on read-back) |
| `ConfinedFileReader.Refusal`: `linkCount != 1` changed to `== 0` | ledger `unreadable`, `tampered-beside-unreadable` | **RED** ×2: "kind must be unreadable, got ''" |
| `RunnerCompletedCardWorkspaceRemoval` names set: OrdinalIgnoreCase changed to Ordinal | case-twin | **RED**: Reason should be `case_ambiguous_content` |
| `OwnedOutputLedger.Inventory` linked-entry `throw` removed (a link treated as a plain entry or directory) | changed_inventory(link) | **survived** (green): see A-1 |

These mutations exercise the same product code that the Windows branches reach: the ReparsePoint attribute check, the link count, and the case comparer. I ran them on Linux only. The Windows branches themselves could not run here, and the native Windows evidence is the author's (`.antiphon/task-00ae50cf.md`).

## (4) Census, roster and policy

`tests/test-execution-policy.json`, `WindowsUnitSkipRoster.cs` and the C1584 roster are unchanged. None of them lists the five methods, and no exclusion row was added. The OutputCleanupTests exclusion row still names only the ten statx and hanging-git methods. C1210 roster closure, C1404, C1577, C1584, C1410, G2865, RcRosterRepair (3) and C1323 LinuxOnlyWindowsExclusions are all green.

## (5) Merge

- **onto rc `ade54222`:** clean.
- **onto `origin/master`:** the whole-branch merge-tree hits add/add conflicts in `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`. Both come from rc/master divergence and neither is a branch file. Picking the branch onto master with `--merge-base ade54222` is clean.
- **CARD-1613 tips (06fc8af7, add64715, f5557ddb):** merge-tree is clean.
  - The only file both branches touch is `OutputCleanupTests.cs`. CARD-1613 adds 10 `[NativeLinuxOnly]` markers there, on the excluded statx and hanging-git methods. This branch does not touch those methods, so their reason strings are unaffected.
  - None of the five methods appears in CARD-1613's `NativeLinuxWindowsBaseline.json` or its audit scope (the 45 witnessed exclusion rows). There is no marker overlap.

## Advisories (no Review rerun needed)

- **A-1** `tests/Antiphon.Tests/Checkpoints/CheckpointOwnedOutputTests.cs:105-106`. The `link` argument pins only `Deleted` empty and `Refusals` non-empty. If the ledger stopped refusing links, the link would still be refused as an unrecorded addition. On Windows the junction would be enumerated as a directory and its target content recorded as output, and the refusal would still occur. This weakness was inherited from the Linux base shape. The Windows row is not vacuous: it proves a real junction exists and that nothing is deleted through it. Pinning the refusal reason, for example the inventory-failed or linked reason, would make it sensitive to the link-handling mutation. Owner: the CARD-0590/1199 test owner.
- **A-2** `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionRepairTests.cs:208-211`. Any non-zero fsutil exit becomes a skip with a named reason. The author saw access denied in a worktree directory but success in %TEMP%, so admission depends on the host and directory. This is honest, because a REQUIRED skip leaves the gate incomplete, but a Windows Full run on a different host could report NotExecuted again.
- **A-3** `scripts/test-rc-platform.ps1` (the `$gaps` list) still models these five rows as "decision gaps" in the synthetic C1323 scenario. The case passes; only the label is stale.
- **A-4** No checkpoint-tool receipts. Every run was a build-slot `dotnet exec`, so `reviewedSourceClean` is false: there are no SHA-validated receipts.

Cleanup: deleted all 35 `bin-cb50` directories by literal path and verified none remain. The mutation copy stays inside the scratchpad.
