# task-2f9c3f5c: group 111 child journal kept on an open pipe after the killed set died

Base: rc/2026-11-01 = ead93feb00fcaf8bf31018599437f17bee8e934c. Branch: feat/card-task-2f9c3f5c.

## Production impact

A handed-off record (`repository_child_journal_handoff`, any reason) fences every land on that
repository until the abandoned pipe reaches EOF. If the server dies first, the fence stays until
`recover-repository-children.ps1` is run by hand. Before this fix, a pipe holder that the
parent-pid walk cannot see kept that fence for the holder's whole lifetime.

## Who holds the pipe

It is case (b), a descendant that escaped the set. It is not (a), a stranger that inherited the
handle, and it is not (c), the parent's own write end:

- Both native tests run an msys hook: `sleep 30` in pre-push (C1076) and `sleep 90` in pre-commit (C448).
- When msys `sh` runs `sleep`, it forks a stub, and the stub execs `sleep.exe`. Because the stub's
  parent (`sh`) is a Cygwin process, the stub exits. `sleep.exe` keeps the stub's dead pid as its
  Toolhelp parent.
- The parent-pid walk from the root therefore never reaches `sleep.exe`, and neither does the
  follower pass, which only accepts children of signalled pids. That matches the native lines:
  `killed_dead=6`/`4`, `still_alive=none`, `followers=0`, `pipe_drained=False`.
- `sleep.exe` holds the inherited stdout/stderr write ends until its sleep ends.
- The test comment "Tree kill does not reach a reparented msys grandchild" already describes this shape.

(a) does not apply to .NET children. On Windows, .NET Process.Start serialises pipe creation and
CreateProcess under `s_createProcessLock` and closes the child ends inside that lock. On Unix it
creates its pipes O_CLOEXEC. The parent's copies are closed by Process.Start, so (c) does not apply.

Process.Start offers no switch for a handle list or job assignment. Getting either needs a custom
CreateProcess with PROC_THREAD_ATTRIBUTE_HANDLE_LIST or PROC_THREAD_ATTRIBUTE_JOB_LIST.

## Fix (697e155cede48b5603f1d32d3c5d10495b1aeacc)

The new class `server/Infrastructure/Git/OwnedChildMarker.cs` is used only for mutating children
that have a journal:

1. **Ownership marker.** The child's environment gets `ANTIPHON_REPOSITORY_CHILD=<guid>`. Every
   descendant inherits it, including reparented ones.
2. **Windows job object.** It is created before Process.Start, and the child is assigned to it
   immediately after start. It has no KILL_ON_JOB_CLOSE, so a server crash still leaves the child
   running and the record standing. The budget/cancel kill calls `TerminateJobObject` after the
   tree walk, which reaches the reparented msys sleep. The pipe then reaches EOF and the normal
   path removes the record.
   - New log line: `repository_child_job_terminate root=<pid> active=<n> terminated=<bool>`.
   - If the job cannot be created or assigned, it logs `repository_child_job unavailable reason=...`
     and falls back to the old behaviour.
3. **Release decision (`LandingGit.ReleaseChildAsync`).** This applies when the root exited, the
   sealed set and followers are reaped and dead, and the pipe is still open. The release asks
   `OwnedHolderAlive`, which reads job members on Windows and `/proc/*/environ` carrying the exact
   marker on Linux:
   - **false:** the journal is removed now (`repository_child_journal_release ... reason=pipe_open_unowned`).
     The Process handle is still disposed only at EOF.
   - **true:** the record is handed off with `reason=owned_holder`.
   - **null:** the old handoff with `reason=pipe_open`.

   If removal fails, it falls back to the EOF continuation.

Both call sites use it: ExecuteProcessAsync and the update-ref --stdin batch.

Known limits:

- A descendant that execs with a scrubbed environment (`env -i`) counts as a stranger on Linux.
- On Windows, a grandchild created before the job is assigned (a start race measured in
  microseconds) is outside the job. If it is also reparented, the job reads empty and the record
  is removed while it lives.
- A live owned daemon that does not hold the pipe also keeps the record. This is conservative.

## Tests (LandingGitTests)

Commits: 0b8ae8cb8f (red), 10602cf3e (literal fix), 697e155ce (fix).

- `C1076_StrangerPipeHolderDoesNotKeepTheJournal` (Linux). The host starts a holder outside the
  owned tree that opens the root's fd 1/2 through /proc, and the set is killed. It asserts:
  - the stranger is alive;
  - the journal is empty immediately;
  - a `release ... pipe_drained=False ... reason=pipe_open_unowned` line exists and no handoff line exists.
- `C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal` (Linux). The same holder carries the marker.
  It asserts a handoff with `reason=owned_holder` and a journal that is kept, then removed after
  the holder is killed.
- Stand-ins (`DetachedPipeHolderGit.StartHolder`, `RedirectedShell` users) now copy the marker the
  way real git passes it to descendants.

## Runs (server2 Linux, one build slot per build)

- **Red at base** (production at ead93feb0, test commit 10602cf3e): 3 run, 2 failed.
  - Stranger: journal kept, with `killed_dead=2 followers=0 pipe_drained=False reason=pipe_open`,
    the same signature as Windows.
  - Owned: reason label.
  - C1076_PIPE: green.
- **Green at 697e155ce**, LandingGitTests + RepositoryMutationLeaseTests + OwnedProcessTreeTests +
  RepositoryChildJournalInspectorTests: 122 total, 120 passed, 0 failed, 2 skipped (Windows-only:
  C448_V13_WindowsJunction..., C448_V28_ExitedRootKeeps...).
- **M2 (old behaviour, keep on open pipe)** = the base run above: stranger red.
- **M1 (release ignores the identity check, `ownedAlive = false`)**: 4 run, 3 failed. Red:
  - OwnedPipeHolder;
  - C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder;
  - C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits.

  The stranger stayed green. Restored.
- **M3 (child not given the marker)**: 3 run, 2 failed (OwnedPipeHolder, detached holder). Restored.
- **Land graph `-Check`**: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
  The evaluator was built natively with -c Release and run with -ToolPath.
- **check-evidence-diff** ead93feb0..HEAD: commits=3, violations=0.
- **merge-tree**: clean onto origin/rc/2026-11-01 (ead93feb0) and origin/master (4518ce64b).

The Windows job-object code is compiled but has not been run here; no Windows host is reachable.
The native rows are its witnesses.

## What the native rerun must show (group 111)

- C1076 and C448_V28 stderr:
  - `repository_child_job_terminate root=<pid> active=<n≥1> terminated=True`;
  - then `repository_child_journal_release ... reason=removed`. `reason=pipe_open_unowned` is
    also acceptable if the job empties while some foreign holder keeps the pipe.
- No `repository_child_journal_handoff` line.
- `.git/antiphon/children` empty, and C448_CANCEL journalCleanupSeconds well under 4.
- If the line `repository_child_job unavailable` appears, the job path did not engage. Report it,
  because the old behaviour then applies.
