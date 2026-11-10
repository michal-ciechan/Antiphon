# CARD-1818 Review round 2 (Final, diff only) - task 5b3c6c26

Subject: feat/card-task-22a3d6a2 tip a8e4325b23c34b08f81ad10cb2b7172195a6d775 (Code owner 22a3d6a2, Grok).
Range reviewed: a6cdc4101fcaec7db97f217342c038ecd12e127d..a8e4325b23c34b08f81ad10cb2b7172195a6d775
(red 3b6063908, fix 674e0d24c, declarations a8e4325b2). Round 1: Review 211c38d3 D-1.

Verdict: CLEAN. D-1 is closed. No blockers and no regressions. Two new advisories.

## D-1 closure checks

1. Nothing deletes, renames or truncates packed-refs.lock or packed-refs any more.
   `ReleasePackedRefsLock` became `ReportPackedRefsLock`. It only checks the file and its
   mtime, then logs a warning and returns `packed_refs_lock_left`. A grep of src/ and
   scripts/ finds no other writer of packed-refs(.lock). The other `.lock` hits are read-only
   marker checks: RunnerWorkspaceSlotPool.MarkerIn, and RunnerCompletedCardWorkspaceRemoval,
   which returns `repository_locked`. The only git command the pass spawns is
   `git gc --prune=2.weeks.ago`. It has no pack-refs retry and no rm.
2. A lock left by a killed gc: nothing clears it automatically. That is the intended
   fail-safe trade.
   - Tick 1: row `failed/packed_refs_lock_left` and a warning log
     `left packed-refs.lock repo=... lock=... ageSeconds=N`. `Last` is served by
     GET /repository-maintenance and in the phone-home status.
   - A failed row never stamps the last run, so the repository stays due. The next tick
     comes after `CheckIntervalMinutes` (default 60). Its gc dies in pack-refs in about
     1.2 s (probe, git 2.47.3: `fatal: failed to run pack-refs`, exit 128), so the row
     becomes `failed/git`. That is an hourly fast failure, not a hot loop and not a 2-week
     prune.
   - Every ref deletion in that repository fails loudly with git's `Unable to create
     ...packed-refs.lock: File exists` (probe: `branch -D` exit 1). So the repository is
     not silently unusable: it is an advisory, not a blocker (A-4).
3. Red first: at 3b6063908, C1818GcOwnerRobustnessTests ran 10 tests: 7 passed and exactly
   these 3 failed.
   - `C1818_skip_docs_name_the_counted_git_calls`: `invariants should contain`.
   - `C1818_budget_kill_reports_a_lock_the_gc_child_wrote`: `File.Exists(childLock)
     should be` (the old code deleted the child-written lock).
   - `C1818_budget_kill_leaves_a_lock_held_by_an_outside_process`:
     `HolderHasLock(holder, heldLock) should be`. The old code unlinked a lock that the
     outside holder still had open, and /proc/<pid>/fd no longer resolves to it.
   All three pass at the tip. The outside-holder witness checks four things: the holder is
   alive, its fd still resolves to the same path, the dev:inode:size identity is unchanged,
   and the gc pid is gone.
   The child witness also checks that an older lock keeps reason `budget` and gets no
   lock log line.
4. The rest is unchanged: the src diff touches only the kill-path lock method. The retry,
   symlinked .git and stop-supervisor exit 3 detail are not in the diff, and their witnesses
   pass (below).

## Advisories (card them, do not hold the land)

- A-4 The lock signal lasts one tick. From tick 2, a left (or pre-existing) packed-refs.lock
  shows only as `failed/git`. The gc stderr is discarded, and no attention note is
  raised. Suggest a pre-gc check: if packed-refs.lock exists, skip with
  `packed_refs_lock_present` plus the lock age. Never delete it. Optionally raise an
  operator attention note when the lock is older than a bound. This would also cover a
  pre-existing old lock, which today reports `budget`.
- A-5 The two new witnesses add more Linux-only dependencies to A-1 (round 1): /bin/sh,
  `stat -c`, /proc/<pid>/fd. There is still no runtime OS skip and no rc Windows exclusion
  row for C1818GcOwnerRobustnessTests.
- Round 1 A-1/A-2 stand. A-3 is resolved: the declarations are committed and the checker
  exits 0.

## Evidence (reviewed SHA a8e4325b2, Linux server2 mirror, every driver leased)

- Build tests/Antiphon.SessionRunner.Tests (UseAppHost=false): exit 0, lease b513f23d.
- SessionRunner.Tests `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)`:
  34/34 passed, 0 skipped (TRX: C1818GcOwnerRobustness 10, C1817 14, C1441 6 = 30/30;
  NativeLinuxPolicyWitness 3, TestClassificationGuard 1). Lease 611547d9.
- Build tests/Antiphon.Tests: exit 0, lease bc512859.
- Antiphon.Tests `(C1818*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)`:
  68/68 passed, 0 skipped (TRX: C1818RepositoryMaintenanceStatus 1, TestClassificationPolicy 41,
  SlowTestRegistryMerge 5, SlowTestTripwire 2, TaskPlatformGuidance 5, TestLaneCategoryGuard 1,
  NativeLinuxPolicyWitness 3, TestClassificationGuard 1, C1729LayeringGuard 9). Lease 9ea50629.
- Red commit 3b6063908, detached scratch worktree: build lease 86cecc3d (exit 0). The
  C1818GcOwnerRobustnessTests run had 10 tests: 3 failed as listed above and 7 passed.
- scripts/test-qualification-stop.ps1 (Smoke): C1507 passed=38 failed=0.
- check-os-witnesses with head 674e0d24c (a8e4325b2 adds only the two JSON files): the
  base 72fada665 run with os-witnesses.json exited 0, and the base a6cdc4101 run with
  os-witnesses-repair.json exited 0. Running with head = tip says `SHA mismatch`, because
  the JSON names 674e0d24c. That is expected.
- check-evidence-diff -BaseRef a6cdc4101 -HeadRef a8e4325b2: commits=3 violations=0 exit 0.
  The same check over 72fada665..a8e4325b2: commits=6 violations=0 exit 0.
- git merge-tree --write-tree origin/master (52e13fdca8dcb780ef4afd3ca9247bc32d28bfe4) a8e4325b2: clean.
- PCs: none discharged. All stay pending for the SourceLanding Mutation.
- Native Windows witnesses owed from round 1 (taskkill /T, Win32_Process identity, PS 5.1) stay pending.
