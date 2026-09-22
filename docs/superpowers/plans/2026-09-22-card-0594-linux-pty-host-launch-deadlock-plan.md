# CARD-0594: Linux pty-host launch deadlock fix

Date: 2026-09-22. Stage: Plan, with TestDesign folded (D-6). Plan task `d9cee4ce`.
Source baseline: `c8e6b727774973bbc85110d1819562b923b49e9b` (investigation commit on
`feat/card-task-a69b4410`). That branch is checked out in the investigation task's worktree, so
this plan is committed on `feat/card-task-d9cee4ce` (same base) and pushed to both branch names.
Investigation: [2026-09-22-card-0594-linux-pty-host-launch-deadlock.md](../../investigations/2026-09-22-card-0594-linux-pty-host-launch-deadlock.md).
**Next: Code.**

## Outcome and scope

Make a Linux pty-host launch complete in the time it takes the host to bind its socket (well
under one second) instead of failing after launch-timeout plus connect-timeout (45 s). Three
fixes, all platform-independent C#, each independently correct:

1. `src/Antiphon.PtyHost/PosixProcessSpawner.cs` `BecomeSessionLeaderOrExit`: after `dup2`
   onto `/dev/null`, also close every descriptor above 2 that still aliases the inherited
   stdin/stdout/stderr, so the intermediary's redirect pipes reach EOF while the host lives.
2. `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs` `LaunchDetachedAsync`: gate success on the
   intermediary's exit plus its first stdout line (the pid), never on stdout/stderr EOF.
3. `src/Antiphon.PtyHost/PtyHostServer.cs` `RunAsync`: dispose the `NamedPipeServerStream` on
   the cancelled `WaitForConnectionAsync` path so the Unix socket file is unlinked.

Plus the verification that makes those provable on this Windows host: three new
Windows-executing TUnit tests, one Docker harness script that reproduces the investigation's
container repro (baseline red, fixed green), and a one-paragraph amendment to
`docs/testing-and-build.md`.

Not in scope (dispatch brief: "keep scope to the three named fixes"): the live V-7 Grok turn,
CARD-0594's bundled V-1 Linux arm (seven Linux-gated methods; the execution lane is CARD-0605),
the bundled PC-12 repoint and persistence-cut extension (CARD-0490 plan amendments), any other
pty-host behaviour, `docker-compose.runner-grok.yml`, the Dockerfile, and mounts (none is
involved; investigation §2 and §5).

No build, test, container or push of code ran in this Plan. All timings below quoted from the
investigation are its measurements on `antiphon-session-runner-grok:1.0.40`.

## Ground truth

| Card / brief assumption | What the code and evidence say at `c8e6b727` | Consequence for this plan |
|---|---|---|
| Card: "a mount/path-visibility issue between the host and container namespaces" | Runner, intermediary and host are one container, one mount namespace, one uid; `curl --unix-socket` from a sibling process connects and the host logs `Runner connected` (investigation §2). `/tmp` is not bind-mounted. | No compose, Dockerfile or mount change. |
| Card: "a timing/ordering issue in when the client attempts the connect relative to the bind" | True, inverted: the bind is 0.34 s after `POST /sessions`; the connect starts only after the host has already died at its 30 s launch timeout, because `LaunchDetachedAsync` is blocked (§3, table). `45.453 s = 30 + 15`; `PtyHostLaunchTimeoutSec=5` gives 20.6 s (§3.2). | Fix the launcher's gate and the host's descriptor leak; leave `PtyHostConnectTimeoutSec`/`PtyHostLaunchTimeoutSec` and `AwaitClientAsync`'s `launch + connect + 5` budget alone. |
| Brief: `PosixProcessSpawner.cs:44` is the primary fix | `BecomeSessionLeaderOrExit` (lines 41-70) calls `setsid`, opens `/dev/null`, `dup2`s it onto 0/1/2 and closes the spare fd. The live host's table shows fds 6 and 7 as write ends of the launcher's pipe after the `dup2` (§3.1). `StartDetached` (17-35) is a plain `Process.Start` with no redirects, so the host inherits the intermediary's 0/1/2, which are the launcher's pipes. | D-1. The aliases' provenance is unattributed (§6). The most likely source is CoreCLR's PAL, which `dup()`s fds 0/1/2 at startup before `Main`; the design does not depend on that: it closes any descriptor that aliases the original 0/1/2, whoever made it. |
| Brief: `PtyHostLauncher.cs:86` gates on an EOF the detach contract cannot deliver | Line 92: `await Task.WhenAll(stdoutTask, stderrTask, exitTask).WaitAsync(ct)` with both stream tasks being `ReadToEndAsync()`. `Process.WaitForExitAsync` waits for stream EOF only when `BeginOutputReadLine` was used, which it is not, so exit is observable independently of EOF. The pid is the first and only stdout line (`Program.cs:14`). | D-2. Success = exit observed plus a parseable first line; stderr is read eagerly but only consulted, with a bound, on the failure path. |
| Brief: `PtyHostServer.cs:45-49` leaves the server stream undisposed on cancel | Lines 47-50: `catch (OperationCanceledException) { break; }` with `pipe` never disposed; the served path disposes in its `finally` (66). Launch timeout reaches this path through `RequestExit` -> `ExitRequested` -> `lifetime.Cancel()` (`HostSession.cs:65-90`, `PtyHostServer.cs:25`). | D-3. Assumption to verify in the harness (H-3): disposing a Unix `NamedPipeServerStream` deletes the bound socket file (.NET deletes a bound `UnixDomainSocketEndPoint` path on `Socket.Dispose`). If H-3 shows the file surviving `DisposeAsync`, add an explicit `File.Delete(options.PipeName)` on non-Windows after the dispose, in the same slice. |
| Windows is unaffected | `Win32ProcessSpawner.Start` passes `bInheritHandles: false`; the Windows host inherits nothing and the launcher's pipes close when the intermediary exits (§4). `PtyHostLauncherTests` (4 methods) and `PtyBackendSeamTests` run real Windows hosts through the same launcher code. | Every launcher change must keep those green (R-1, R-3). D-1 is a no-op on Windows by construction (`--detach` is POSIX-only). |
| Investigation: `LinuxPtyHostLauncherTests` has never executed | All five methods open with `RequireLinux()` -> `SkipTestException`; the class already contains `Intermediary_pipes_reach_eof_while_host_lives` (asserts EOF within 5 s while the host lives and 0/1/2 -> `/dev/null`), which is the natural D-1 test and CARD-0490's PC-30 target. CARD-0605 (Backlog) owns giving the class a lane. The CARD-0590 `docker/tests/Dockerfile` `test-runner` target (SDK 10, runtime 9, full source) is a plausible substrate, but the test also asserts `libporta_pty.so` in the shadow dir, which a RID-less `dotnet run` may not place at the output root. | D-4. No new Linux-gated test in this card. The Windows-executing tests are platform-neutral so CARD-0605's lane executes them too. The Docker harness is this card's proof for D-1 (rule 4 of the checkpoint manifest: a test that cannot go red here is not a checkpoint). |
| `docs/testing-and-build.md:159` describes V-7 as blocked by the 30 s / 15 s mismatch | Stale after this fix; no test pins that paragraph (grepped `tests/` for its phrases). | S5 rewrites the paragraph to name the root cause, the three fixes and the harness. |
| The runner's `POST /sessions` repro is available on this host | Docker Desktop 29.5.3 (linux/amd64) is running; `antiphon-session-runner-grok:1.0.40` (built 2026-09-21 18:39Z, after every CARD-0490 pipe fix) is present locally; `docker/session-runner-grok/Dockerfile` publishes runner and PtyHost from the build context, so a fixed image builds from this branch. | CP-4 (baseline red on 1.0.40) and CP-5 (fixed green on the branch image) are runnable now, with no Linux test lane. |
| Card bundles (V-7 live turn, V-1 Linux arm, PC-12) belong to this dispatch | Dispatch brief restricts this card's pipeline round to the three fixes. | Recorded under Out of scope with owners; the live V-7 re-run is the natural follow-up Code/Review round on this card after land. |

## Decisions

### D-1. The host closes every descriptor that aliases its inherited stdio, by identity, on Linux

In `BecomeSessionLeaderOrExit`, before `setsid`, record the identity of fds 0, 1 and 2 as the
raw `readlink` target of `/proc/self/fd/{0,1,2}` (`pipe:[ino]`, `socket:[ino]`, or a path).
After the three `dup2` calls and the spare-fd close, enumerate `/proc/self/fd`, and for every
fd greater than 2 whose `readlink` target equals one of the recorded identities, `close(fd)`.
The enumeration's own directory descriptor and any fd that vanishes mid-scan are skipped. On
macOS (no `/proc`) the scan is skipped and the existing `dup2`-only behaviour stays; there is
no macOS runner and the card is Linux. `readlink` is either `new FileInfo(path).LinkTarget`
(raw target, no P/Invoke) or a `readlink(2)` P/Invoke; Code picks whichever it can verify
returns the raw `pipe:[n]` string in the harness (H-2 fd listing).

Rejected:

- **Blind `closefrom(3)` / `close_range(3, ~0)`.** Closes descriptors the running CoreCLR owns
  before `Main` (diagnostics IPC socket, PE image handles, the System.Native signal pipe once
  created) with undefined consequences. The defect is two specific aliases, not "everything".
- **`fstat` P/Invoke comparing `st_dev`/`st_ino`.** Exact, but `struct stat` layout varies by
  arch/libc and older glibc exports `__fxstat`; `readlink` identity is exact for pipes and
  sockets and equal-path for files, which is all stdio can be.
- **Rewiring the intermediary instead** (dup2 `/dev/null` onto its own 0/1/2 before
  `Process.Start`, print the pid through a saved `F_DUPFD_CLOEXEC` copy). The intermediary's
  own runtime aliases of the pipes would still be inherited unless they are CLOEXEC, so the
  same identity scan would be needed there anyway; the host-side scan covers both provenances
  (aliases made in the host before `Main`, and non-CLOEXEC aliases inherited from the
  intermediary) in one place.

### D-2. The launcher gates on intermediary exit plus the pid line; stderr is diagnostic only

Restructure `LaunchDetachedAsync` around two internal static seams (the class stays sealed,
the public signature is unchanged):

- `internal static IntermediaryReads BeginReads(Process intermediary)` starts
  `PidLine = StandardOutput.ReadLineAsync()`, `Stderr = StandardError.ReadToEndAsync()` (eager
  so a chatty failure can never fill the pipe and block exit; a fault observer is attached so
  a read that faults after dispose is never an unobserved exception) and
  `Exit = WaitForExitAsync()`.
- `internal static async Task<int> AwaitPidAsync(Process intermediary, IntermediaryReads reads, CancellationToken ct)`:
  `await reads.Exit.WaitAsync(ct)`; then `await reads.PidLine.WaitAsync(PidLineGrace, ct)`
  (5 s; the line was written before exit, so it is already buffered in the healthy case);
  exit code 0 and a positive parseable pid returns it. Otherwise collect stderr with
  `reads.Stderr.WaitAsync(StderrDrainGrace, CancellationToken.None)` (2 s, best effort, empty
  on timeout) and throw `InvalidOperationException` carrying the exit code, stderr and the
  stdout line, as today.
- `LaunchDetachedAsync` = start the process, `BeginReads`, `try { return await AwaitPidAsync }
  catch { await TryKillSpawnedHostAsync(...); throw; }`. The CARD-0086 kill keeps its 5 s
  bounded drain of `PidLine` and `Exit` on `CancellationToken.None` and kills the parsed pid.
  The kill stays out of the seam so a test that drives the seam with a fake intermediary can
  never kill an arbitrary pid it printed.

`Antiphon.PtyHost.Client.csproj` gains `<InternalsVisibleTo Include="Antiphon.PtyHost.Tests" />`
(precedent: `Antiphon.PtyHost.csproj`).

Rejected:

- **Keep `WhenAll(stdout, stderr, exit)` and add a timeout.** Turns a deadlock into a
  launch-timeout-long delay and still fails: the host would be dead by the time connect starts.
- **Stop redirecting stderr.** Loses the detach-failure text the host writes to the inherited
  stderr before `dup2` (`setsid failed`, `dup2 onto /dev/null failed`), which is the only
  diagnostic for a failed detach.
- **A fake `Antiphon.PtyHost.exe` in a temp source dir for the test.** `HostExeName` is fixed
  and a renamed `cmd.exe` cannot take `--spawn --session ...`; the seam is smaller.

### D-3. The server disposes the listening stream on the cancelled accept

```csharp
var pipe = new NamedPipeServerStream(options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, pipeOptions);
try { await pipe.WaitForConnectionAsync(lifetime.Token); }
catch (OperationCanceledException) { await pipe.DisposeAsync(); break; }
```

Constructor exceptions keep today's behaviour (they propagate; only OCE is caught). If H-3
shows the socket file surviving `DisposeAsync`, add `if (!OperatingSystem.IsWindows()) File.Delete(options.PipeName)`
after the dispose, wrapped so a missing file is not an error.

Rejected: wrapping the whole loop body in `await using` (the served path already disposes in
its own `finally`; a double dispose is harmless but the restructure is larger than the defect).

### D-4. Proof on this host is Windows TUnit for D-2/D-3 and the Docker harness for D-1; Linux TUnit stays CARD-0605's

The three new tests execute on Windows and are written platform-neutral (each has a Linux arm)
so CARD-0605's lane runs them unchanged. `LinuxPtyHostLauncherTests` is not a checkpoint row
here: it cannot go red on this host. The investigation's container repro is turned into a
script (D-5) and run twice, baseline red against `1.0.40` and fixed green against an image
built from the branch; that pair is the D-1 evidence and Review reruns it.

### D-5. `scripts/verify-card0594-linux-launch.ps1` is evidence tooling, not product

pwsh 7, ASCII-only, Docker Desktop only, never touches 17202-17205, one container per run
under `.antiphon/c594-harness/<stamp>/`, and prints a `C594 HARNESS` result line plus
`C594 HARNESS EXIT CODE: n`. No TUnit test of the script in this card (Not done, noted).

### D-6. TestDesign is folded into this plan; next stage is Code

The brief asks for the checkpoint manifest and the coverage decision, which is the TestDesign
content. `## Verification design` below is the executable manifest.

### D-7. One doc amendment, no new doc

`docs/testing-and-build.md` line 159's V-7 paragraph is rewritten (S5). The investigation
already carries the mechanism; `docs/adr/0002` is about the Windows ConPTY backend and is not
touched.

### D-8. Defaults this plan is written under

- `PidLineGrace = 5 s`, `StderrDrainGrace = 2 s` (D-2); constants, not settings.
- Harness port: any free port at or above 18200 (`-Port`, default 18299); baseline image tag
  `antiphon-session-runner-grok:1.0.40`; fixed image tag `antiphon-session-runner-grok:c594-<short sha>`.
- The macOS arm of D-1 is intentionally the old behaviour.

## Slices

### S1. Host: release inherited stdio aliases

Files: `src/Antiphon.PtyHost/PosixProcessSpawner.cs` (new private helpers
`RecordStdioIdentity()`, `CloseStdioAliases(IReadOnlySet<string>)`, `ReadLinkOrNull(string)`;
`close` P/Invoke already exists), summary comment updated to say why the aliases exist (PAL
`dup` of stdio at startup, or a non-CLOEXEC copy inherited across exec) and that closing by
identity is deliberate. No test file in this slice (D-4); evidence is CP-5 (H-1 direct-timing
probe green) against CP-4 (baseline red). `LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives`
is the test that will prove it under CARD-0605 and needs no change.

### S2. Launcher: exit-plus-pid gating and the seam

Files: `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs` (D-2 restructure; the CARD-0086 and
CARD-0045 comments survive), `src/Antiphon.PtyHost.Client/Antiphon.PtyHost.Client.csproj`
(InternalsVisibleTo), `tests/Antiphon.PtyHost.Tests/PtyHostLauncherTests.cs` (three new
methods, no OS skip on the first two):

- `Intermediary_result_does_not_wait_for_a_grandchild_holding_its_pipes`: start a real shell
  whose grandchild inherits and holds both redirected pipes after the shell exits. Windows:
  `cmd.exe /d /c "echo 4242& start /b ping -n 12 127.0.0.1"`. Linux/macOS:
  `/bin/sh -c "echo 4242; (sleep 12 &)"`. `BeginReads` then `AwaitPidAsync` must return
  `4242` within 5 s (assert elapsed under 5 s and result 4242) while the grandchild is still
  alive (assert `reads.Stderr.IsCompleted` is false at that moment). Red today: the equivalent
  gate waits about 12 s for EOF.
- `Intermediary_failure_reports_exit_code_and_stderr`: Windows `cmd.exe /d /c "echo boom 1>&2& exit 3"`,
  Linux `/bin/sh -c "echo boom 1>&2; exit 3"`; expect `InvalidOperationException` whose
  message contains `exit 3` and `boom`.
- `Intermediary_without_a_pid_line_fails_without_a_host`: Windows `cmd.exe /d /c "exit 0"`,
  Linux `/bin/sh -c "exit 0"`; expect `InvalidOperationException` containing `exit 0`,
  returned within 5 s.

The existing four methods are unchanged and remain the Windows real-host regression (R-1).

### S3. Server: dispose on cancel

Files: `src/Antiphon.PtyHost/PtyHostServer.cs` (D-3),
`tests/Antiphon.PtyHost.Tests/HostSessionPipeTests.cs` new method
`Launch_timeout_exit_releases_the_pipe_name` (no OS skip): `HostHarness.Start(o => o with { LaunchTimeout = 1 s, PipeName = <absolute temp path on non-Windows, default name on Windows> })`,
never connect, `await host.RunTask.WaitAsync(15 s)`, then
`new NamedPipeServerStream(host.Options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)`
must succeed (dispose it); on non-Windows additionally `File.Exists(host.Options.PipeName)`
is false. Red today on Windows (`IOException`: all pipe instances are busy, because the
undisposed instance is still open) and on Linux (`AddressAlreadyInUse` on the stale file).

### S4. Harness script

File: `scripts/verify-card0594-linux-launch.ps1`. Parameters: `-Image <tag>` (required),
`-Build` (build `-Image` from the checkout with `docker build -f docker/session-runner-grok/Dockerfile --build-arg SOURCE_REVISION=<HEAD sha> -t <tag> .`),
`-Expect fixed|baseline` (default `fixed`), `-Port` (default 18299, refused if in
17202-17205), `-EvidenceRoot` (default `.antiphon/c594-harness`). Steps and the exact
observations are in `### Docker harness` below. The script never deletes anything outside its
own container and evidence directory, and `docker rm -f` only the container it named.

### S5. Docs

File: `docs/testing-and-build.md`, the paragraph starting "V-7 is still blocked after those
three" (line 159 at baseline). Replace with: the launch blocker was CARD-0594's deadlock
(link this plan and the investigation): `LaunchDetachedAsync` waited for stdout/stderr EOF
that the detached host kept open through duplicate descriptors, so `Connect` only began after
the host's launch timeout; fixed in `PosixProcessSpawner` (close stdio aliases),
`PtyHostLauncher` (gate on exit plus pid line) and `PtyHostServer` (dispose on cancel);
evidence is `scripts/verify-card0594-linux-launch.ps1`; the live V-7 turn itself is still to
be re-run. Keep "Do not treat agent.Status=Running as a ready Linux host." Also add one
sentence under "Linux native ordinary" that `LinuxPtyHostLauncherTests` has no execution lane
until CARD-0605.

## Verification design

TestDesign folded (D-6). Bodies read: `PosixProcessSpawner.cs` (whole), `PtyHostLauncher.cs`
(whole), `PtyHostServer.cs` (whole), `Program.cs` (PtyHost), `Win32ProcessSpawner.cs:20-67`,
`HostSession.cs:60-90`, `PtyHostClient.cs:40-82`, `SessionRunnerRuntime.cs:2043-2075, 3209-3222`,
`SessionRunnerSettings.cs:65-68`, `PtyHostLauncherTests.cs` (whole), `LinuxPtyHostLauncherTests.cs`
(whole), `HostHarness.cs` (whole), `HostSessionPipeTests.cs:190-245`, `PipeTestClient.cs:1-80`,
`ProcessSpawnLimit.cs`, `PtyBackendEnvGuard.cs:1-40`, `docker/session-runner-grok/Dockerfile`,
`docker-compose.runner-grok.yml`, `docker/tests/Dockerfile`, `docs/docker-stack.md`,
`docs/testing-and-build.md:60-204`, `scripts/run-checkpoint.ps1:1-60`, CARD-0490 plan
lines 677-680 and 977-979.

### Inspection

- `PtyHostLauncherTests` | `[NotInParallel("Pty")]`, `[ParallelLimiter<ProcessSpawnLimit>]`,
  `SkipIfNotWindows` per method; `Launcher_reuses_the_shadow_copy_across_launches` waits up to
  20 s for two 5 s launch-timeout hosts -> the class costs about 1-2 min; the three new methods
  add under 10 s and carry no OS skip.
- `HostHarness.Start` | in-process `PtyHostServer.RunAsync` on `Task.Run`, `DisposeAsync`
  cancels and waits 10 s -> the S3 test observes `RunTask` completing on its own (launch
  timeout), then probes the name, and `DisposeAsync` is then a no-op cancel.
- `HostSessionPipeTests.No_launch_within_timeout_self_destructs_without_manifest` | boundary:
  a connected client; the new test is the never-connected variant of the same exit path.
- `PtyHostClient.ConnectAsync` | boundary: .NET retries `ECONNREFUSED` silently inside
  `Connect(timeout)`; after the fix the host is alive when the loop starts, so the 15 s budget
  is untouched (Out of scope).
- `LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives` | asserts EOF within
  5 s and 0/1/2 -> `/dev/null`; the assertion set already matches D-1's effect; no edit.
- CARD-0490 PC-30 ("remove child stdio redirection to /dev/null") targets that same method; this
  plan's PC-1 (remove the alias scan while keeping `dup2`) is a distinct mutation of a distinct
  line and must not be renumbered into CARD-0490's table.
- Missing setup recorded: no Linux TUnit lane on this host (CARD-0605); the `1.0.40` image is
  present; `docker build` of the fixed image needs network for the cached `grok` install layer
  only if the cache is evicted.

### Delivery inventory

None. No asynchronous delivery path changes: the launcher returns a pid to its caller
synchronously, the host's pipe lifecycle is process-local, and the harness is a foreground
script. Substitutes: none claimed; the harness is a real Linux runner and real host in a
container, not a simulation.

### Proves it works now

- V-1: the launcher returns as soon as the intermediary exits with a pid, with both pipes still
  held open by a grandchild | real shell grandchild on Windows (Linux arm under CARD-0605) |
  `PtyHostLauncherTests.Intermediary_result_does_not_wait_for_a_grandchild_holding_its_pipes` |
  result 4242, elapsed under 5 s, stderr task still incomplete at return.
- V-2: a failed intermediary still reports exit code and stderr | real shell |
  `PtyHostLauncherTests.Intermediary_failure_reports_exit_code_and_stderr` | message contains
  `exit 3` and `boom`.
- V-3: an intermediary that exits without a pid fails fast | real shell |
  `PtyHostLauncherTests.Intermediary_without_a_pid_line_fails_without_a_host` | message contains
  `exit 0`, elapsed under 5 s.
- V-4: a host that exits on launch timeout releases its pipe name | in-process server |
  `HostSessionPipeTests.Launch_timeout_exit_releases_the_pipe_name` | a second
  `NamedPipeServerStream` on the same name constructs; on non-Windows the socket file is gone.
- V-5: a real Linux runner launches a session in seconds, not 45 | Docker harness, fixed image |
  CP-5 `C594 HARNESS fixed` line | `http` is 2xx, `launchMs` under 5000, host log shows
  `Runner connected` before any `Host exiting`, and H-1's `eofMs` under 2000 with the probe host
  still alive at that instant.
- V-6: a timed-out Linux host leaves no socket file | Docker harness, fixed image | CP-5
  `orphanSocket=no` | `/tmp/antiphon-pty-c594probe-*` absent after `Host exiting: launch timeout`.
- V-7 (baseline control, must be red): the same harness against `1.0.40` reproduces the defect |
  Docker harness | CP-4 `C594 HARNESS baseline` with `-Expect baseline` | `http=500`,
  `launchMs` about 45000, `eofMs` about 20000 (equal to the probe's `--launch-timeout-sec`),
  `orphanSocket=yes`. Exit 0 only because `-Expect baseline` inverts the verdict.

### Guards the regression

- R-1: Windows real-host launch, shadow reuse, CARD-0206 linger, CARD-0086 cancel kill |
  `PtyHostLauncherTests` existing four methods | unchanged assertions; the cancel test must stay
  well under the 30 s backstop.
- R-2: served-connection loop, reconnect, launch-timeout self-destruct, custody handshake |
  `HostSessionPipeTests` (all), `HostCustodyTests` (all) | unchanged assertions.
- R-3: the runner's own path through the launcher to a detached Windows host |
  `PtyBackendSeamTests` (all, `tests/Antiphon.SessionRunner.Tests`) | unchanged assertions.
- R-4 (deferred to CARD-0605, not a row here): `LinuxPtyHostLauncherTests` five methods,
  including `Intermediary_pipes_reach_eof_while_host_lives` and `Canceled_launch_leaves_no_owned_host`.

### Guard inventory

- G-1: D-1 alias release keeps the launcher's pipes at EOF while the host lives | PC-1
- G-2: D-2 the launcher does not wait for stream EOF | PC-2
- G-3: D-2 failure diagnostics survive the restructure | PC-3
- G-4: D-3 cancelled accept disposes the listening stream | PC-4
- G-5: CARD-0086 kill-on-cancel survives the restructure | PC-5

guards=5, mapped=5, missing=0, duplicate PC mappings=0.

### Positive controls

Mutation runs these after land, method-scoped, red then restore then green; Code implements
the tests and runs V/R only.

- PC-1: in `PosixProcessSpawner.BecomeSessionLeaderOrExit` delete the `CloseStdioAliases` call
  (keep `dup2`); expect `LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives`
  red at `stdoutAndStderrCompletedWithinFiveSeconds.ShouldBeTrue()` on a Linux lane. Until
  CARD-0605 delivers that lane, the substitute red is the harness `-Expect fixed` run against an
  image built from the mutated tree: `eofMs` about 20000 and exit 1.
- PC-2: in `PtyHostLauncher.AwaitPidAsync` await `reads.Stderr` before returning the pid; expect
  `PtyHostLauncherTests.Intermediary_result_does_not_wait_for_a_grandchild_holding_its_pipes` red
  at the elapsed-under-5-s assertion.
- PC-3: in the failure branch of `AwaitPidAsync` replace the drained stderr with `""`; expect
  `PtyHostLauncherTests.Intermediary_failure_reports_exit_code_and_stderr` red at `boom`.
- PC-4: in `PtyHostServer.RunAsync` delete `await pipe.DisposeAsync()` from the OCE catch; expect
  `HostSessionPipeTests.Launch_timeout_exit_releases_the_pipe_name` red at the second-server
  construction (Windows: `IOException`, all pipe instances busy; Linux: address in use). Known
  caveat: on Windows a garbage collection between `RunTask` completion and the probe can
  finalize the leaked handle and turn this red green; rerun if that happens, and record it.
- PC-5: in `LaunchDetachedAsync` skip `TryKillSpawnedHostAsync` in the catch; expect
  `PtyHostLauncherTests.Cancelled_LaunchDetachedAsync_after_the_intermediary_starts_does_not_leave_a_host`
  red at `leftover.ShouldBeEmpty`.

Filters: `--treenode-filter "/*/*/PtyHostLauncherTests/<method>"`,
`"/*/*/HostSessionPipeTests/Launch_timeout_exit_releases_the_pipe_name"`; restore and rebuild
before each green.

### Docker harness

The script (S4) performs, in order, recording every command's stdout to the evidence
directory; Code may run these by hand only if the script cannot run and must then say so.

1. Optional `-Build`: `docker build -f docker/session-runner-grok/Dockerfile --build-arg SOURCE_REVISION=<HEAD sha> -t <image> .`
   from the checkout root (root `.dockerignore` applies; the image's `grok` install layer is
   source-independent and cached).
2. `docker run -d --name c594-<stamp> --user 1654:1654 -p <port>:8080 -e TMPDIR=/tmp -e SessionRunner__SessionLogPath=/tmp/state/session-runner -e SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts -e Serilog__LogPath=/tmp/state/runner-logs -e PhoneHome__Enabled=false <image>`;
   wait for `GET /health` (60 s cap).
3. **H-2 launch**: `POST /sessions` with
   `{"sessionId":"<new guid>","exe":"/bin/bash","args":[],"env":{},"cwd":"/tmp","cols":120,"rows":30}`;
   record HTTP status and elapsed ms (`launchMs`). Copy the host log
   (`docker exec ... find /tmp/state -name '<sessionId:N>.log'`) and the fd table of the host
   pid named in its `pty-host starting` line (`ls -l /proc/<pid>/fd`); on the fixed image 0/1/2
   are `/dev/null` and no fd above 2 is a `pipe:` that also appears in the runner's own
   `/proc/1/fd` (the runner is pid 1 under `--init`-less `docker run`; use the `dotnet` pid
   otherwise).
4. **H-1 direct timing** (D-1's own red/green, investigation §3.1):
   `docker exec -u 1654:1654 <c> bash -c 'S=$(date +%s%N); /app/Antiphon.PtyHost --spawn --session <guid> --pipe /tmp/antiphon-pty-c594probe-<n> --manifest-dir /tmp/c594probe --launch-timeout-sec 20 2>&1 | cat; E=$(date +%s%N); echo EOF_MS=$(( (E-S)/1000000 ))'`;
   record `eofMs`, and immediately `ls /proc/<printed pid>` to prove the host was alive at EOF.
5. **H-3 orphan socket**: wait for that probe host's `Host exiting: launch timeout` (about
   20 s), then `ls -la /tmp/antiphon-pty-c594probe-*`; `orphanSocket=yes|no`.
6. `docker rm -f c594-<stamp>`; print
   `C594 HARNESS <expect>: image=<tag> http=<code> launchMs=<n> eofMs=<n> hostAliveAtEof=<yes|no> orphanSocket=<yes|no>`
   and `C594 HARNESS EXIT CODE: <n>`. Exit 0 when the observations match `-Expect`
   (`fixed`: 2xx, launchMs under 5000, eofMs under 2000, hostAliveAtEof=yes, orphanSocket=no;
   `baseline`: 500, launchMs over 40000, eofMs over 15000, orphanSocket=yes), 1 otherwise,
   2 on a refused port, missing Docker, failed build or unhealthy container.

### Out of scope

- `LinuxPtyHostLauncherTests` execution (CARD-0605); `ShadowCopyStoreTests.Linux_copy_preserves_execute_mode`
  and `LinuxPhoneHomeRunnerTests.Restart_adopts_same_store_session_and_generation` (same lane).
- The live V-7 Grok turn (`scripts/verify-phone-home-grok.ps1`): a follow-up Code/Review round
  on CARD-0594 after this lands; this plan removes the launch blocker only (investigation §6).
- CARD-0490 PC-12 repoint and the persistence-cut extension (CARD-0490 plan amendments).
- `PtyHostConnectTimeoutSec`, `PtyHostLaunchTimeoutSec`, `AwaitClientAsync`, `PtyHostClient.ConnectAsync`.
- macOS alias release (D-1 keeps `dup2`-only there).
- `docs/docker-stack.md`, the CARD-0590 test image and roster (a lane question, CARD-0605).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S3 | `tests/Antiphon.PtyHost.Tests -> bin-c594/` | launcher | `/*/*/PtyHostLauncherTests/*` | V-1, V-2, V-3, R-1 | all listed, 0 failed (7 executed) | 8 |
| CP-2 | S3 | CP-1 | server-loop | `/*/*/(HostSessionPipeTests*)\|(HostCustodyTests*)/*` | V-4, R-2 | all listed, 0 failed | 6 |
| CP-3 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c594/` | runner-seam | `/*/*/PtyBackendSeamTests/*` | R-3 | all listed, 0 failed | 10 |
| CP-4 | none (before S1) | none | harness-baseline | `pwsh -NoProfile -File scripts/verify-card0594-linux-launch.ps1 -Image antiphon-session-runner-grok:1.0.40 -Expect baseline` | V-7 | `C594 HARNESS EXIT CODE: 0` with the baseline shape | 4 |
| CP-5 | S1-S4 | Docker image from HEAD (`-Build`) | harness-fixed | `pwsh -NoProfile -File scripts/verify-card0594-linux-launch.ps1 -Image antiphon-session-runner-grok:c594-<short sha> -Build -Expect fixed` | V-5, V-6 | `C594 HARNESS EXIT CODE: 0` with the fixed shape | 15 |

CP-4 needs S4 (the script) to exist but no product change, so run it right after S4 is
committed and before, or independently of, S1-S3; its `After` is "the script commit, no fix
commit". If CP-2's combined class filter does not select on the pinned TUnit 1.44, split it
into two rows sharing CP-1's build. Run `PtyHost.Tests` and `SessionRunner.Tests` sequentially
(both spawn hosts). Delete every `bin-c594` directory and every `c594-*` container before
settling.

### Cost

Estimated, not measured; nothing ran in this Plan.

| Ordinary Code floor | Minutes |
|---|---:|
| CP-1 launcher (build PtyHost.Tests into `bin-c594/` + 7 methods) | 8 |
| CP-2 server-loop (no build) | 6 |
| CP-3 runner-seam (build SessionRunner.Tests + class) | 10 |
| CP-4 harness-baseline (image present) | 4 |
| CP-5 harness-fixed (image build about 10 + run) | 15 |
| **Ordinary V/R** | **43** |

Authoring: S1 30, S2 60, S3 20, S4 45, S5 10 = 165. `-ExpectAbout 210`.

Separate post-land Mutation floor: PC-2..PC-5 method-scoped, each red 0.5 + restore/rebuild
0.7 + green 0.5 = 1.7, total 6.8; PC-1 through the harness substitute is two image builds and
runs, about 30; plus 5 snapshot setup = **41.8**, estimated. When CARD-0605's lane exists,
PC-1 drops to about 2.

Handoff audit: bodies read; guards=5, mapped=5, missing=0, duplicate PC mappings=0; every PC
is a compiling defect with an exact method and assertion; numeric Cost above. Next stage: Code.

## Risks

- **Alias identity by `readlink` string.** Exact for pipes and sockets. For a regular file
  target (stdout redirected to a log) two independent opens of the same path also match and
  would both be closed; nothing in the host opens its own stdout path, and the intermediary's
  streams are pipes in every real launch.
- **`FileInfo.LinkTarget` on `/proc` magic links.** Expected to return the raw `pipe:[n]`
  string; if it normalises or throws, use the `readlink(2)` P/Invoke (D-1 names both).
- **A pending stderr read on a failure path.** On Unix the eager `ReadToEndAsync` blocks a
  pool thread until EOF; after D-1 that EOF arrives within milliseconds, and only a failed
  launch against an unfixed host would hold it for the host's lifetime (bounded by the launch
  timeout). The fault observer keeps it out of `UnobservedTaskException`.
- **PC-4 red can be masked by a garbage collection on Windows** (finalized handle). Stated in
  the PC; on Linux the stale socket file makes the red deterministic.
- **Socket unlink is an assumption until H-3.** D-3 names the fallback (explicit delete).
- **The seam test's grandchild outlives the test by up to 12 s** (`ping`/`sleep`). It holds
  only the test's own pipes and exits on its own; the shape is deliberate.
- **Harness needs Docker Desktop up.** A stopped daemon is exit 2, reported, not a product
  failure; use the `docker-desktop` skill before retrying.

## Not done, noted

- **CARD-0605**: execution lane for `LinuxPtyHostLauncherTests` (and the other RequireLinux
  classes). The CARD-0590 `test-runner` image is the obvious substrate; check where
  `libporta_pty.so` lands for a RID-less build before assuming the shadow-copy assertion holds.
- **Provenance of fds 6 and 7.** H-2's fd listing before and after the fix will show whether the
  aliases are the PAL's startup `dup` (present in every .NET process before `Main`) or
  inherited; record the answer in the S1 comment, it does not change the fix.
- **Script contract test** for `verify-card0594-linux-launch.ps1` in the
  `RunCheckpointScriptTests` style (argument refusal, port guard, exit codes) if the script is
  kept beyond this card, for example as CARD-0605's or the nightly's Linux launch probe.
- **Live V-7 re-run** with `scripts/verify-phone-home-grok.ps1` once this lands; the
  investigation warns it does not prove there is no further blocker behind the launch.
- **`LinuxPtyHostLauncherTests.Shadow_host_exchanges_bytes_and_exits` uses `launchTimeout: 60 s`**;
  after this fix that value is harmless, but a 60 s backstop on a test that should complete in
  seconds is worth shortening when CARD-0605 first runs it.
