# Final Review round 1 — landing fence job object and owned marker

Verdict: not clean. Reviewed tip `ac884e807233633a07e2491b065656a041c1ebae` on `feat/card-task-2f9c3f5c` (this review branch fast-forwards that tip). `reviewedSourceClean: false`. Do not land.

Subject task `2f9c3f5c-3a35-48d2-a1b4-e1f77d86be3b`. Base `ead93feb00fcaf8bf31018599437f17bee8e934c`. Diff `ead93feb00fcaf8bf31018599437f17bee8e934c..ac884e807233633a07e2491b065656a041c1ebae`: 4 files, +511/−6 (`.antiphon/task-2f9c3f5c.md` +120, `LandingGit.cs` +55/−6, `OwnedChildMarker.cs` +175, `LandingGitTests.cs` +167).

Diff-only authority in the brief was used. Nothing in the subject was edited. PCs stay pending.

## D-1. An unreadable environ becomes "none found", and the journal is removed

Where: `server/Infrastructure/Git/OwnedChildMarker.cs:85-106` and `server/Infrastructure/Git/LandingGit.cs:260-277`.

`AnyEnvironCarries` reads `/proc/<pid>/environ`. `Directory.EnumerateDirectories("/proc")` failing is `null` (keep). A single `ReadAllBytes` that throws `IOException` or `UnauthorizedAccessException` is skipped (`continue` at line 101). After the loop the method `return false` (line 106). The comment at lines 85-86 states the choice: another user's environ is unreadable and is not ours.

`ReleaseChildAsync` calls that result only when the root has exited, `HoldersGone` is true (reap waited, `StillAlive == 0`, `FollowStillAlive == 0`), and the pipe is still open. `ownedAlive == false` calls `onStreamsClosed` (`RepositoryChildJournal.StreamsClosed` → `Remove`) and logs `repository_child_journal_release ... reason=pipe_open_unowned` (lines 262-277).

Scenario: the killed set is dead, stdout is still open, and a live descendant is other-uid, `PR_SET_DUMPABLE=0`, or otherwise unreadable (hidepid, permissions). Linux `/proc/<pid>/environ` is same-user only. The scan treats that process as absent and drops the fence while a writer of this repository may still be alive. The brief requires cannot-tell, which keeps the record (`reason=pipe_open`).

Measured on this host while writing the report: uid 1654, 26 readable environs, 7 unreadable, 0 unreadable same-uid, `/proc/1/environ` uid 0 mode 400, `ptrace_scope=1`. Those counts move with the process table. A live other-uid descendant is unknowable here, and the code answers false.

`C1076_StrangerPipeHolderDoesNotKeepTheJournal` stays green because unreadable processes are skipped. It does not go red for this case. A repair that returns null on every `UnauthorizedAccessException` makes `pipe_open_unowned` unreachable on a normal multi-user host (pid 1 is always unreadable) and will fail that stranger test until the witness matches the rule. A pid that has already disappeared (`FileNotFoundException`) can stay a skip. An access failure on a pid that is still listed must not become false.

## D-2. Windows `ActiveProcesses == 0` ignores the marker

Where: `OwnedChildMarker.AnyAlive` lines 76-79, `Attach` lines 45-52, `LandingGit.ReleaseChildAsync` lines 260-277.

On Windows, a live job handle with `ActiveProcesses == 0` returns false. The marker is not consulted. `Attach` runs after `Process.Start` and documents the window: a child started before `AssignProcessToJobObject` is outside the job. `TerminateJobObject` does not reach it.

Scenario: a grandchild is created in that window and then reparented (the same shape as the msys `sleep.exe` whose parent was the exited fork stub). The parent-pid walk reports the sealed set dead, the job reports no members, and lines 262-277 remove the record while that grandchild still carries `ANTIPHON_REPOSITORY_CHILD` and can still hold the pipe. The worker called the window microseconds. The production hook sleep starts later, so it is inside the job when assign succeeded. The race is still a path that removes the record while a provably-ours writer may live.

Repair: put the process in the job before it can spawn (`CREATE_SUSPENDED`, assign, resume, or `PROC_THREAD_ATTRIBUTE_JOB_LIST`). An empty job must not mean "no owned writer" for a process that never joined.

Create or assign failure already falls back to keep. `Create` logs `repository_child_job unavailable reason=create_win32_<err>` and leaves `_job` zero (lines 36-40). `AnyAlive` then returns null (line 78), and the release reason stays `pipe_open` (LandingGit.cs:283-284). Assign failure logs `assign_win32_<err>` or the exception type name, calls `CloseJob`, and the same null path applies (lines 52-59). That fallback holds.

## Removal paths

1. `RepositoryChildJournal.NotStarted` (line 94) from `LandingGit.CreatedNoChild` (lines 103-106). Start created no process.
2. Success path `journal.Exited` (LandingGit.cs:162) after both redirected reads complete and the root has exited. `Exited` throws if the handle has not exited (journal line 84). Ownership is not consulted. A descendant that closed its pipe ends does not keep the record.
3. Release early-return when `drained && RootExited` (LandingGit.cs:243-246), then the caller `journal.Exited` (line 155), reason `removed`. Same EOF rule. `TreeKillReport.None` has `ReapWaited` false, so a failed kill does not enter path 4 (`HoldersGone` is false).
4. New `pipe_open_unowned` remove (LandingGit.cs:262-277). This is D-1 and D-2.
5. Handoff continuation (lines 286-307): when the abandoned streams complete and `RootExited`, `closeJournal()` runs. A root still alive waits up to `AbandonedRootExitBound`, then removes only if the handle has exited. Otherwise the record stays (`exit_wait_bound`).
6. `ExitedAsync` (journal lines 96-101) removes only when pid plus start ticks confirm the root is not alive.
7. `Remove` retries for 2 seconds, then annotates `CleanupFailure` and throws (journal lines 104-123). Callers that catch `IOException` keep the fence and the EOF continuation retries (LandingGit.cs:270-273 and 310-312).

The worker sentence that a live owned daemon which does not hold the pipe also keeps the record is true only while some other holder keeps the pipe open. Paths 2 and 3 drop the record once the redirected ends reach EOF, even if a marked process is still alive. Those paths pre-exist this diff.

The killed-set identity is unchanged. `OwnedProcessTree` is not in the diff. `HoldersGone` still requires the reap report (`ReapWaited`, `StillAlive == 0`, `FollowStillAlive == 0`). `RootExited` is `Process.HasExited` on the handle.

## Job object

No call to `SetInformationJobObject`. No `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, no UI limit, no process-count limit, no `BREAKAWAY_OK`. `Dispose` is `CloseJob` only (lines 134-143). Closing the last handle does not terminate members, so a server exit does not kill in-flight git through this job. The success path never calls `TerminateMembers`. A normal git child that finishes is a job member with no extra limit; the journal is removed by the existing EOF path.

`TerminateJobObject(job, 1)` runs only from the kill path (line 68). Exit code 1 is the uint passed to `TerminateJobObject`. The handle is not closed by that call.

`CloseJob` is idempotent (zero the pointer after `CloseHandle`). Both launch sites use `using` (`LandingGit.cs:99` and `:1052`). Assign failure closes, then `Dispose` no-ops. No leaked handle on the current paths. The handle is `IntPtr`, not `SafeHandle` (advisory below). `CloseHandle` failure still zeros `_job`.

Breakaway is not granted, so credential helpers, ssh, gpg, and hooks stay in the job. That is what lets `TerminateJobObject` reach the reparented sleep. A helper that treats `CREATE_BREAKAWAY_FROM_JOB` failure as fatal is a native-only question.

The marker is `Guid.NewGuid().ToString("N")` per `Create` (line 40), one value per mutating child, not per repository. Another repository's child has a different value, so it does not match this needle and does not keep this record. Copying this exact guid into some other process can only keep this fence. The value is not logged. Log lines are `repository_child_job unavailable reason=...` and `repository_child_job_terminate root=<pid> active=<n|unknown> terminated=<bool>`. Reading other processes' environments is not written out.

## Tests

No `### Checkpoints` table exists for this fix, so the checkpoint tool was not used. The commissioned selection was the four named classes, one red reproduction, and one mutant. An earlier filter `/*/*/(LandingGitTests|RepositoryMutationLeaseTests|OwnedProcessTreeTests|RepositoryChildJournalInspectorTests)/*` (class names not starred) discovered zero tests and exited 8. That run is not a row.

Tip build, native default output, `UseAppHost=false`, `TEMP=/tmp`, through `scripts/build-slot.ps1`: `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -c Debug` exited 0.

Green, same properties, `--no-build`, filter `/*/*/(LandingGitTests*)|(RepositoryMutationLeaseTests*)|(OwnedProcessTreeTests*)|(RepositoryChildJournalInspectorTests*)/*`, `--timeout 45m`. TRX `/tmp/review-bd90f6b5-green/green.trx` (gitignored): `total=122 executed=120 passed=120 failed=0 notExecuted=2`. TRX times `2026-11-02T08:34:22.161375Z` to `2026-11-02T08:35:53.2986496Z`. Skips: `C448_V13_WindowsJunctionAndOtherProcessShareTheLease` ("Requires Windows junctions"), `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` ("Windows inherited-handle boundary"). Passed in that run, among others: `C448_ParentDeathGrandchildDoesNotKeepTheJournal`, `C1076_StrangerPipeHolderDoesNotKeepTheJournal`, `C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal`, `C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits`, `C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder`, `C448_JournalStaysWhileTheRootLives`.

Red at `10602cf3e4ba146bdcc59a964c3638253ce60ade` (tests only, before the fix), detached worktree, same build shape. Filter `/*/*/LandingGitTests/(C1076_StrangerPipeHolderDoesNotKeepTheJournal*)|(C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal*)|(C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder*)`. HTML summary total 3, passed 1, failed 2, dashboard duration 9.35s. `C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder` passed (`repository_child_journal_handoff ... reason=pipe_open`). `C1076_StrangerPipeHolderDoesNotKeepTheJournal` and `C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal` failed; both logged `repository_child_journal_handoff ... reason=pipe_open` (the owned test's expected `reason=owned_holder` was absent). Matches the worker's 2 of 3.

M1, uncommitted, detached worktree at the tip: `OwnedHolderAlive` forced to `false` (the release ignores ownership). Not committed. Filter added `C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits`. HTML summary total 4, passed 1, failed 3, dashboard duration 6.75s. `C1076_StrangerPipeHolderDoesNotKeepTheJournal` passed with `repository_child_journal_release ... reason=pipe_open_unowned`. The other three failed with that same release line: `C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal`, `C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits`, `C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder`. The mutant worktrees were removed after the run.

The whole Unit lane was not rerun. The commissioned round caps ordinary scope at this selection.

## Windows native, still pending

The Windows half compiled with the Linux build of the shared sources and did not execute. A skip is not a pass. This pending run is not itself a code defect, and it is not a pass.

Only a native run can prove: `CreateJobObject` and `AssignProcessToJobObject` succeed inside the real host job (nested jobs); the log is `repository_child_job_terminate ... terminated=True` and there is no `repository_child_job unavailable`; the msys sleep is in the job and dies; the pipe reaches EOF; the journal is removed; there is no handoff; a normal git commit or push that runs hooks, ssh, or gpg still finishes; server exit does not kill that child through this job. `ActiveProcesses` is proved only by a native `active=<n>` line.

Reviewed against the Win32 contract and the P/Invoke in `OwnedChildMarker.cs:160-174`: `CreateJobObject`, `AssignProcessToJobObject`, `TerminateJobObject`, `QueryInformationJobObject`, `CloseHandle` match kernel32 and set last error. `JobBasicAccounting` matches `JOBOBJECT_BASIC_ACCOUNTING_INFORMATION`. Info class 1 is `JobObjectBasicAccountingInformation`. `AssignProcessToJobObject` returns `ERROR_ACCESS_DENIED` when the process is already in a job that disallows nesting (Windows 7). Windows 8 and later allow a nested job when the target job is empty or in the hierarchy and has no UI limits. This code sets no UI limits. No `SafeHandle`. Wine headers were not on this host.

After D-1 and D-2 are fixed, that native qualification is still outstanding. Do not dispatch another Review only to repeat this paragraph.

## Land gates

`scripts/check-evidence-diff.ps1` over `ead93feb00fcaf8bf31018599437f17bee8e934c..ac884e807233633a07e2491b065656a041c1ebae`: `commits=4 entries=1 violations=0` exit 0. The worker reported commits=3 because that run was before the report commit `ac884e807`. Not a violation.

`git fetch origin master` and `origin/rc/2026-11-01`. `origin/master` is `4518ce64b70c13a4c03a375d9630152342ee732c`. `origin/rc/2026-11-01` is `ead93feb00fcaf8bf31018599437f17bee8e934c`. `git merge-tree --write-tree` exited 0 for both: master tree `fe5a1f9de87cfccefcbf58c6c06838b37a51afad`, rc tree `ce3ca1b9d2cfe097f62e2851a06ddf96f4de6e78`. No conflict output. Not merged.

`scripts/audit-land-verification-graph.ps1 -Check -ToolPath` the existing Debug evaluator `tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll` (the script accepted it; no Release rebuild). Exit 0. `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69` then `phase=pass2` and `approved`. `-Update` was not run.

OS-branch witness checker was not run: there is no plan path and no Code witness JSON. The Windows obligation above is the witness gap.

Runner placement was read (`GET /api/runner-defaults` revision 2, kinds Grok, Claude Code, Codex; session runners included one eligible Windows row, one eligible Linux row, and one ineligible Linux row). The Linux tests ran unpinned. No `-Platform`.

## Advisories

- A descendant that execs with a scrubbed environment (`env -i`) has a readable environ and no marker. That is the designed stranger answer. It is distinct from D-1, where the read fails.
- `IntPtr` plus a manual `CloseHandle` is safe on the paths above. A later edit can leak it. `SafeHandle` would close on finalize.
- The `/proc` scan can miss a marked child whose pid was already passed, if its parent exits before that parent is read. Once the root and the sealed set are dead, the only forker is a process outside that set. Worth closing in the same repair as D-1. It is a narrower window than the unreadable-environ answer.
- Paths 2 and 3 (EOF removes without asking ownership) are unchanged by this diff.

## Scope label

`ordinaryScopeCompleted: Interim`. The commissioned Linux selection ran with nonzero counts. The Windows native job path did not execute, so this is not Full and it does not authorize land. Substantive defects D-1 and D-2 send the next stage to code. D-2 policy: do not open another Review only to relabel the unrun Windows half or to edit this narrative.
