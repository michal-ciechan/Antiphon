# Debug 2fdc7fb5 — C448 and C1076 child journal left after a Windows kill

Outcome: the leftover `.git/antiphon/children/<id>.json` is the pipe-drain fence, not a failed delete. `Process.Kill(entireProcessTree)` kills the root first and then asks who its children were. An intermediate that dies with that root (Windows job object `KILL_ON_JOB_CLOSE`, same shape as Linux `PR_SET_PDEATHSIG`) is already gone, so its child is never walked and keeps the redirected pipes. The journal stays until those pipes close. The owner now snapshots the descendant set before the first fatal signal and terminates that set.

Start ref `fc064a68247b0186918dfb1f04465e6be0475389` (`origin/rc/2026-10-27`) was not already fixed. The earlier delete-retry commits `59b4bf077` and `cfaed0245` are on that tip, and the native rerun of qualification `d47848cc` still failed both rows.

This worker is Linux. The two native rows were not executed. The native rerun below is still owed.

## Commits

Branch `feat/card-task-2fdc7fb5`, fast-forward of `fc064a68247b0186918dfb1f04465e6be0475389`.

- `a909cdd1644682d634a323f7d0ed28c23f83597e` — red witness only.
- `c70d88af986d32a8d6dfde887ddbf1cb645fb718` — snapshot-before-kill (`OwnedProcessTree`), handoff that re-reads `HasExited` when the streams close, and the Git-for-Windows `winpid` line in the C1076 hook.

Files: `server/Infrastructure/Git/OwnedProcessTree.cs` (new), `server/Infrastructure/Git/LandingGit.cs`, `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs`.

## Red, then green

Red, commit `a909cdd16`, filter `C448_ParentDeathGrandchildDoesNotKeepTheJournal`: failed at `LandingGitTests.cs` grandchild-alive. `HolderAlive` was true after `Process.Kill(entireProcessTree)`. Cancellation had already returned (`C448_PDEATH elapsed=00:00:02.2058065 error=TaskCanceledException`). The empty-journal assertion was not reached. A standalone perl tree (intermediate `PR_SET_PDEATHSIG=SIGKILL`, grandchild clears it and holds stdout) showed the same: grandchild alive, root exited.

Green, same filter plus the budget and pipe-holder rows, commit `c70d88af9`, assembly `tests/Antiphon.Tests/bin-c448j` (slot `ef2759fd`, 7 passed, 0 failed, 10.5s):

| Test | Result | Evidence |
|---|---|---|
| `C448_DescendantSetKeepsGrandchildrenWhoseParentDiesWithTheRoot` | passed | in-memory parent map keeps the grandchild whose parent is 20, excludes ppid 1 |
| `C448_ParentDeathGrandchildDoesNotKeepTheJournal` | passed 1.47s | `repository_child_tree_kill root=12718 descendants=3`; grandchild dead; journal empty; cancel returned in 0.30s |
| `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` | passed 3.31s | `descendants=4`; `C1076_BUDGET elapsed=2.04s` `git_timeout`; journal empty; later push succeeded |
| `C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder` | passed 8.81s | `descendants=1` (reparented holder excluded); handoff `exited=True`; journal stayed until `KillHolder`, then empty; recovery preview and `-Execute` without confirm stayed exit 3 |
| `C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget` | passed 8.05s | call stayed past the budget; exit 0; journal empty after the holder closed |
| `C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits` | passed 3.82s | `descendants=1`; handoff `exited=True`; journal kept while the holder lived, empty after it died |
| `C1076_UpdateRefsStdinBreakClearsTheJournal` | passed 0.29s | EPIPE still clears the journal |

`RepositoryMutationLeaseTests` on the same assembly (slot `c21d125f`, 28 passed, 2 skipped, 0 failed, 1m 17s). The filter also selected the rest of that class; nothing failed.

- `C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion` passed in 0.26s. Stderr `repository_child_tree_kill root=19138 descendants=2`. `C448_CANCEL journalCleanupSeconds=0.000`. The empty-directory assertion and the 4s `HasUnfinishedAsync` wait are the original test body.
- `C448_JournalRemovalRetriesTransientDeleteFailuresBeforeRelease` passed for `exited`, `exited-async`, `streams-closed`, `not-started`.
- `C448_JournalRemovalFailureKeepsFenceAndRecordsReason` passed for the same four completions.
- `C661_AlreadyExitedChildIsJournalledInsteadOfThrowing`, `C661_FastExitingOwnedChildLeavesTheCallersChildStateUnknown`, `C661_FastExitingOwnedChildPersistsUnknownIdentityAndRecoveryRefuses` passed.
- Skipped on Linux, unchanged: `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` (Windows inherited-handle boundary), `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`.

Census and roster, same assembly (slot `7076e8ef`): `C1584_E2EExpandedBudget`, `C1577_Group71MeasuredBudget`, `C1404_Group10MeasuredBudget`, `C1410_MeasuredCostGroups`, `G2865_Group28Group65MeasuredBudget` passed (5/5). `RcRosterRepairTests` `Mirror`, `Explicit`, `Costs` passed (3/3).

Land-graph `-Check` (no `-Update`), tool `tools/Antiphon.LandGraphAudit/bin-c448j/Antiphon.LandGraphAudit.dll`, slot `3a7a740c`: `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69`, `approved`.

## Assertions kept

- `C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion` was not edited. It still requires the owned root to have exited, the children directory to be empty, and admission to succeed after dispose.
- `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` still requires `TimeoutException` / `git_timeout`, elapsed under 15s plus `KillDrainGrace`, an empty journal, and a successful push afterwards. The hook writes `/proc/$$/winpid` when that file exists so `KillHolder` uses the Win32 pid. On Linux the file is absent and `$$` is unchanged. The empty-journal assertion is the same line.
- The three detached-pipe tests still require the journal to remain while the reparented holder is alive, and to disappear after that holder exits. `Descendants` does not include a pid whose parent is outside the captured set, so a double-fork holder reparented to init is not killed. The green run recorded `descendants=1` on those rows.
- `C448_ParentDeathGrandchildDoesNotKeepTheJournal` skips on Windows with `Skip.Test`, naming the two native rows. `PR_SET_PDEATHSIG` is the Linux stand-in for the job-object order. It is not a Windows skip of the failing tests.

## What the product does now

`OwnedProcessTree.Kill` reads parents (Toolhelp32 on Windows, `/proc/<pid>/stat` ppid after the last `)` on Linux), walks up to four passes, records start ticks, then `Kill(false)` only when the start ticks still match, then kills the root with `entireProcessTree: false`. A reused pid is not signalled. Processes already reparented are not in the set.

If the 2s drain grace expires, the continuation reads `HasExited` when the streams actually close. A root that is still alive is waited on a background `WaitForExit` and the journal is deleted only after that exit. The caller is not blocked for that wait. Stderr lines: `repository_child_tree_kill`, `repository_child_tree_kill_snapshot_failed`, `repository_child_tree_kill_failed`, `repository_child_journal_handoff`, `repository_child_journal_waiting_for_root`, `repository_child_journal_retained_running_root`, `repository_child_journal_cleanup_continuation_failed`.

Delete retries are unchanged (`RepositoryChildJournal.Remove`, 2s / 50ms, then `CleanupFailure=delete_failed` and `repository_child_journal_cleanup_failed`). A thrown cleanup would have failed these tests as the wrong exception. The native message was "directory should be empty but had 1 item" after `child.HasExited` passed, which is the fence left because the pipes never reached EOF.

`scripts/recover-repository-children.ps1` does not read `CleanupFailure`. Preview is the default (exit 3 while anything is retained). `-Execute -ConfirmDescendantsExited` removes only `dead`, `reused`, and `completed` records. `unknown` and `alive` stay. It does not kill processes. A dead root whose sleeper was missed is therefore recoverable once an operator confirms descendants have exited. A record whose pid is still the live sleeper stays a fence, which is the correct admission hold.

Linux cannot reproduce a sharing violation: unlink succeeds while the file is open. The delete-seam tests above are the witness for a transient or permanent delete failure.

## Native rerun owed

On the Windows checkout of `c70d88af986d32a8d6dfde887ddbf1cb645fb718` (or the rc commit that contains it), rerun only:

- `RepositoryMutationLeaseTests.C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion`
- `LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal`

Keep stderr. Read `repository_child_tree_kill root= descendants=`.

- `descendants=0` and the json remains: the Toolhelp snapshot missed the tree (`repository_child_tree_kill_snapshot_failed` will say so).
- `descendants>0` and the sleeper is still alive: terminate missed that pid (start-tick mismatch or access denied).
- `descendants>0`, sleeper dead, json remains: the drain or delete path. Open the json. `CleanupFailure=delete_failed` means the 2s retry could not unlink it; `scripts/recover-repository-children.ps1 -Repository <repo> -Execute -ConfirmDescendantsExited` removes a dead/reused/completed record and must not remove an unknown one.

`C448_ParentDeathGrandchildDoesNotKeepTheJournal` is expected to skip on Windows.

## Landable

`git merge-tree --write-tree origin/master HEAD` conflicts. That is the standing rc-versus-master divergence, and it includes `LandingGit.cs`, `LandingGitTests.cs`, and `RepositoryChildJournal.cs`. Not merged. `origin/rc/2026-10-27` is still `fc064a68247b0186918dfb1f04465e6be0475389`, and this branch fast-forwards it.
