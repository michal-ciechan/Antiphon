# Review aaefd3a1 — round 1 (diff-only): tree-kill reap before journal release

Subject: Debug task 39db3a34-0139-41f2-ba03-fbe7b6b9b8e6, branch feat/card-task-39db3a34,
tip 115d3cd00126d40d0676a84a4a84d158c3c890d3 (a58bafcab red witness + 115d3cd00 fix) on
679b696bd178e3c8bb14b32fcd04a1c26b148435 (= origin/master = origin/rc/2026-11-01).

Verdict: CLEAN. No D-n defects. Advisories A-1..A-5 below (cards, non-blocking).

## Invariant audit (a)

- Release rule. The record is removed (`return false`, then `journal.Exited`) only when the root
  has exited and every redirected stream reached EOF. The new path, in the cancel branch, retries
  the drain once more only if `HoldersGone` (reap ran, sealed still_alive=0, follow still_alive=0).
  The base already removed the record at EOF + root exit through the abandon continuation. The new
  rule is never weaker than the base rule; it only reaches it sooner. A detached holder keeps the
  pipe, so the drain fails and the record is handed off (`reason=pipe_open`, or `live_holder` /
  `root_alive`). A lease/exclusion is admitted from the same journal evidence, so it is not
  released earlier than at the base.
- Pid reuse. Sealed kills still re-check start ticks in `Signal` before `Kill(entireProcessTree:false)`.
  Followers are re-checked against their own follow-snapshot ticks. On Windows every signalled
  parent's handle is held until the follow pass ends (`KillHeld` always adds it to `retained`), and the
  root is LandingGit's own handle, so a parent pid cannot be recycled inside the window. Waits use the
  retained handle, never a fresh lookup. See A-1 for the Linux residual.
- Cap. `alreadyAccepted + kills.Count >= 64` bounds sealed + followers together, root included.
- Root ownership. The retained root entry has Dispose=false; only GetProcessById handles are disposed,
  in Kill's finally after Execute.
- Bounds. Sealed reap is at most 2 s (one shared `reapClock` per wave). Follow reap is at most 2 s. Grace
  is 2 s, then the root wait is 2 s, then the extra drain is 2 s. Worst case is about 10 s plus table
  scans (the base was about 4 s). There is no unbounded wait. `WaitForExit(int)` with a finite timeout
  does not wait for stream EOF.
  C1076 still asserts < 17 s; C448_LATE took 8.3 s under mutant M1.
- No throw out of the release path. Every `WaitForExit` is caught by Reap (counted alive, kill_failed
  logged). Follow-snapshot failure returns `followers=[]` with a reason. Unexpected exceptions still hit
  TryKillTree's catch or the caller's "leave the journal" catch, which is conservative.
- Windows (c). A retained handle of an exited process signals, so `WaitForExit` returns true and the
  process counts as gone. That is correct. A zombie row reads ticks=null in the follow snapshot and is
  skipped. A pipe handle inherited by an unrelated process keeps EOF away, so after the extra 2 s the
  record is handed off (`pipe_open`). That is bounded and conservative.

## Evidence (b), (d) — runs on server2 (Linux), each build/run under its own build-slot lease

| Run | Commit | Result |
|---|---|---|
| Tip classes: OwnedProcessTreeTests, LandingGitTests, RepositoryMutationLeaseTests, RepositoryChildJournalInspectorTests | 115d3cd00 | total 120: 17/17, 66/66, 28 passed + 2 Windows NotExecuted (C448_V13, C448_V28_ExitedRoot…), 7/7; failed 0 |
| Base red: ChildBorn + SnapshotIsSealed | a58bafcab | 1 failed (C448_ChildBornAsTheRootIsSignalledIsKilled: `killed should contain 30 but was [10, 20]`), 1 passed |
| Five witnesses (ChildBorn, ChildThatAppears, FollowPass, LateInTree, SnapshotIsSealed) | 115d3cd00 | 5/5 |
| M1 skip sealed reap | mutant | red 2: ChildThatAppears (`waits > 0`), LateInTree (`HolderAlive false`) |
| M2 PlanLateChildren returns empty | mutant | red 5/5 |
| M3 drop child-start >= parent-start check | mutant | red 1: FollowPass (`killed should not contain 41`) |
| M4 (accept reparented pid-1 children), M5 (disable extra drain) | — | NOT RUN: stopped at the 50-min budget |

The detached-holder and parent-death rows are green inside the 66: C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder,
C448_ParentDeathGrandchildDoesNotKeepTheJournal and C448_DescendantSetKeepsGrandchildrenWhoseParentDiesWithTheRoot.
C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion and C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal
were green on Linux. Their native Windows rerun is still pending; it cannot be run from server2.

- Land graph `audit-land-verification-graph.ps1 -Check`: approved, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`. No -Update was run.
- check-evidence-diff over 679b696bd..115d3cd00 (after `git fetch --unshallow`): `commits=2 entries=0 violations=0`.
- merge-tree onto origin/master and origin/rc/2026-11-01 (both 679b696bd): exit 0, tree 6c227ac3257f7befad3b64b480cc29a6cfbbb810. Both fast-forward.

Not run: the whole Unit lane (Antiphon.Tests, about 70 min wall on server2) and the native Windows rows.
Hence `ordinaryScopeCompleted: Interim`.

## Advisories (non-blocking; suggest cards)

- A-1 (Linux pid-reuse residual in the follow pass). `PlanLateChildren` does not re-apply Plan's
  parent-identity gate (`root_identity_mismatch`). On Linux the root is reaped by .NET and other killed
  pids are reaped by init. If the root pid is recycled within the ~2 s reap window, the rooted follow walk
  starts at the stranger, and that stranger's children (start >= the old root start) become "followers"
  and are SIGKILLed. Recycling would need a full pid wrap (pid_max 32768 here) inside about 2 s, so this
  is improbable. Windows is immune because the handles are retained. On Linux, a dead parent's children
  are reparented, so a follower only exists under a live parent. Hardening: skip a parent whose
  follow-snapshot row is present with StartTicks different from the recorded ticks, and skip parent rows
  that are absent on Linux.
- A-2 (effectiveness gap, not safety). `snapshotted` takes every row of the sealed passes, including rows
  masked as started-after-clock in the final pass. A child born between the last clock sample and the
  table read is never a follower, so it survives and the record is handed off as `pipe_open`.
- A-3. `HoldersGone` counts only signalled pids. A sealed entry whose identity read failed
  (kill_failed unreadable/Win32) is alive and not counted. The pipe drain still gates the release, as at
  the base, but `killed_alive=0` in the log can mislead.
- A-4. `WaitRetained` returns true (exited) for a pid with no retained handle. That cannot happen in
  production, but it is a fail-open default; prefer false.
- A-5. The reap blocks the calling thread-pool thread for up to about 4 s per cancelled git. The cancel
  path's worst case goes from about 4 s to about 10 s.

## Housekeeping

- Build-slot lease `bb0adedf-a157-4ead-ac12-3655c96fa873` (label aaefd3a1-m4-build) is still listed
  holderAlive=true. Its holder pid 18959 is not visible in this container. The M4 build was stopped,
  and the lease expires 2026-11-01T15:43:07Z. It was not deleted by hand.
- All bin-rvaa1 / bin-land-audit directories and the scratch worktree were removed. Source was never edited
  in this checkout; mutants were applied only in the removed scratch worktree.
- The worker report was read from GET /api/agent-tasks/39db3a34-…; `.antiphon/task-39db3a34.md` is not in this mirror.
