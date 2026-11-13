# CARD-1860 round 1 Review (715407ca): CLEAN

Subject: `origin/feat/card-task-042b61c0` tip `e7a0013a59573052dcd6fc7a164eb49e07fb932e`,
3 commits on master `cc48c70050a2932eddf4abe18cea8659d478c33e`. Code / landing owner
`042b61c0-ab0d-49e7-a298-93ce0cf8792b`. Round 1, Final profile, Full ordinary scope.
Executed on server2 Linux (pwsh 7, .NET 9). Restart: none. No live runner touched.

Verdict: **CLEAN**, no blockers. One inherited advisory filed as CARD-1863.

## What was checked

- `scripts/session-runner-restart-health.ps1`: the PID reader now also catches
  `DirectoryNotFoundException` and returns `$null`. It still creates nothing. Invalid,
  overflowing, non-positive and unreadable PID files (a directory, a lock, the wrapper)
  still throw. `Invoke-RunnerRestart` reads the Census before any control. That covers
  restart, `-Hard` and `-WaitOnly`, so all three fail closed with no control.
- Missing `logs/` versus a running supervisor: Census only ever reads `<Root>/logs`. A
  supervisor from this checkout writes its PID and log there, so a missing directory
  behaves exactly like the absent-PID-file case that CARD-1851 already accepted. A PID
  file kept elsewhere was never observed before this change either. No new no-supervisor
  path is reachable.
- Callers: `scripts/restart-session-runner.ps1` (dot-sources the helper; `-File` and `&`)
  and the root forwarder `restart-session-runner.ps1` are unchanged. The changed helper
  and the canonical script contain 0 non-ASCII bytes. The root forwarder already had one
  em dash before this change; it is not a daemon script.
- Independent probe of the real `New-RunnerRestartPlatform` Census with no `logs/`,
  under both `pwsh -File` and `&`. With the base helper (cc48c7005) the read throws
  `MethodInvocationException ... Could not find a part of the path` in both forms. With
  the tip helper it returns an absent supervisor and wrapper, and `logs/` stays absent.
  This is the CP-1 red/green.
- Fresh mirror: before CP-0, `regenerate-derived-impact-index.ps1 -NoBuild` (leased)
  exits 1 with `The application to execute does not exist:
  .../scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll`.
  That is the CP-2 precondition. After the CP-0 command row builds the driver,
  `regenerate_script_reports_changed_then_unchanged` passes in the same serial run.
- Test adequacy: the Hard/WaitOnly variants map to the existing directory/invalid setups.
  With WaitOnly the controls are always empty, so exitCode=1 is the discriminator: a
  swallowed read would let the probe report healthy (exit 0). The missing-logs method
  asserts that the Census creates nothing, the result is healthy, the control order is
  exact and `logs/` is still absent.
- Evidence diff `cc48c7005..e7a0013a5`: commits=3 entries=0 violations=0.
  `git merge-tree --write-tree origin/master HEAD` exits 0 (tree
  0448cff81346d298658e3d24671b397314202807; origin/master = cc48c7005).

## Checkpoint rows (unedited lines)

Run 20261113-004526-e535 (`--serial`, CP-0 then the consumers):

```text
CHECKPOINT CP-0 commit=e7a0013a59573052dcd6fc7a164eb49e07fb932e build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=e7a0013a59573052dcd6fc7a164eb49e07fb932e sourceState=clean buildSource=notApplicable
CHECKPOINT CP-5 commit=e7a0013a59573052dcd6fc7a164eb49e07fb932e build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-715407ca/.antiphon/checkpoints/20261113-004526-e535/rows/CP-5/run.trx slot=granted waited=90s dirty=0 source=e7a0013a59573052dcd6fc7a164eb49e07fb932e sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=e7a0013a59573052dcd6fc7a164eb49e07fb932e build=ok filter=/*/*/(DerivedImpactIndexTests*)|(ProcessSpawnLimitTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=136 passed=136 failed=0 skipped=0 trx=/work/worktrees/task-715407ca/.antiphon/checkpoints/20261113-004526-e535/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=e7a0013a59573052dcd6fc7a164eb49e07fb932e sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=e7a0013a59573052dcd6fc7a164eb49e07fb932e build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-715407ca/.antiphon/checkpoints/20261113-004526-e535/rows/CP-6/run.trx slot=granted waited=45s dirty=0 source=e7a0013a59573052dcd6fc7a164eb49e07fb932e sourceState=clean buildSource=verified
```

TRX breakdown for CP-4: DerivedImpactIndexTests 27, ProcessSpawnLimitTests 57,
TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5,
SlowTestTripwireTests 2 and NativeLinuxPolicyWitnessTests 3, all passed.
`regenerate_script_reports_changed_then_unchanged` passed (6.1 s).

CP-3 in that run was red (executed=20 failed=20 skipped=22) for an environmental reason:
every Linux case threw `ShellResolutionException: no readable regular file for: pwsh`,
because the fixture refuses the `/usr/local/bin/pwsh` symlink. The Code evidence used the
same `/opt/powershell` PATH prefix. This is an inherited fixture issue (CARD-1863). The
rerun with `PATH=/opt/powershell:$PATH`, run 20261113-012228-a43e:

```text
CHECKPOINT CP-3 commit=e7a0013a59573052dcd6fc7a164eb49e07fb932e build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=20 passed=20 failed=0 skipped=22 trx=/work/worktrees/task-715407ca/.antiphon/checkpoints/20261113-012228-a43e/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=e7a0013a59573052dcd6fc7a164eb49e07fb932e sourceState=clean buildSource=verified
```

In the CP-3 TRX, `Missing_logs_is_an_absent_pid_without_creating_the_directory` passed in
both pwsh forms, -File and &. Its 2 powershell.exe cases were skipped. All 12 pwsh
`Pid_read_errors_stop_restart_before_any_control` cases passed, including hard-/wait-
directory and invalid. The 22 skips are native Windows cases (CARD-1669 reason), which
is the deferred A-2 gate.

Source receipt: run 20261113-012917-2dee (CP-0 only):
`CHECKPOINT SOURCE VALID source=e7a0013a59573052dcd6fc7a164eb49e07fb932e rows=1`.
The strict validator refuses run e535 (report_failed, because of the environmental CP-3)
and run a43e (row_failed, because of the 22 native skips). Every row in both runs is
dirty=0 sourceState=clean with verified build provenance.

## Pending (never discharged here)

- Native Windows CP-3 under PowerShell 7 and Windows PowerShell 5.1. This includes both
  missing-logs entry forms and the Hard/WaitOnly variants. The desktop `&` restart
  acceptance is also caller/operator owned.
- PC-1, PC-2 (both variants) and PC-3 stay pending for a method-scoped SourceLanding Mutation.

## Advisories

- CARD-1863: the restart fixture refuses a symlinked pwsh. Without a PATH prefix, a plain
  server2 checkpoint run of this class is all red.
- Cost: run e535 took 36m42s wall. CP-4 build was 501 s and DerivedImpactIndexTests took
  606 s. This is already CARD-1861; it is not re-filed.
