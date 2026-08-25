# CARD-0206 — `Antiphon.SessionRunner.Tests` leaks its own pty-hosts: root cause and fix plan

**Date:** 2026-08-25 · **Card:** CARD-0206 (`0cd3aebc-037c-441b-9856-dd2ab54350fc`) · **Status:**
investigated, root cause established, fix designed — **no implementation code written** (plan
only, as briefed) · **Verified against:** `master` @ `11789d6`, worktree `card-task-d9b02a4c`.
Every count below was measured on this machine on 2026-08-25.

---

## Verdict up front

**Two leak shapes, not one, and the immortal one is a race inside the pty-host that a single test
fixture is uniquely good at triggering.**

| | |
|---|---|
| **Shape A — immortal host** (the card's finding: a `SessionCpuWatchdogTests` host alive hours later, no manifest) | `HostSession.ObserveExitAsync` (`src/Antiphon.PtyHost/HostSession.cs:264-314`) observes the child's exit, then **saves the exit-stamped manifest (`SaveAtomic`) BEFORE arming the linger timer**, with no `try/catch`, from a fire-and-forget task (`_ = ObserveExitAsync()`, `:146`). `WatchdogFixture.DisposeAsync` (`tests/Antiphon.SessionRunner.Tests/SessionCpuWatchdogTests.cs:205-222`) kills the **child** externally, detaches the runtime, and immediately `Directory.Delete(logRoot, recursive: true)`s the very directory the host is writing that manifest into. When the deleter wins the interleaving, `SaveAtomic` throws (`CreateDirectory` on a delete-pending parent, `WriteAllText` into a vanished directory, or `File.Move` of a `.tmp` the deleter just removed), the unobserved-task fault is swallowed, **the linger is never armed**, `HostSession` is never disposed (so the pseudoconsole and its `conhost.exe` stay open), and the host lives until reboot. The linger TTL these fixtures set (`PtyHostLingerHours = 0.02` = 72 s) never applies. |
| **Shape B — 72-second lingerer** (the "1-2 per run" the card counted) | Every host-spawning fixture in the assembly ends a session with `runtime.KillAsync(...)` then `runtime.DisposeAsync()` back to back. `RunnerSession.KillAsync` returns as soon as `_exited` completes (`SessionRunnerRuntime.cs:1825-1827`); the Shutdown ack that makes the host delete its manifest and exit is sent from `HandleExited` via `_ = Task.Run(ShutdownHostAsync)` (`:1966-1968`). `DisposeAsync` nulls `_client` (`:1900-1911`), so when it runs first `ShutdownHostAsync` returns without acking (`:2062-2065`) and the host sits out its full linger (72 s here; **24 h** for any fixture that forgets to set `PtyHostLingerHours`). Measured: 1–2 such hosts per run of the watchdog class, all gone by +100 s. |
| **Fix** | Three slices (§3): **S1** the requested `[After(Assembly)]` sweep — positive identity by executable path under this assembly's own temp roots, kills both shapes from this run *and* from earlier runs, then deletes the roots the locked DLLs kept alive; **S2** fix the one fixture that creates Shape A (kill through the runtime, wait for the host to exit, *then* delete) and share that teardown with the other six fixtures so Shape B stops too; **S3** harden `HostSession.ObserveExitAsync` so a failed manifest save can never disarm the linger (product change, pinned by a deterministic `Antiphon.PtyHost.Tests` case). |
| **Reproduction rate** | 7 runs of the class (21 sessions, `bin-c206/`): **0** immortal hosts, 1–2 lingerers per run, all exited by +100 s. The immortal one on this machine (pid 8688) came from an 18:04 run — most likely a full-assembly run with wider timing jitter. Shape A is a real but narrow race; Shape B is routine. Both are closed by S1 regardless of rate. |

---

## 1. What was measured

### 1.1 Live census (23:29 local)

15 `Antiphon.PtyHost.exe` processes. 13 run from `C:\logs\antiphon\session-runner\pty-hosts\bin\…`
(the production runner — out of scope). Two run from `%TEMP%`:

| pid | started | exe root | `--linger-hours` | children | verdict |
|---|---|---|---|---|---|
| 8688 | 18:04:10 | `%TEMP%\antiphon-cpu-watchdog-tests-04f82b94…` | 0.02 | `conhost.exe` only (created 18:04:14) | **Shape A** — 5.5 h alive on a 72 s linger, child long gone, 0.69 s CPU total |
| 59512 | 23:26:55 | `%TEMP%\antiphon-fake-pty-59a0a971…` | 0.02 | `conhost.exe` only | **not this card** — an `Antiphon.Tests` host from worktree `card-CARD-0205` (its log names `bin-card205\fakeclaude`); child killed 22:27:25Z by request, host exited 22:28:37Z on its TTL exactly as designed |

### 1.2 The immortal host's surviving temp root — the fingerprint

`%TEMP%\antiphon-cpu-watchdog-tests-04f82b947add4ccf91daaf710c298610\` now contains, and only
contains:

```
pty-hosts\                         created 18:04:09  last-write 18:04:15
pty-hosts\bin\20260825-170409-…\   the shadow-copied host binaries — locked by pid 8688, undeletable
pty-hosts\manifests\               created 18:04:14  last-write 18:04:15  EMPTY
```

Gone: `pty-hosts\logs\` (and the host log in it), `claude-config\`, `agent-cwd\`, the `.ansi.log`,
the manifest `.json`. The fixture's recursive delete ran to completion on everything it could
delete — *except* the `manifests` directory, which it emptied but could not remove. .NET's
recursive delete removes a directory only after its children are gone; a directory left standing
empty, with a last-write one second after its creation (18:04:14 is the launch-time manifest save,
18:04:15 is the teardown), means **a file was being created inside it while the deleter walked it**.
The only writer of that directory is the host's exit-manifest `SaveAtomic` (`.tmp` create → `Move`).
So the child's exit **was** observed and the save **was** attempted, in the same instant the fixture
deleted the tree. Had the save succeeded the linger would have been armed and the host gone 72 s
later, or the `.json` would still be there (the linger path never deletes it); had the exit not been
observed at all there would have been nothing writing into `manifests` and it would have been
removed like `logs`. Only "save attempted and failed" leaves this exact residue *and* an immortal
host.

The host log that would have said this outright was in `pty-hosts\logs\` — deleted by the same
teardown. The `_log.Info("Child exited …")` line (`HostSession.cs:303`) comes **after** the save, so
even a surviving log would have been silent about the failure; the exception goes to a discarded
task and nowhere else.

### 1.3 Exit detection is not the problem (ruled out)

Porta.Pty's Windows connection raises `ProcessExited` from a `Process.WaitForExit` thread (verified
by byte-searching `porta.pty/1.0.7/lib/netstandard2.0/Porta.Pty.dll`: `WaitForExit` and
`ProcessExited` present, no pipe-EOF or `WaitForSingleObject` mechanism). An external
`TerminateProcess` on `cmd.exe` therefore reaches `PtyAgentRunner._exitTcs`
(`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:121-128`) and `ObserveExitAsync` runs. The modern
backend (`ModernConPtyConnection.cs:133`, `Process.Exited`) behaves the same. The leak is downstream
of observation, not a missed observation.

### 1.4 Reproduction runs

```
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c206/ -- \
  --treenode-filter "/*/*/SessionCpuWatchdogTests/*"
```

| run | tests | hosts under `antiphon-cpu-watchdog-tests-*` at exit (excl. 8688) | at +100 s |
|---|---|---|---|
| 1 (with build, 48 s) | 3/3 pass | 1 (pid 55428, child gone, `conhost.exe` only) | 0 |
| 2–7 (`--no-build`, ~10 s each) | 18/18 pass | 0, 0, 1, 2, 2, 2 (cumulative, each lingering) | 0 |

Zero immortal hosts in 21 sessions; the Shape B lingerer appears on most runs. The race window for
Shape A is the few milliseconds between `Process.Kill` returning and the deleter reaching
`pty-hosts\manifests` (it deletes `agent-cwd`, `claude-config`, then `pty-hosts\bin` — where every
DLL fails with a sharing violation that .NET records and *continues past* — then `logs`, then
`manifests`), against the host's `WaitForExit` wake-up plus one file write. Under a full-assembly
run, with other classes' process spawns and the `[NotInParallel("ClaudeConfigDirEnv")]` lane's
neighbours competing for the disk, that window is wider than in an isolated class run.

---

## 2. Root cause, in code

### 2.1 When a pty-host exits — and when it does not

A host exits on exactly four events (`HostSession.cs`): the runner's **Shutdown ack** (`:236-240`,
deletes the manifest), a **launch timeout** with no Launch received (`:69-73`), a **launch failure**
(`:107-112`), or the **linger TTL** elapsing after the child's exit (`:309-314`). It does **not**
exit when the runner's pipe drops — surviving the runner is the entire point of the pty-host split
(`SessionRunnerRuntime.cs:751-760`, `:1900-1911`). A session that is detached from and never
acked therefore lives until its child exits *and* the linger fires.

### 2.2 The ordering defect in `ObserveExitAsync`

```
HostSession.cs
 264  private async Task ObserveExitAsync()
 270      exitCode = await _runner.Exited;                 // the child is gone
 283      _status = PtyHostStatus.Exited;
 291      _manifest = _manifest with { ExitCode, ExitReason, ExitedAtUtc };
 298      _manifest.SaveAtomic(_options.ManifestPath);     // CreateDirectory + WriteAllText(.tmp) + Move — UNGUARDED
 303      _log.Info("Child exited …; lingering for runner ack");
 306      _sink?.TryWrite(new ExitedMessage(…));
 310      _ = Task.Run(async () => { await Task.Delay(_options.LingerTtl); RequestExit("linger TTL expired"); });   // armed LAST
```

Invoked as `_ = ObserveExitAsync()` (`:146`). Anything thrown at `:298` faults a task nobody awaits:
no log line, no `ExitedMessage`, no linger, no `DisposeAsync` of the `PtyAgentRunner` (so the
pseudoconsole handle is never closed — which is why the leaked host's only child is a `conhost.exe`).
`PtyHostServer.RunAsync` (`PtyHostServer.cs:20-64`) keeps serving the pipe forever, waiting for an
`ExitRequested` that can no longer come.

### 2.3 The fixture that hits it

`SessionCpuWatchdogTests.WatchdogFixture.DisposeAsync` (`:205-222`):

```
Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
Process.GetProcessById(childPid).Kill(entireProcessTree: true);   // cmd.exe — the CHILD, not the host; returns before the exit lands
await runtime.DisposeAsync();                                      // detach-not-kill, by design
try { Directory.Delete(logRoot, recursive: true); } catch { }      // races the host's exit-manifest save
```

`childPid` is `dto.Pid` (`RunnerSessionDto.Pid`, the ConPTY child; `HostPid` is a separate field —
`SessionRunnerContracts.cs:80,87`). Two of the three tests (`Working_session_is_never_killed…`,
`Session_within_the_min_uptime_grace_is_left_alone`) reach this path with the session still Running,
so the host is on the exit path *because of* this kill, at the same instant as the delete. The third
(`Idle_session_burning_a_core_is_killed…`) kills through the watchdog → `runtime.KillAsync`, and
suffers only Shape B (the lost ack, §2.4). The `catch` at `:166-172` (launch or ingestion assertion
failed before the fixture existed) disposes the runtime without killing anything at all — a host
with a live `cmd.exe` and no TTL running, the CARD-0204 never-exits shape, though it requires a
failing test to reach.

This is the **only** fixture in the assembly that (a) never kills through the runtime and (b) deletes
its log root while a host may still be on its exit path. Every other spawning fixture goes through
`runtime.KillAsync` first, which makes the host kill its own child, observe the exit, save the
manifest (with nothing deleting the directory), and receive — usually — the Shutdown ack:

| fixture | teardown | shape |
|---|---|---|
| `FirstWriteRaceTests` `:73-83` | `KillAsync` → `DisposeAsync` → `Directory.Delete` | B |
| `PtyBackendSeamTests` `:160-173` | `KillAsync` → `DisposeAsync` → tree-kill child | B |
| `SessionBufferBoundsTests` `:69-70`, `:108-109` | `KillAsync` → `DisposeAsync` → tree-kill child | B |
| `PtyHostAdoptionTests` `:101-106`, `:238-247` | `KillAsync`/`KillAllAsync` → (one test waits for host exit, 15 s) → `DisposeAsync` | B, except the one that waits |
| `TranscriptAdoptionSafetyTests` `:1848-1860` | `KillAsync` → tree-kill child **and host** → `Directory.Delete` | none (host killed explicitly) |
| `SessionLivenessTests` `:56-58` etc. | `SweepVanishedSessions` (sends the ack) or tree-kill child; runtime never disposed, root never deleted | B at worst |
| `SessionCpuWatchdogTests` `:205-222` | tree-kill child → `DisposeAsync` → `Directory.Delete` | **A** and B |

### 2.4 Shape B — the ack that loses to `DisposeAsync`

`RunnerSession.KillAsync` (`SessionRunnerRuntime.cs:1798-1828`) awaits `_exited`, which
`HandleExited` completes at `:1961` — *before* it schedules the ack at `:1968`
(`_ = Task.Run(ShutdownHostAsync)`, deliberately off the read loop). A test that calls
`runtime.DisposeAsync()` on the next line nulls `_client` (`:1904`); `ShutdownHostAsync` then takes
the `client is null → return` arm (`:2062-2065`) and the host, which has already saved its
exit manifest and armed its linger, waits out the TTL. Harmless in production (the daemon does not
dispose its runtime after every kill; the 24 h TTL covers a runner restart), routine in tests. The
manifest survives on disk in the temp root the fixture could not fully delete.

The runtime already has the right primitive — `RunnerSession.EnsureExitedHostGoneAsync`
(`:1996-2035`: ack, bounded wait, verified kill) — but it is `internal` on the session, reachable
only via relaunch. Tests cannot call it; the fixture-side fix in S2 does the same three steps
through the public surface (`KillAsync`, then poll `HostPid`).

---

## 3. The fix — three slices, land in this order

Each slice is independently testable and independently shippable. S1 alone satisfies the card's
ask; S2 removes the cause; S3 makes the product survive the next fixture that gets this wrong.

### S1 — `[After(Assembly)]` sweep in `tests/Antiphon.SessionRunner.Tests` (the card's ask)

**New file `PtyHostLeakSweep.cs`**, same shape as the assembly's existing `PtyBackendEnvGuard`
(`[Before(Assembly)]`, public static method on a public class) and `Antiphon.Tests`'
`TestDbFixture.DisposeAsync` (`[After(Assembly)]`). Two pieces:

**(a) `TestSessionLogRoot` — the registry.** A static helper every spawning fixture uses instead of
its own `Path.Combine(Path.GetTempPath(), $"antiphon-<x>-{Guid.NewGuid():N}")`:

- `TestSessionLogRoot.Create(string prefix)` → creates `%TEMP%\antiphon-<prefix>-<guid:N>`, adds it
  to a `ConcurrentDictionary<string, byte>` (fixtures in different classes run concurrently —
  this assembly has **no** `ProcessSpawnLimit`, unlike the other three pty assemblies), returns
  the path.
- `TestSessionLogRoot.KnownPrefixes` — the assembly's fixed list, one entry per fixture:
  `cpu-watchdog-tests`, `liveness-tests`, `adoption-tests`, `bufbounds-tests`, `backend-seam`,
  `first-write-race`, `0180-dto` (the seven call sites: `SessionCpuWatchdogTests.cs:108`,
  `SessionLivenessTests.cs:139`, `PtyHostAdoptionTests.cs:263`, `SessionBufferBoundsTests.cs:121`,
  `PtyBackendSeamTests.cs:127`, `FirstWriteRaceTests.cs:24`, `TranscriptAdoptionSafetyTests.cs:1260,1774`
  — the herdr fixtures spawn no pty-host and stay as they are). **Not** `fake-pty` (that is
  `Antiphon.Tests`'), not `session-runner-relaunch`/`contract`/`runtime-runner-tests` (other
  assemblies, other shapes).

**(b) The sweep.** `[After(Assembly)] public static async Task SweepLeakedPtyHostsAsync()`:

1. Enumerate `Process.GetProcessesByName("Antiphon.PtyHost")`; for each, read
   `MainModule?.FileName` (the idiom `PtyHostLauncherTests.HostPidsUnder` and
   `SessionRunnerRuntimeTests` already use; swallow access-denied/exited-mid-scan). A host runs
   from `<root>\pty-hosts\bin\<stamp>\Antiphon.PtyHost.exe` (`PtyHostLauncher.cs:53`,
   `SessionRunnerSettings.PtyHostBinDir`), so the executable path *is* the log root — no manifest
   needed, which matters because the manifest is exactly what the leaking fixture deleted. If
   `MainModule` is unreadable, fall back to WMI `Win32_Process.CommandLine` and take
   `--manifest-dir`; both name the root.
2. **Positive identity, two arms, nothing else ever matches:**
   - *this run:* path starts with a root in the registry;
   - *earlier runs:* path matches `^<%TEMP%>\antiphon-(<KnownPrefixes>)-[0-9a-f]{32}\\` **and**
     the host's `StartTime` is earlier than this test process's `StartTime` **and** (it has no
     live child other than `conhost.exe`/`OpenConsole.exe` **or** it is older than 30 min). The
     child rule is what keeps a *concurrent* run of this same assembly in another worktree safe:
     its hosts have a live `cmd.exe` and are seconds old. Production hosts (`C:\logs\antiphon\…`),
     `Antiphon.Tests`' `antiphon-fake-pty-*`, `Antiphon.PtyHost.Tests`' roots — none can match a
     prefix from this list, so age is never the deciding evidence (CARD-0203's rule).
3. For each match: `Process.Kill(entireProcessTree: true)` on the **host** (takes `conhost`/
   `OpenConsole` and any `cmd.exe` with it), `WaitForExit(10 s)`, record pid + root + whether the
   child was still alive (that distinguishes a Shape A/B lingerer from a CARD-0204-style live
   session in the output).
4. Delete every root that had a match, and every registered root, best effort — the kill is what
   unlocks the `bin\` DLLs the fixtures could never remove; today those directories accumulate
   invisibly under `%TEMP%` (this machine has them from 2026-08-07 onward, §1.1's listing).
5. `Console.WriteLine` one line per host and a summary:
   `[CARD-0206] swept N pty-host(s) this assembly left behind (this run: a, earlier runs: b): pid … root …`
   and `[CARD-0206] no leaked pty-hosts` when zero. **Do not throw**: a failing assembly hook would
   stand in for the real results and the sweep exists precisely for the case the fixtures got
   wrong. The number for *this run* is the metric S2 drives to zero; reviewers should read it in
   the run output.

TUnit runs `[After(Assembly)]` after the last test of the assembly in this process, including
filtered runs (`--treenode-filter`), so the sweep covers a single-class run too. It must be
`public static`, on a `public` class, in this assembly (hooks are assembly-scoped — the same reason
`PtyBackendEnvGuard` is duplicated per assembly rather than shared).

**Pin:** a test `PtyHostLeakSweepTests.A_host_under_a_registered_root_is_identified_and_a_production_path_is_not`
that drives the matcher (pure function over `(exePath, startTime, children)`) — no process needed.
Whether the sweep actually kills is verified operationally (§4), not by a test that would have to
leak on purpose.

### S2 — stop creating the leak: fix `WatchdogFixture.DisposeAsync`, then share the teardown

**`SessionCpuWatchdogTests.WatchdogFixture.DisposeAsync`**, new order:

1. `Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null)` (unchanged);
2. `await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None)` inside
   `try/catch` — the host kills its own child, observes the exit, saves the manifest *in a directory
   nothing is deleting*, and the runner gets `Exited`;
3. **wait for the host to exit** — poll `Process.GetProcessById(dto.HostPid)` until gone or 15 s,
   the pattern `PtyHostAdoptionTests.KillAll_kills_every_live_session_and_their_hosts` (`:242`)
   already uses. This is what gives the `Task.Run(ShutdownHostAsync)` ack time to land; a host that
   is still there at 15 s gets a tree-kill (it protects nothing once its child is gone);
4. `await runtime.DisposeAsync()`;
5. child-pid tree-kill as the existing fallback (already dead on the happy path);
6. `Directory.Delete(logRoot, recursive: true)` — now nothing is writing into it.

The fixture needs `dto.HostPid` alongside `dto.Pid` (both on the `RunnerSessionDto` it already
holds, `SessionCpuWatchdogTests.cs:186`). The `catch` at `:166-172` should do steps 2-4 as well
(`KillAllAsync` is simplest there) so a failed launch cannot leave a live `cmd.exe` host.

**Then extract it**: `TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId, hostPid)` in
the same helper file as S1's registry, and use it at the six Shape B sites in §2.3's table. That
turns each fixture's 72-second lingerer into an acked exit with the manifest deleted, so S1's
"this run" count is expected to be **0** in a green run rather than "a few". Keep the existing
child-pid `KillBestEffort` fallbacks; they are correct as backstops.

**Pin:** the existing three watchdog tests, re-run in a loop (§4) with S1's output read — `this run:
0` across ≥10 iterations. No new assertion in the tests themselves: a fixture that asserts on
process counts is the flake shape `PtyStressTests` had to tolerate with a `< 5` margin.

### S3 — product hardening: a failed manifest save must not disarm the linger

`HostSession.ObserveExitAsync` (`HostSession.cs:264-314`): arm the linger **before** touching the
filesystem, and guard the save:

```
_status = Exited …                                  // as today
_ = Task.Run(async () => { await Task.Delay(_options.LingerTtl); RequestExit("linger TTL expired without runner ack"); });
try { _manifest.SaveAtomic(_options.ManifestPath); }
catch (Exception ex) { _log.Error("exit manifest save failed; lingering anyway", ex); }
_log.Info("Child exited …");                        // as today
_sink?.TryWrite(new ExitedMessage(…));               // as today — the runner still hears the exit
```

Semantics are unchanged on the happy path (the timer starts a few ms earlier). On the failure path
the host now exits after its TTL and the runner's adoption sweep already treats a missing/stale
manifest correctly (`AdoptOrphanedHostsAsync`, `SessionRunnerRuntime.cs:409-458`: dead host pid ⇒
`ProcessVanished`). `ExitedMessage` should also go out regardless of the save, for the same reason.
Consider the same guard on the launch-time save at `:140` — today a throw there propagates into
`PtyHostServer`'s message loop as a launch error, which is at least loud; leave it unless a test
shows otherwise.

**Pin, in `tests/Antiphon.PtyHost.Tests`** (which already spawns real hosts and has the
`ProcessSpawnLimit` lane): launch a host with `--linger-hours 0.001` (3.6 s) into a fresh temp root,
connect and Launch `cmd.exe /k`, then **replace `pty-hosts\manifests` with a FILE of that name**
(so `Directory.CreateDirectory` throws deterministically — the race, made certain), tree-kill the
child externally, and assert the host process is gone within ~10 s. Red today (the host lives
forever), green with S3. Name it for the card:
`A_host_whose_exit_manifest_cannot_be_saved_still_exits_on_its_linger_TTL`.

---

## 4. Verification

Build to an alternate path — the always-on daemons hold `bin/`; forward slash, never backslash:

```
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c206/
dotnet build tests/Antiphon.PtyHost.Tests        --property:OutputPath=bin-c206/
```

1. **Before any change**, census: `Get-CimInstance Win32_Process -Filter "Name='Antiphon.PtyHost.exe'"`
   filtered on `CommandLine -like '*\Temp\antiphon-*'`. Note pids (pid 8688 is the standing
   Shape A specimen; killing it is the operator's call — it is also the evidence).
2. **S1 landed:** run the watchdog class once; the sweep line must name pid 8688 under *earlier runs*
   and kill it, and its root `antiphon-cpu-watchdog-tests-04f82b94…` must be gone afterwards. Then
   the full assembly once (it is small — the Herdr classes use a fake server):
   `dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c206/`.
3. **S2 landed:** 10 iterations of the watchdog class with `--no-build`; read the `[CARD-0206]`
   summary each time — *this run* must be 0 on every iteration, and no `antiphon-cpu-watchdog-tests-*`
   host may exist 5 s after the process exits (no sweep needed to reach zero). Then the same for the
   full assembly, twice.
4. **S3 landed:** `dotnet run --project tests/Antiphon.PtyHost.Tests --no-build --property:OutputPath=bin-c206/ -- --treenode-filter "/*/*/*/A_host_whose_exit_manifest*"`
   — confirm it is **red without** the `HostSession` change (temporarily revert) and green with it.
   Then the whole `Antiphon.PtyHost.Tests` assembly, and `Antiphon.Agents.Pty.Tests` **after** it,
   never concurrently (the two cannot cap each other's spawns).
5. Delete every `bin-c206` directory (one per project in the graph, ~12):
   `Get-ChildItem . -Recurse -Depth 2 -Directory -Filter bin-c206 | Remove-Item -Recurse -Force`.

Do not widen any timeout to make a step pass: a host that has not exited 15 s after an acked kill
is a defect to name, not a budget to raise.

---

## 5. Out of scope, recorded

- **`Antiphon.Tests`' `antiphon-fake-pty-*` lingerers** (§1.1, pid 59512): same Shape B ack race,
  different assembly, exits on its own TTL. Its fixtures could take the same
  `KillAndAwaitHostExitAsync` if the 72 s ever matters; not part of this card.
- **The 24 h default.** Every spawning fixture in this assembly sets `PtyHostLingerHours = 0.02`
  today (11 sites, grep-verified). A future fixture that forgets inherits 24 h; S1's registry helper
  is the natural place to hand out a `SessionRunnerSettings` with the TTL pre-set, but that is a
  refactor beyond the ask.
- **The reaper's proposed command-line arm** (CARD-0204 plan §6.2): unnecessary once S1 lands — the
  assembly cleans up after itself, including earlier runs.
- **No `ProcessSpawnLimit` in this assembly** (the other three pty assemblies have one, CARD-0050
  S5). Not the cause here (the race is inside one fixture's own teardown), but it is why a
  full-assembly run has more timing jitter than a class run; worth its own small card if spawn
  flakes appear.
- **`%TEMP%` litter from other assemblies** (`antiphon-review-*`, `antiphon-contract-*`,
  `antiphon-session-runner-relaunch-*`, back to 2026-08-07): not pty-host leaks, just undeleted
  scratch; the Windmill weekly cleanup (`reference_windmill_cleanup_schedule`) is the place for
  it.
