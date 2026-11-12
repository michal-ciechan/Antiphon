# Harness hangs: antiphon g118 and session-runner g4 (Windows run ae84478d)

Read-only evidence pass. Run `ae84478dd83d4cde8f537ba8303794fb` (rc-2026-11-17,
`3db1a43b0`), log dir
`C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d`,
timing root `...\logs\timing-ae84478dd83d4cde8f537ba8303794fb`. Comparison:
run `d88de419` (rc-2026-11-16). Source line numbers are at `56e96eabf`
(rc/2026-11-18, the live run 0b292fa4). The leaking code is the same there, so
0b292fa4 can hit both hangs again.

Both chunks passed every test (g118 146/146, sr-g4 133/133). In both, the test
host exited normally (`root ... exited=true`). One process from a test was
still alive in the harness job, so the chunk ended as `completed-but-hung`.

## How the harness hangs (both chunks)

`scripts/lib/nightly-owned-process.cs`, `NativeProcessOwner.Run`:

- Line 127-132: a Job Object with `KILL_ON_JOB_CLOSE` and no breakaway. The
  test host is assigned at line 178, so every descendant is in the job,
  including a PtyHost that tries to break away (`--spawn`).
- Line 133-148: the stdout and stderr pipes. The root gets them through an
  explicit `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`. A child the test host starts
  with `Process.Start` uses `bInheritHandles=TRUE` and no list, so it can also
  inherit those write ends.
- Line 188-201 and `NightlyCompletionGrace.Wait` (line 850): the chunk is done
  only when `RootExited(root) && Count(job) == 0`. After `total ran` and
  `request execute stop` appear, it waits `DefaultMilliseconds = 60000`
  (line 847). Then it sets `CompletedButHung`, writes `<log>.hang.txt`
  (line 222) and calls `TerminateJobObject` (line 225).

What gates completion is the job's active-process count, not pipe EOF. One
live descendant holds the chunk whatever handles it has. The capture lists
handle types only, not names, so it does not show whether the survivor held
the stdout or stderr write end. After `TerminateJobObject`, both drains
finished in 11.8 ms (g118) and 21.8 ms (sr-g4). Nothing outside the job held
the pipes.

On Linux the harness uses no Job Object. A stray grandchild there does not
gate the chunk, and neither leak below happens on Linux.

## (A) antiphon `all-group-118`: orphaned MSYS `sleep 90`

### Evidence

- `antiphon-all-group-118-tests.log.hang.txt`: `root pid=106536 exited=true
  active=1`. 3262 descendants, and only one is alive:
  `descendant pid=82328 image=sleep exited=false handles=104`.
- `owner-antiphon-all-group-118-ae84478d...-1.json` (pid, image, start and
  exit times; times are UTC on 2026-11-12):

| start | exit | pid | image | role |
|---|---|---|---|---|
| 11:04:15.163 | 11:04:16.247 | 43680 | pwsh | crash worker (`process`), killed `entireProcessTree:false` |
| 11:04:15.175 | 11:04:22.396 | 71128 | pwsh | `unrelated` StartSleeper |
| 11:04:16.174 | 11:04:17.992 | 73536 | git | `cmd\git.exe` launcher (journal child) |
| 11:04:16.225 | 11:04:18.012 | 81088 | git | mingw `git.exe commit` |
| 11:04:16.246 | 11:04:17.945 | 100948 | pwsh | recovery worker |
| 11:04:16.266 | 11:04:18.027 | 50792 | sh | pre-commit hook (`#!/bin/sh\nsleep 90`) |
| 11:04:16.318 | **11:04:16.355** | 21788 | sh | MSYS fork child; exits 18 ms after exec |
| 11:04:16.337 | **11:05:38.156** | 82328 | sleep | exec'd `sleep 90`; Windows parent 21788 is dead |

- TRX test: `RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced`,
  11:04:14.964-11:04:22.455. The tests in that window run one at a time.
- The root exited at 11:04:37.830. 60 s of grace ended at 11:05:38, and
  `TerminateJobObject` killed the sleep at 11:05:38.156. The sleep would have
  exited on its own at about 11:05:46.3, 8 s too late.
- d88de419 is identical (`antiphon-all-group-118-tests.log.hang.txt`, same
  roster position, a live `sleep` pid 352316). C24 ran 12:20:23.9-12:20:36.0.
  The sleep started 12:20:25.505. Its fork-sh 373264 exited at 12:20:25.527.
  git and the hook were killed at 12:20:28.68-.70. The root exited at
  12:20:52.995 and the harness killed the sleep at 12:21:53.306, 2.2 s before
  it would have ended.

### Spawning test and why its kill misses

`tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs`,
`C448_C24_KilledWorkerLeavesLiveGitChildFenced`:

- Line 487 installs the hook `#!/bin/sh\nsleep 90\n`. A pwsh crash worker
  (line 491-512) runs the real `LandingGit` commit, which blocks in that hook.
- Line 538 kills the worker with `entireProcessTree: false` on purpose. The
  test asserts that the git child stays alive.
- Line 565 `child.Kill(true)` and the `finally` at line 580 are the only
  cleanup. `child` is the journaled git pid.

Git for Windows runs the hook as `sh.exe`. MSYS bash runs `sleep` by fork plus
exec. The forked Windows process (21788) starts `sleep.exe` and exits about
20 ms later. Windows keeps the dead pid as the sleep's parent pid.
`Process.Kill(entireProcessTree: true)` on Windows walks live parent-pid edges
from git: 73536, 81088, 50792. pid 21788 is gone, so the sleep cannot be
reached. TerminateProcess on bash sends no MSYS signal to its child. The test
still passes, because it checks only the git child, and the 90 s sleep
outlives the chunk.

On Linux bash forks, and exec keeps the same pid, so `sleep` is a live child
of `sh`. `Kill(true)` (a /proc walk) reaches it. Linux also has no job gate.

g118 hangs only when C24 runs less than about 30 s before the chunk ends
(90 s sleep minus 60 s grace). Group membership decides that. Every g118 hang
on record (d88de419 and ae84478d) has this signature.

### Related: `C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion` (line 46)

Its hook (`printf ready; sleep 90`) leaves the same kind of orphan (sh 119312
exited at 11:04:32.440, sleep 35100 started at 11:04:32.430). The product
kill (`OwnedProcessTree`, TRX stderr
`repository_child_tree_kill root=110424 descendants=3
killed=110424,18072,98020,77624 ... followers=0`) did not target the sleep. The
sleep died at 11:04:32.533, about 100 ms after it started and about 25 ms after
bash was killed. It was killed only because the test cancels right after the
`ready` marker, while the sleep is still starting up. That is luck, not
cleanup. d88de419 shows the same (sleep 289776, 170 ms old).

The product `OwnedProcessTree` (`server/Infrastructure/Git/OwnedProcessTree.cs`
`Plan`, line 86) has the same blind spot. A dead fork stub's row is missing
when the snapshot is taken, so the edge to the exec'd grandchild is not
walked. A real Windows land whose hook runs a long MSYS command would leave
that command running after a cancel. This is not what hung g118, but it should
get its own card.

### Owner of the cleanup

`RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced`.
The test spawns the tree, so the test must own the whole tree, not a pid
walk:

- Preferred: create a test-owned Windows Job Object (`KILL_ON_JOB_CLOSE`).
  Assign the crash worker to it right after `Process.Start` at line 512, and
  close it in the `finally` at line 580-585. Killing the worker at line 538
  does not close the job, so the test's intent (git outlives its worker)
  still holds. `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs`
  already uses job-object interop that can be reused.
- Cheaper alternative: have the hook exit early instead of sleeping a fixed
  90 s, for example by polling for a release file that the `finally` writes,
  with an upper bound under 30 s. Under the 60 s grace, a leaked sleep could
  then never outlive the chunk. Apply the same change to V28's hook at line 46.

A plain `exec sleep 90` is not recommended. It depends on how MSYS treats a
stub under a non-Cygwin parent.

## (B) session-runner `all-group-4`: wedged `Antiphon.PtyHost`

### Evidence

- `session-runner-all-group-4-tests.log.hang.txt`: `root pid=111844
  exited=true active=1`. Only `descendant pid=118772 image=Antiphon.PtyHost
  exited=false handles=263` is alive.
- `owner-session-runner-all-group-4-ae84478d...-1.json` (UTC, 2026-11-12):

| start | exit | pid | image | role |
|---|---|---|---|---|
| 03:57:45.920 | 03:57:46.114 | 158380 | Antiphon.PtyHost | `--spawn` launcher, launch A |
| 03:57:46.100 | 03:57:46.747 | 123072 | Antiphon.PtyHost | host A |
| 03:57:46.603 | 03:57:46.747 | 147284 | OpenConsole | A console: lives until host A closes it |
| 03:57:46.624 | 03:57:46.680 | 123380 | cmd | A child, killed by `KillAsync` |
| 03:57:46.791 | 03:57:46.976 | 30764 | Antiphon.PtyHost | `--spawn` launcher, launch B |
| 03:57:46.954 | **04:02:28.546** | 118772 | Antiphon.PtyHost | host B, killed by `TerminateJobObject` |
| 03:57:47.422 | **03:57:47.512** | 42984 | OpenConsole | B console: dies 4 ms after cmd while host B lives |
| 03:57:47.441 | 03:57:47.508 | 102748 | cmd | B child |

- TRX test: `RunnerSessionGenerationTests.C502_V26_kill_and_relaunch_publish_their_own_generations`,
  03:57:45.558-03:57:47.523 (`[NotInParallel("SessionLiveness")]`).
- The root exited at 04:01:28.181. 60 s of grace ended, then the harness
  killed the job at 04:02:28.5.
- The other hosts in this chunk whose child exited, for example the C514
  hosts 145760 and 118560, exited 72 s later. That is
  `PtyHostLingerHours = 0.02`. Host 118772 lived 4 min 41 s, so its linger
  never fired.
- `%TEMP%\antiphon-c502-v26-4e158ffeee0e481986f0f7e5d0aa47e5\pty-hosts\bin\20261112-035745-387dd8cf\`
  is still there, with `Antiphon.PtyHost.exe` and its dlls. Its mtime is
  04:01:27.76, the test host's assembly teardown. The sweep's
  `Directory.Delete` removed the host logs but not the exe, which the running
  host still held. The host log is gone, so the internal state below is
  inferred from process timing and source.

### Spawning test and the sequence

`tests/Antiphon.SessionRunner.Tests/RunnerSessionGenerationTests.cs`, lines
59-88:

1. Line 79 relaunches session B (host 118772, interactive `cmd`).
2. Line 84 `runtime.SweepVanishedSessions(new StubProbe(false))`. The stub
   claims that the live child is dead. `RunnerSession.MarkVanishedIfDead`
   (`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:4739`) publishes the
   exit and runs `_ = Task.Run(ShutdownHostAsync)` without waiting (line
   4788). That sends a Shutdown ack to a host whose child is still running.
3. Line 87 `KillBestEffort(runtime.Get(sessionId).Pid)` (helper at line
   264-269) kills the child pid only, never the host. Then `await using
   runtime` detaches without killing (`SessionRunnerRuntime.DisposeAsync`,
   line 2677).

In host B, `HostSession.Shutdown` (`src/Antiphon.PtyHost/HostSession.cs:403`)
calls `RequestExit`. `PtyHostServer.RunAsync` returns, and Program's
`await using var session` runs `HostSession.DisposeAsync` (line 572). That
calls `_runner.DisposeAsync()` (`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:779`),
which on Windows calls `ModernConPtyConnection.Dispose()`
(`src/Antiphon.Agents.Pty/ModernConPtyConnection.cs:394`):

- Line 399 sets `_disposed = true`. Line 402 unsubscribes `OnProcessExited`.
  Then `CloseConsoleForDrain` (`ClosePseudoConsole`) ends OpenConsole and its
  client. This matches OpenConsole 42984 dying at 47.512, 4 ms after cmd. In
  launch A, OpenConsole lived until host A exited.
- Line 432 `OnProcessExited` returns at once when `_disposed`, so `Exited`
  never fires. `PtyAgentRunner._completion` is never set.
- `HostSession.DisposeAsync` line 581-582 `await _exitObserver` waits on
  `ObserveExitAsync`, which waits on `_runner.Completion` (line 447). It never
  completes. The linger timer is never armed, because `StartLingerExpiry` (line
  473) runs only after `Completion`. Even armed, it would only call
  `RequestExit` again. The host waits forever.

If the test's child kill is seen first, the exit is observed, the host lingers
72 s and exits, and the chunk does not hang. That race is why this is
intermittent. `C502_V26_kill_and_relaunch` was in `session-runner-all-group-4`
in 7 runs: 8e938b6d, 231f573c and dce78ee0 timed out (the 1801 s launch
watchdog, from before the hang capture existed), ae84478d was
`completed-but-hung`, and c3f65ddb, a6410818 and 6591e378 were clean. The
2026-11-10 note
(`2026-11-10-why-missed-session-runner-group4-post-completion-hang.md`) could
not name the survivor. This capture names it.

On Linux the hang does not occur. `PtyAgentRunner.DisposeAsync` takes the Unix
path (`_conn.Dispose()` first, then the reader). That lane uses PortaPty, not
`ModernConPtyConnection`. The harness has no job gate, and
`PtyHostLeakSweep` runs only on Windows.

### The assembly safety net missed it

`tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs`: `[After(Assembly)]
SweepLeakedPtyHostsAsync` kills leaked hosts under registered roots. But
`RootFromHostPath` (line 178) matches only the 7 original prefixes
(`cpu-watchdog-tests|liveness-tests|adoption-tests|bufbounds-tests|backend-seam|first-write-race|0180-dto`).
`KnownPrefixes` (line 15-25) gained `c502-v26` and the other prefixes in
`32bb2a52f` (CARD-0502), but the regex was not updated. For host 118772,
`root` was null and the loop `continue`d. The host survived, and the root
directory delete failed on the locked exe.

### Owner of the cleanup (three layers)

1. Product, the root cause:
   `src/Antiphon.Agents.Pty/PtyAgentRunner.cs` `DisposeAsync` (line 779). After
   `_conn?.Dispose()`, complete `_completion` (and `_exitTcs`) with a disposed
   or killed observation, so no awaiter of `Completion` is left hanging. An
   alternative is to stop `ModernConPtyConnection.Dispose` from dropping the
   exit (line 399-402), or to bound `HostSession.DisposeAsync`'s
   `await _exitObserver` (`HostSession.cs:581`). A host given Shutdown or any
   `RequestExit` while its child lives must still exit. This is
   production-relevant (it leaks hosts on the desktop), not only test hygiene.
2. Test: `RunnerSessionGenerationTests.C502_V26_kill_and_relaunch_publish_their_own_generations`
   line 84-87. Capture the B host pid and start time before the sweep and end
   with `TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId,
   hostPid, hostStart)` (`PtyHostLeakSweep.cs:42`) instead of
   `KillBestEffort(child pid)`. `KillBestEffort` on the child pid has the same
   gap at line 126 and line 172 of that class.
3. Safety net: `PtyHostLeakSweep.RootFromHostPath` (`PtyHostLeakSweep.cs:178`).
   Build the alternation from `TestSessionLogRoot.KnownPrefixes` instead of
   the hard-coded list.

## Latent risk found on the way

`PtyHostLingerHours = 0.02` (72 s) is longer than the 60 s completion grace. A
session-runner test that relies on linger (child killed, host left to time
out) and runs in the last ~12 s of a chunk will be marked `completed-but-hung`
with no bug at all. Teardown should kill the host explicitly
(`KillAndAwaitHostExitAsync`), or the grace or linger values should be aligned.

## Next stage

A Debug or Code task on master, then ported to the next rc cut:

- (A) `RepositoryMutationLeaseTests.cs`: C24 job-owned worker tree, or a
  short release-file hook, and the same hook change for V28.
- (B1) `PtyAgentRunner.DisposeAsync`: complete `Completion` on dispose. Red
  test: a host or runner given Shutdown with a live modern-ConPTY child must
  exit within a bound, Windows-only.
- (B2) `RunnerSessionGenerationTests` teardown via
  `KillAndAwaitHostExitAsync`.
- (B3) `PtyHostLeakSweep.RootFromHostPath` derived from `KnownPrefixes`.
- A follow-up card for `OwnedProcessTree` and orphaned MSYS exec grandchildren
  in production lands.
