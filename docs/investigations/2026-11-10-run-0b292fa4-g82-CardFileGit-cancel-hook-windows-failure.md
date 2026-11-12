# run 0b292fa4 antiphon all-group-82: CardFileGit cancel leaves the owned hook running (Windows)

Read-only desktop read, task e92f04ad, 2026-11-12. Run `0b292fa4bc3b40158e8950fb17804a21` on
rc/2026-11-18 = `56e96eabf698e9c1fcd462836ad32ed1fb259e83`, desktop (Windows 10.0.19045,
.NET 9.0.23, TUnit 1.44). Log dir
`C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs\2026-11-12-1213-0b292fa4\`.
Source line numbers below are at `56e96eabf`. `CardFileRepository.cs` and the test file are the
same on origin/master (empty diff).

## Verdict

This is a product gap, not a test bug, and the assertion is correct. On Windows,
`CardFileRepository` cancels a git command with `Process.Kill(entireProcessTree: true)`
(`server/Infrastructure/Git/CardFileRepository.cs:386-399`, `TryKill`). That is a top-down
parent-pid walk. It is not atomic, and it cannot see MSYS descendants whose Windows parent
already exited. The hook's own shell kept running after the cancel. It reached its
wait-failed branch and wrote `hook-wait-failed`. This is the CARD-1850 family (no Job Object
containment of the owned git tree), but on a different path. CardFileRepository does not use
`OwnedProcessTree` or `OwnedChildMarker`, and does not share LandingGit's kill code. CARD-1850
as written covers only `OwnedProcessTree.Plan`, so it does not cover this path.

The failure depends on timing and load. It is not deterministic. Locally the race fired in 2
of about 133 cancels, and only when the cancel landed between the hook's fork and its exec of
the first `ping`. A related leak is deterministic: an orphaned MSYS grandchild survives the
same kill every time (4/4). A Job Object kill removes it (4/4). See "Witness" below.

## Files read (name-filtered)

| file | size | note |
|---|---|---|
| `antiphon-all-group-82.trx` | 683199 | 358 results |
| `antiphon-all-group-82-tests.log` | 3590 | console log |
| `antiphon-all-group-82.timing.json` | 2943 | chunk 15:59:29.14Z-16:03:04.30Z, runWall 214.96 s, build 573 s, p95 12.25 s |
| `antiphon-all-group-82-execution\log_261112155929339.diag` | 1331182 | Trace diag; only TestNode updates, nothing beyond the TRX |

Chunk total: 358, failed 1, succeeded 357, skipped 0, duration 3m 33.856s. No `.hang.txt`.

## The failure

`Antiphon.Tests.Application.CardFileGitFailureAcceptanceTests.Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed(cancel)`.
It ran 15:59:47.809Z-15:59:53.363Z, 5.554 s.

```
ShouldAssertException: File.Exists(Path.Combine(root, "hook-wait-failed"))
    should be
False
    but was
True
   at ...CardFileGitFailureAcceptanceTests.AssertOwnedHookKilledAsync(String root) in ...CardFileGitFailureAcceptanceTests.cs:line 134
   at ...CardFileGitFailureAcceptanceTests.Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed(String fault) in ...CardFileGitFailureAcceptanceTests.cs:line 84
```

Expected `False`, actual `True`. Stdout has only the `ANTIPHON_TEST_PHASE_ID ...4.1.0` line. The
test logs nothing else, because the repository uses `NullLogger`.

Assertions that passed before the failing line, and what they establish:

- line 73: `hook-started` exists, so the pre-commit `StallHook` ran.
- line 74: the commit task threw `OperationCanceledException`. `CommitAsync` returns `git_error`
  immediately on a failed commit (`CardFileRepository.cs:259`) and runs no git command after
  it. So the cancel reached `RunGitInputAsync` while the `git commit` process was still
  running, and `TryKill` ran on it (`CardFileRepository.cs:365-369`).
- line 82: HEAD was unchanged.
- line 83 and line 133: `hook-ended` was absent.

What the marker means. The hook (test lines 116-128, Windows variant):

```
#!/bin/sh
echo $$ > hook-pid
echo started > hook-started
while [ ! -f hook-release ]; do
  ping -n 2 127.0.0.1 >/dev/null 2>&1 || { echo wait-failed > hook-wait-failed; exit 1; }
done
echo ended > hook-ended
```

Only the hook's main shell writes `hook-wait-failed`, after its `ping` command returns
non-zero. So the main shell was still executing after the cancel kill, and its waiter had
died first. The owned hook was not killed as a unit.

## Siblings (same class, all in this chunk)

| outcome | test | window (UTC) | duration |
|---|---|---|---|
| Passed | `Missing_desired_or_reappearing_deleted_file_refuses_byte_recheck(missing)` | 15:59:45.305-15:59:50.156 | 4.85 s |
| Passed | `Missing_desired_or_reappearing_deleted_file_refuses_byte_recheck(reappeared)` | 15:59:45.474-15:59:51.615 | 6.14 s |
| Passed | `Git_failures_..._removed(timeout)` | 15:59:47.126-16:00:12.603 | 25.48 s |
| **Failed** | `Git_failures_..._removed(cancel)` | 15:59:47.809-15:59:53.363 | 5.55 s |
| Passed | `Git_failures_..._removed(lock)` | 15:59:50.177-15:59:57.493 | 7.32 s |
| Passed | `Worktree_change_from_real_post_index_hook_is_detected_after_staging_before_commit` | 15:59:51.617-15:59:58.070 | 6.45 s |
| Passed | `Git_failures_..._removed(stderr)` | 15:59:53.377-15:59:59.430 | 6.05 s |

What the passes bound:

- `(timeout)` ran the same `StallHook` and the same `TryKill` concurrently, and passed the same
  `AssertOwnedHookKilledAsync`. It is killed after the 20 s product budget, when the hook is in
  steady state: the fork stub and `PING.EXE` are alive and reachable by the walk. The same kill
  in steady state succeeds.
- `(lock)` and `(stderr)` do not kill anything.
- The `Missing_*` and post-index tests run no stall.

So the fault needs the cancel kill to land early in the hook. Only `(cancel)` does that: it
cancels within ~25 ms of `hook-started`, while the hook is starting its first `ping`.

## What ran at the same time

The `(cancel)` window overlapped about 90 tests, under heavy load:

- the `(timeout)`, `Missing_*`, `(lock)` and post-index siblings, each in its own scratch repo
  (no shared files);
- 17 `AgentTaskLandReceiptWatermarkSafetyTests.C1121_*` DB tests;
- `SessionMessageQueuePtyIntegrationTests` (two pty tests);
- `BootStallAttentionTests`, `ComplexityChainRaceTests`, `ComplexityRoutingWalkTests`;
- three `DispatchConcurrencyMigrationTests` (42-43 s each);
- `DispatcherAdmissionSchedulingTests.C1519_*`;
- many sub-ms `CheckInterpretationValidatorTests`.

`[ParallelLimiter<GitProcessSpawnLimit>]` let the four `Git_failures_*` rows overlap. The
chunk's live qualification was running on the same host.

## Classification: timing/load race (not an assertion on returned state)

Evidence:

1. The failing assertion is on a side effect of a race inside the cancel kill, not on a value
   `CommitAsync` returned. The returned state was correct (OCE, HEAD unchanged).
2. Local replay on this desktop, read-only apart from scratch repos under
   `<worktree>/.antiphon/probe`. It used the same hook, `git commit` from PATH (`cmd\git.exe`
   launcher), redirected streams with `CreateNoWindow`, then `Process.Kill(true)` after
   `hook-started`. pwsh 7 / .NET 10 has the same `Kill(entireProcessTree)` walk.
   - A steady-state kill (hook looping for 5 min) killed all five processes and wrote no marker:
     launcher, mingw `git.exe`, hook `sh`, fork-stub `sh`, `PING.EXE`.
   - Kill delays of 15-500 ms after `hook-started`: 2 hits in 16. Both wrote
     `wait-failed rc=127`. Both pre-kill snapshots showed only the hook `sh` and its fork child,
     with no `PING.EXE` yet. The cancel landed between fork and exec. The fork child had started
     1.2 s and 0.5 s after the hook shell, so fork was slow under load.
     - hit 1: `hookStartedMs=2556 killMs=121 before=[sh:107352<-26960 sh:156308<-107352]`
     - hit 2: `hookStartedMs=341 killMs=178 before=[sh:100808<-140920 sh:140920<-158408]`
   - Other batches gave 0 hits in 117 kills (0/8, 0/20, 0/14, 0/42, 0/32). These batches held
     handles on the descendants and recorded exit order. Every run exited strictly top-down
     (launcher, git, sh, sh, PING), all with code -1.
   - Overall: 2 hits in about 133 cancels, roughly 1.5%.
3. `Kill(true)` took 62-774 ms per call on this host (`killMs`). It kills one level, then takes a
   full process-table snapshot to find the next level. The hook's MSYS fork/exec keeps going
   during that window.
4. rc=127 is the exit status bash saw for the fork child. MSYS/Cygwin maps a Windows exit
   status it did not set itself to 127: an externally terminated Cygwin child, or a failed exec
   or DLL init. Either way the in-flight fork child ended before the hook shell's own
   termination took effect, and the shell ran its `||` branch.

What is not proven: which exact interleaving inside the walk let the hook shell outlive its
fork child. The exit-code probes that held handles never hit. The product consequence does not
depend on the interleaving. A pid walk is not an atomic owned-tree kill, and the hook ran code
after the cancel.

## Product path and relation to CARD-1850 / g118

- `CardFileRepository.RunGitInputAsync` (`CardFileRepository.cs:321-384`): on
  `OperationCanceledException` it calls `TryKill(process)`, which is
  `process.Kill(entireProcessTree: true)` (`:386-399`). There is no Job Object, no
  `OwnedChildMarker`, no `OwnedProcessTree`, and no wait for the tree to be reaped.
- LandingGit is separate. It uses `OwnedProcessTree` (snapshot walk with start ticks) plus
  `OwnedChildMarker` (`server/Infrastructure/Git/OwnedChildMarker.cs`: a Windows Job Object
  assigned right after `Process.Start`, and `TerminateMembers` = `TerminateJobObject`) in
  `ReleaseChildAsync` (`LandingGit.cs:422`). CardFileGit shares none of it.
- g118 / CARD-1850 (`docs/investigations/2026-11-12-harness-hangs-g118-srg4-windows.md` on
  `origin/feat/card-task-ed1f9715`): an MSYS fork stub exits about 20 ms after exec. The exec'd
  grandchild then has a dead Windows parent, and any parent-pid walk misses it. Same root:
  Windows owned-tree kill by pid walk instead of containment.
- This failure shows a second symptom of that root on a different call site: the walk is not
  atomic, so hook code runs during it.

## Witness (deterministic, Windows)

Shape (scratch-repo probe; the same `Process.Kill(true)` call `TryKill` makes):

```
pre-commit:  #!/bin/sh
             echo started > hook-started
             sh -c 'sleep 2; echo leaked > leaked'
start git commit; wait for hook-started; +300 ms; cancel (Kill(true)); wait 4 s;
assert !File.Exists("leaked")
```

The hook forks, and the fork child execs a second MSYS `sh` and then exits. That `sh` has a dead
Windows parent, so the walk cannot reach it. It finishes `sleep 2` and writes `leaked`.

- With `Process.Kill(true)`, as in today's `TryKill`: **leaked 4/4 (red)**.
- With a Job Object created before start, `AssignProcessToJobObject` right after
  `Process.Start`, and `TerminateJobObject` on cancel: **leaked 0/4 (green)**.
- `exec sh -c ...` directly in the hook is **not** a witness (0/3 leaked). MSYS keeps the hook
  shell alive as a stub under a non-MSYS parent, so the walk still reaches its child. Use the
  fork-then-exec form above.

This is a Windows-only red. On Linux exec keeps the pid and the parent link, so it is green
both before and after the fix.

## Proposed smallest production fix

In `CardFileRepository.RunGitInputAsync`, reuse `OwnedChildMarker` (same namespace,
`Antiphon.Server.Infrastructure.Git`):

1. `var owned = OwnedChildMarker.Create(log)` before start, and `owned.Apply(psi)`.
2. Right after `Process.Start`, call `owned.Attach(process, log)`.
3. In both `TryKill` paths (cancel, timeout or exception), call
   `owned.TerminateMembers(process.Id, log)` **first**. That one `TerminateJobObject` call kills
   every job member at once, including reparented MSYS grandchildren. Then keep
   `Kill(entireProcessTree: true)` as the backstop for a child the root started before
   `Attach`.
4. Dispose `owned` in `finally`.

Residual gap, not closed by this: a process the root spawned before `Attach` is outside the job.
In practice that is a `cmd\git.exe` launcher spawning mingw git within ms. Closing it needs
spawn-into-job: `CREATE_SUSPENDED` then assign then resume, or `PROC_THREAD_ATTRIBUTE_JOB_LIST`.
That is the same change CARD-1850 needs for LandingGit, and should be one shared helper.

Recommended tracking: widen CARD-1850 to "Windows owned git tree kill uses a Job Object on every
server git spawn (LandingGit + CardFileRepository)", or file a sibling card that cites this doc.

## Second, separate test defect (do not fix by weakening)

`StallHook` writes `echo $$ > hook-pid`. Under MSYS, `$$` is the MSYS pid, not the Windows pid.
Observed: `hook-pid` = 20115 for a hook shell whose Windows pid was 153524. A one-off
`sh -c 'read w < /proc/$$/winpid'` gave msys=20135, win=34432.

`HookAlive` (test lines 148-160) calls `Process.GetProcessById(msysPid)` on Windows. So the
liveness proof at line 139 checks an unrelated pid:

- it is vacuous when no such pid exists;
- it can fail spuriously when an unrelated process happens to own that pid.

Fix in the test: on Windows write the Windows pid (`read w < /proc/$$/winpid; echo $w > hook-pid`)
and keep every assertion.

## Why missed before the rc run

- Linux never sees it. bash `fork` + `exec` keeps the pid, and the git lane runs on server2.
- On Windows the race window is only the fork-to-exec gap of the hook's first `ping`. That gap
  is short on an idle host and only becomes wide (0.5-1.2 s) under qualification load. It fired
  in about 1.5% of local cancels.
- The Windows liveness proof (`HookAlive`) used the MSYS pid, so a surviving hook shell would not
  have been reported by that check anyway. Only the marker files catch it.
- `CardFileRepository`'s kill was never brought under the Job Object work done for LandingGit
  (`OwnedChildMarker`), and CARD-1850 scoped only `OwnedProcessTree.Plan`.

## Housekeeping

The probe scripts and scratch repos live under the gitignored `<worktree>/.antiphon/probe/`. No
probe process was left alive (checked by command line). Nothing outside this worktree was
written. The live run, AppHost, runner and Docker were not touched.
