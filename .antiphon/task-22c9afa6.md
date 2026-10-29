# Review 22c9afa6: LandingGit round 2, journal fence until pipe closes (subject 05078c1b)

Reviewed source: `90dd667ab1c0ecc38cbcd431548f59f83b6540da` (tests `ab7f8fd7d`, round 1 `46c3d8b93`, base rc `070d35b80`). Host: server2 runner mirror (Linux). Scope: Interim, as commissioned. The whole Unit lane was not run.

Outcome: 1 defect (a regression to fail-closed that wedges every later land on the repository), plus advisories. Round-1 D-1 and D-2 are fixed on `ExecuteProcessAsync`. The flipped witnesses are red at `ab7f8fd7d` and green at the tip 20/20, and they leave no sleeper behind.

## Defects

### D-1: the `update-ref --stdin` batch leaves a permanent fencing journal when the stdin write faults, where the base cleared it
- Where: `server/Infrastructure/Git/LandingGit.cs:903`. RunGitInputBytesAsync passes `write` to ReleaseChildAsync as one of the descendant-proof "streams", along with `copy` and `error`. Then `:186`: `Task.WhenAll(streams).WaitAsync(grace.Token)`. Only `OperationCanceledException` is caught (`:189`). Any other fault escapes into `:906-909` (`// The drain faulted ... Leave the journal.`), so `drained=false` and `:915` never calls `Exited`.
- Failure: git `update-ref --stdin` exits before it consumes stdin, so `child.StandardInput.BaseStream.WriteAsync` gets EPIPE. Examples: a startup fatal such as a bad config line, an invalid refname that `IsRetainedRef` admits (`.lock`, `@{`, `//`), or a caller cancellation/kill during a write larger than the pipe buffer. `write` faults with IOException. `copy` and `error` reach EOF and the root has exited, so the descendant proof is complete. But WhenAll is Faulted, the drain counts as faulted, and the journal file stays. A dead-root record always fences (`RepositoryChildJournal.FirstFencingAsync`). `RepositoryMutationLease.AcquireAsync:63` then returns null for every later land and mutation on that common directory ("unfinished repository child journal ...; run scripts/recover-repository-children.ps1"). That lasts until an operator runs `-Execute -ConfirmDescendantsExited`.
- Why: the base (`070d35b80`) did not await `write` in the catch: `try { await Task.WhenAll(copy, error); } catch { }`, then `journal?.Exited(child)`. It deleted the record in this exact scenario. The stdin write is not evidence about descendants; only the read pipes are. This is the "leftover entry wedges the next land" case the brief asks about, and it is new versus base. It fails closed, so it is not fail-open.
- Fix: keep `write` out of the descendant-proof set. Observe it separately: swallow or observe its fault, and await it best-effort before dispose. Alternatively, in ReleaseChildAsync treat "all read streams completed and root exited" as drained whatever the outcome of the write. Witness: in a LandingGit subclass, `StartProcess` returns, for `update-ref`, a stand-in `/bin/sh -c 'exec 0<&-; sleep 0.2; exit 3'`. Call `UpdateRefsAsync` (a real `refs/antiphon/land/...` delete) and assert that the journal directory is empty afterwards. It should be red at `90dd667ab`.

## Brief checks
1. Journal lifecycle:
   - Normal success and normal non-zero exit: drained, then `Exited`. Unchanged.
   - Budget kill, no holder: drained within grace, then `Exited`.
   - Budget kill, holder: `git_timeout` at about budget + 2 s. The record stays until both reads reach EOF, then the continuation deletes it. Witnessed: C1076_PIPE elapsed 4.19 s, entry present while the holder lives, cleared after the kill.
   - Caller cancellation: same release path, and the OperationCanceledException is rethrown.
   - Continuation `File.Delete` throwing: the continuation task faults unobserved and the record stays. Fail-closed, but it fences lands, as in D-1.
   - Server shutdown with a continuation pending: the record persists.
   - Recovery script: a dead root with a live holder is retained (exit 3), even under `-Execute` without confirmation (witnessed). With confirmation it is removed whatever the holder state. The script never auto-clears a record whose holder died later. While the server lives, the continuation clears it. After a crash, an operator must clear it.
   - Identity: each journal is a `Guid.NewGuid()` file, and each continuation captures its own journal instance. A continuation cannot delete a new invocation's entry, and concurrent lands do not collide.
2. Normal path: output is read to EOF (ReadToEndAsync starts before the identity read), and exit code and `git_exit_N` are unchanged. The normal-exit drain is again unbounded, as at base, so D-2 is fixed. A daemonised descendant that keeps a pipe can hang a normal-exit `ExecuteProcessAsync` land indefinitely, holding the lease. That is **unchanged from base**, and nothing bounds it at the LandingGit level. `gc --auto` is disabled (`DisableAutoMaintenance`) and its daemonize closes stdio. In RunGitInputBytesAsync the normal-path drain is bound by the fixed 5 min budget and throws OCE, not `git_timeout`. Also unchanged from base.
3. Handles and races: TryKill guards HasExited and swallows InvalidOperationException/Win32Exception, so double Kill and Kill-after-exit are safe. The process is disposed exactly once: the `finally` when not handed off, otherwise TryDispose in the continuation. The continuation does not touch the Process before deleting. A null budget becomes DefaultBudget. `TimeSpan.MaxValue` makes `CancelAfter` throw before the journal exists, unchanged from base. A canceled `write` (budget/ct) makes WhenAll Canceled, so it is caught as OCE and takes the abandon path. The continuation then runs inline at once. Correct, just indirect.
4. Witnesses: `[Timeout(60_000)]`, Linux-only double-fork, self-limiting sleeps (8/20/30 s), KillHolder in `finally`.
   - Red at `ab7f8fd7d` (tests over the round-1 product), built from a `git archive` export: 3 run, 2 failed as intended. C1076_NormalExit failed on `pastKillWindow should be True`, C1076_BudgetKill on `JournalFiles(children) should not be empty`. C1076_RunOptions passed on Linux (its msys holder is Windows-only).
   - Green at the tip, 20 sequential runs of the 3 tests: 20/20, 3/3 each.
   - `ps` after the runs: no `sleep 8|20|30` or `holder.sh` processes.

## Runs (build `--property:OutputPath=bin-r22c/ -p:UseAppHost=false`, 0 errors, slot 227 s; all runs `dotnet <dll>` through scripts/build-slot.ps1)
- `(LandingGit*)|(RcRosterRepairTests)|(C1514LandingSplitBudgetTests)|(RepositoryChild*)`: total 122, passed 122, failed 0, skipped 0 (4m26s). Breakdown:
  - LandingGitTests 60, LandingGitProfileTests 26, LandingGitBatchingTests 6
  - RepositoryChildRecoveryTests 15, RepositoryChildJournalInspectorTests 7
  - RcRosterRepairTests 3
  - C1514 census 5 (C1404, C1577, C1584, C1410, G2865)
- `(AgentTaskLand*)`: total 1401, passed 1391, failed 8, skipped 2 (32m37s, exit 2). The 8 failures are the pre-existing Linux CP-5 refusals: C883 x4 (StagedBlobSurvivesNativeIndex, CaseAliasCannotChangeRegisteredIdentity, BuiltinCrLfRecoveryPreservesRealEdits, LinkedWorktreeWithSpacesRecovers) and C975_SpacedBacklinkCorruption{Before,After}MoveRefuses(False/True) x4.
- Not run: the whole Unit lane (Interim), Windows-native C1076_RunOptions (the original Windows failure), and any PC (pending for Mutation).
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 070d35b80 -HeadRef 90dd667ab`: commits=4, entries=0, violations=0, exit 0.
- Landability: `merge-tree --write-tree origin/rc/2026-10-27 (fffa9e95) 90dd667ab` is clean. Chained per-commit `merge-tree --merge-base` of 48a24df44, 46c3d8b93, ab7f8fd7d, 90dd667ab onto origin/master (c49efbdc) is clean, so a cherry-pick port works. The chain wrote unreferenced commit objects; no refs were changed.

## Advisories
- A-1: if the root survives the tree kill, the second kill and 4 s of grace (for example D-state), `delete` stays null (`:210`). The record then stays forever, even after the streams close, which implies the root has gone. Fail-closed; base hung instead. Consider re-checking `process.HasExited` inside the continuation before TryDispose. The handle is still undisposed there.
- A-2: no witness covers the RunGitInputBytesAsync abandon path. The handoff's "both paths covered" is true in code only. The budget is a fixed 5 min, so drive it by caller cancellation with a stdout-holding stand-in.
- A-3: a ReadToEndAsync fault in `ExecuteProcessAsync` leaves the journal (`:144`). Same as base.
- A-4: on master, the author's whole-branch merge-tree conflicts in LandingGitTests C1076_RunOptions. Take HEAD there, or cherry-pick per commit, which is clean.
- Cleanup: the `bin-r22c` directories in the worktree (gitignored) and the scratch export with `bin-r22red` are left in place, per the brief's rule against recursive delete.
