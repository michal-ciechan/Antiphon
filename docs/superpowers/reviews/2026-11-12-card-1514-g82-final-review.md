# CARD-1514 g82 Final Review — card-file Windows job kill

Round 1, Final. Reviewed source task `d018e35b-6df1-4617-a45a-87d3d56da616` (Debug, Succeeded), tip `240157c13a2f229109fb9f7ff527a255359b56c8` on `feat/card-task-d018e35b`. Product range versus the merge-base with master is `077f0b3a7ef3279b78beb321d3fc86b13ba0e211..240157c13a2f229109fb9f7ff527a255359b56c8` (red witness `007b2c80b8e57e2357f2ef8b9ebfef8770338c11`, fix `240157c13`). This host is Linux. The Windows job path was judged by reading. It was not executed here.

## Verdict

No product blocker. The cancel path now puts each Windows card-file git child in an anonymous `OwnedChildMarker` job and calls `TerminateJobObject` before the old `Process.Kill(entireProcessTree: true)` fallback. Linux still kills with `Kill(true)` and never creates a job. The new test's assertions were committed in the red witness and were not edited by the fix.

`reviewedSourceClean` is false. Checkpoint run `20261112-173300-a759` is tool GREEN exit 0 at that SHA, and `tools/Antiphon.Checkpoints validate` then returned `CHECKPOINT SOURCE INVALID reason=row_failed` exit 2 because CP-1 has `skipped=3`. Those three results are the pre-existing `CardFilePrivacyGitTests.Junction_ancestors_refuse_root_and_managed_file_aliases` arguments `cards`, `root`, and `board` (`outcome=NotExecuted`, roster reason: NTFS junction fixtures require Windows reparse points). They are not passes, and they are not the new hook test. Evidence-backed land returns `review_evidence_source_not_clean` on this false value.

## Windows reading

`CardFileRepository.RunGitInputAsync` starts git with `CreateNoWindow`, then on Windows calls `OwnedChildMarker.Create` and `Attach` after `Process.Start`. `AssignedBeforeSpawn` stays false. `Attach` documents that a grandchild started before assignment is outside the job. The g82 hook is a pre-commit child of git, so it starts after `Attach` and is inside the job. The pre-assign window is the same attach-after-start contract LandingGit already uses. An anonymous job (`CreateJobObject` with a null name) cannot be terminated by a name that another process shares.

`TryKill` calls `TerminateMembers` first. If that throws, the `Kill(true)` walk still runs. If assign or create fails, the job handle is closed and `TerminateMembers` no-ops, so the fallback runs while git is still alive. After a successful job kill, `process.HasExited` is true and `Kill(true)` is skipped, so a grandchild that escaped before `Attach` is not collected by the fallback. The comment that says `Kill(true)` covers processes outside the job overclaims that case. The observed race (hook already running) is inside the job.

`Dispose` is `CloseHandle` only. There is no `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. The `finally` disposes the marker and the process on success, cancel, timeout, and the generic catch. `CloseJob` zeroes the handle, so dispose does not close twice. CardFile does not call `Apply`, so there is no `ANTIPHON_REPOSITORY_CHILD` environment marker. CardFile does not call `AnyAlive`, so the missing marker does not change this kill.

LandingGit kills with `TryKillTree` and then `TerminateMembers`. CardFile terminates the job first. That order is the fix for the conhost-then-reread race. It is not a requirement to copy LandingGit's order.

On Linux `owned` stays null. `TryKill(process, null)` still uses `Kill(true)`. The IL is not byte-identical, because the kill is now a method. The Linux kill behavior is the previous one.

## Tests

`Canceled_commit_ends_a_spawning_hook_before_it_sees_its_child_fail` runs eight rounds, cancels 300 ms after `hook-started`, and asserts the recorded hook pid is dead within 15 s, `hook-child-failed` is absent, and `HEAD` is unchanged. On Linux the child is `env true`, which does not fail, so this Linux pass does not exercise the job. On Windows the child is `ping -n 1`. `HookPidLine` writes `/proc/$$/winpid` when that file is readable, otherwise `$$`. On Linux `winpid` is absent, so `$$` is the real pid. A non-numeric or empty pid fails `int.Parse`. `HookAlive` returns false immediately for `pid <= 0`, which would not observe a death, but a live shell's winpid or `$$` is a positive integer. The stall-hook assertion still requires `hook-wait-failed` to be absent. The fix commit does not change the test file.

`linux-test-roster.json` still says `directTestMethods: 3` and source sha256 `eefcdbde83f885b650a813177f105997f38a1773c94384cc47944872c3942759` for this class. The file's current sha256 is `1face186a20d7a912651805abd62be0aae7296bdb44ab79ec09eabc6e90b2364`, and the class has four direct test methods. `Assert-LinuxTestRoster` checks names, dispositions, owners, and discovery. It does not read `sourceSha256` or the method count. Disposition remains `exclude`. Stale bookkeeping, not a behavioral failure.

## What a native Windows run must show

At `240157c13a2f229109fb9f7ff527a255359b56c8`, `CardFileGitFailureAcceptanceTests.Canceled_commit_ends_a_spawning_hook_before_it_sees_its_child_fail` must pass 8/8. Each round must leave `hook-child-failed` absent, the process recorded in `/proc/$$/winpid` gone within 15 s, and `HEAD` unchanged. Debug's native 8/8 is a claim this review did not revalidate. A Linux pass is not that witness. The three junction arguments stay pending on Windows as well. They are not this fix.

## Receipts

Run `20261112-173300-a759`. Commit `240157c13a2f229109fb9f7ff527a255359b56c8`. Host Debian GNU/Linux 12. `sourceState=clean`, `buildSource=verified`. Tool verdict GREEN exit 0. Wall 17m35s. Unlisted builds: none. CP-1's derived row timeout of 75 minutes exceeds the 45 minute advisory. That warning is not a failure. The new test passed in 4.275 s on Linux.

```
CHECKPOINT CP-1 commit=240157c13a2f229109fb9f7ff527a255359b56c8 build=ok filter=/*/*/(CardFileGitFailureAcceptanceTests*)|(CardFileIgnoreAcceptanceTests*)|(CardFileIgnoreTests*)|(CardFilePrivacyGitTests*)|(CardFilePrivacyRecoveryTests*)|(CardTaskFileServiceTests*)|(ImpactedClassesSelectorTests*)|(ServerGitProcessTests*)|(TestLaneCategoryGuardTests*)|(WorktreeDefaultCallerTests*)/* executed=144 passed=144 failed=0 skipped=3 trx=/work/worktrees/task-9e89ca0f/.antiphon/checkpoints/20261112-173300-a759/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=240157c13a2f229109fb9f7ff527a255359b56c8 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=240157c13a2f229109fb9f7ff527a255359b56c8 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-9e89ca0f/.antiphon/checkpoints/20261112-173300-a759/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=240157c13a2f229109fb9f7ff527a255359b56c8 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=240157c13a2f229109fb9f7ff527a255359b56c8 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-9e89ca0f/.antiphon/checkpoints/20261112-173300-a759/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=240157c13a2f229109fb9f7ff527a255359b56c8 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=240157c13a2f229109fb9f7ff527a255359b56c8 build=ok filter=/*/*/(GitIndexLockTests*)|(ServerGitAssemblyTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-9e89ca0f/.antiphon/checkpoints/20261112-173300-a759/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=240157c13a2f229109fb9f7ff527a255359b56c8 sourceState=clean buildSource=verified
```

Nine-class guard union is the CP-2 count of 78. ProcessSpawnLimitTests is the separate CP-3 count of 57. Do not add those into 78.

## Diff, merge, impact, migration

`scripts/check-evidence-diff.ps1` on `077f0b3a7ef3279b78beb321d3fc86b13ba0e211..240157c13a2f229109fb9f7ff527a255359b56c8`: commits=2, entries=0, violations=0, exit 0.

`git merge-tree --write-tree origin/master HEAD`: exit 0, tree `bfd54d72c97afa2ef90b93f05801edf3b5224816`.

`scripts/impacted-classes.ps1 -Range` (no `-MergeSubset`): kinds `server-git-start-sites,test-lane-category`. The Linux treenode filter is the CP-1 filter above, plus `tests/Antiphon.Server.Git.Tests/GitIndexLockTests.cs`, `tests/Antiphon.Server.Git.Tests/ServerGitAssemblyTests.cs`, `tests/Antiphon.Tests/Infrastructure/ServerGitProcessTests.cs`, and `tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs`. CP-4 is the Server.Git pair. No `windowsFilter` line. The same range with `-MergeSubset` prints `migrations=False`. Downgrade selector count is 0. The merge fixed subset is not an extra owed row of this review.

## OS witness and performance

Changed production behavior is the `OperatingSystem.IsWindows()` branch in `CardFileRepository.RunGitInputAsync`. The real object is `CardFileRepository`, reached by the acceptance fixture's `Repository().CommitAsync`. No OS-witness JSON is committed at this tip, so `scripts/check-os-witnesses.ps1` was not run. Native status stays pending. Startup scripts and AppHost are untouched. Restart performed: none. A later server activation is what picks the fix up. This review does not restart.

The new test shares one scratch repository across its eight rounds and records no per-test timeout above the class budget. It is not a 30-minute Debug. CP-1's slow class is the pre-existing `ImpactedClassesSelectorTests` at 420 s (42 tests), inside a row that finished in 476.817 s.

## Advisories

These do not hold the land and are not a Code repair of this fix:

- `TryKill` skips `Kill(true)` once the job kill has already exited git, so a pre-assign grandchild is outside both the job and the fallback. The hook-already-running race is inside the job.
- `linux-test-roster.json` `sourceSha256` and `directTestMethods` for `CardFileGitFailureAcceptanceTests` are stale. Nothing in `Assert-LinuxTestRoster` reads them.
- The three junction skips block a zero-skip certificate. They are inherited Windows fixtures in an impacted class.

Positive controls stay pending. No producer or recipient queue changed. No input wait was added. Parking is irrelevant.
