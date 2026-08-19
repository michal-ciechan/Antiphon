# CARD-0086 — Runner-side launch leak: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0086 (`3c617eaf-ab0f-4862-a902-67196b709e2c`) — a failed runner-side launch leaks its pty-host
**Precedent:** CARD-0056 D1 (`KillAndDisposeAsync` in `AgentSessionService.cs:427`) — a failed launch
must kill what it started. Do not invent a new pattern. Do not port D2/D3/D4 (boot retries,
`RcDegraded`, server reconciliation) — those are server-side and already shipped.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**Same bug class as CARD-0056, one layer down.** `DisposeAsync` is not teardown — on
`RunnerSession` it is documented as detach-not-kill (`SessionRunnerRuntime.cs:918-919`), the
pty-host-split analogue of `RunnerClaudeAdapter.DisposeAsync` being a no-op. The outer
`SessionRunnerRuntime.StartAsync` catch (`:82-87`) only disposes. That is D1's leak, verbatim.

The inner `RunnerSession.StartAsync` catch (`:561-567`) already calls `KillHostBestEffort` for
failures *after* the host pid is assigned. That is the right idea and must stay. It is not
enough: `LaunchDetachedAsync` sits **outside** that try (`:446-461`), so a spawn-then-throw
never assigns `_hostPid`, never enters the inner catch, and the outer catch detaches from a
host it does not even know the pid of. The host is empty (`WaitingForLaunch`) and detached by
design — the 30 s launch-timeout backstop is not the contract, and linger is 24 h once a
Launch has cancelled that timeout.

One Code slice. Port D1. Stop.

## 1. Current shape (verified against the files, 2026-08-19)

Launch chain, one session:

```
SessionRunnerRuntime.StartAsync
  └─ RunnerSession.StartAsync
       ├─ PtyHostLauncher.LaunchDetachedAsync     // intermediary --spawn → detached host pid
       │     host is now alive, WaitingForLaunch, pipe not yet connected
       └─ try
            ConnectAsync → client.LaunchAsync → child ConPTY
            status = Running
          catch
            KillHostBestEffort()                  // Process.Kill(_hostPid), swallow
            throw
  catch
    _sessions.TryRemove
    session.DisposeAsync()                        // DETACH ONLY — must not kill
    throw
```

| Site | What it does on failure | CARD-0056 analogue |
|---|---|---|
| `RunnerSession.StartAsync` inner catch `:561-567` | `KillHostBestEffort` then rethrow | The card-path inner kills that already existed before CARD-0056 |
| `SessionRunnerRuntime.StartAsync` outer catch `:82-87` | `DisposeAsync` only | `LaunchInteractiveProcessAsync` / `StartAsync` outer catch **before** D1 |
| `RunnerSession.DisposeAsync` `:915-931` | Drop the pipe. Comment: "must NOT kill" | `RunnerClaudeAdapter.DisposeAsync => ValueTask.CompletedTask` |
| `PtyHostLauncher.LaunchDetachedAsync` `:66-77` | Intermediary `Process.Start` then `ReadToEndAsync(ct)` / `WaitForExitAsync(ct)`. On cancel or unparseable stdout it throws with the host already detached. No kill. | No server analogue — this is the layer that *has* the pid |
| `AdoptAsync` failure `:278-285` | `DisposeAsync` then `KillPidBestEffort(manifest.HostPid)` | Already D1-shaped. Leave it. |
| `EnsureExitedHostGoneAsync` (CARD-0050 S1) | Ack, wait, then `KillHostIfStillOurs` (name-checked) | Relaunch of an **exited** session. Not this card. Do not reopen. |

`KillHostBestEffort` (`:1094-1104`) is weaker than the verified kill CARD-0050 already wrote
(`KillHostIfStillOurs`, `:1062-1074`): no `PtyHost` process-name check (pid-reuse risk), no
`using`, no wait-until-gone. Reuse the verified helper; do not add a third kill.

The 30 s host self-destruct (`HostSession.StartLaunchTimeout`, `PtyHostLaunchTimeoutSec = 30`)
is a backstop for "runner died mid-start", not a substitute for D1. A host that has accepted
Launch cancels that timer (`HostSession.cs:93`) and then lives until linger (`PtyHostLingerHours
= 24`) or a kill. The card's 24-hour `WaitingForLaunch` find is the linger TTL, not a reason to
change either timer.

## 2. The fix — CARD-0056 D1, three call sites, one helper

Do not change `DisposeAsync` to kill. Detach-on-dispose is the pty-host split. Kill belongs
only on **failed-launch** paths, with `CancellationToken.None`, kill-failure swallowed so it
never replaces the launch exception. Double-kill is harmless.

### 2.1 `RunnerSession`: one teardown helper, wrap the whole start

Add `TearDownFailedLaunch` (sync is fine — both existing kills are sync) that:

1. `KillHostIfStillOurs()` when `_hostPid > 0` (reuse, do not clone `KillHostBestEffort`).
2. Does not throw.

Move `LaunchDetachedAsync` **inside** the existing try so every failure after a pid is assigned
hits the catch. Replace `KillHostBestEffort()` in that catch with `TearDownFailedLaunch()`.
Delete `KillHostBestEffort` if nothing else calls it.

### 2.2 `SessionRunnerRuntime.StartAsync` outer catch — kill then dispose

```
catch
{
    _sessions.TryRemove(request.SessionId, out _);
    session.TearDownFailedLaunch();   // kill what StartAsync started
    await session.DisposeAsync();     // then detach; Dispose still must not kill
    throw;
}
```

This is `KillAndDisposeAsync` (`AgentSessionService.cs:427-441`) copied down one layer. The
inner catch already killed: a second call is a no-op on a dead pid. The outer catch is what
closes the spawn-then-throw window if the inner catch is ever skipped.

Do **not** kill on the "already running" branch (`:70-74`) — that session never spawned a host.

### 2.3 `PtyHostLauncher.LaunchDetachedAsync` — kill at the layer that has the pid

If the intermediary has started and this method is about to throw, drain stdout/stderr and
`WaitForExit` on **`CancellationToken.None`** (the caller's token may already be cancelled —
same posture as CARD-0056), parse the host pid if present, and `Process.Kill(entireProcessTree:
true)` it before rethrowing. A kill failure is swallowed.

This is the case `_hostPid` is never assigned: cancel during `ReadToEndAsync(ct)`, or
unparseable stdout after a successful spawn. Without this, 2.1/2.2 have nothing to kill.

Do not pass the caller's cancelled token into the drain. An already-cancelled `ct` today throws
before stdout is read, which is how the pid is lost.

## 3. Tests — pin the process is gone, not a lifecycle list

CARD-0056's tests (`AgentSessionLaunchFailureTests`) assert `["Kill", "Dispose"]` on a fake
adapter. The runner leak is a **detached OS process**; a fake lifecycle list cannot see it.
Assert the `Antiphon.PtyHost` pid is gone (the `PtyHostLauncherTests.WaitForProcessExitAsync`
helper is the existing shape).

`[NotInParallel("Pty")]` + `[ParallelLimiter<ProcessSpawnLimit>]` on anything that starts a
host. Alternate `OutputPath=bin-card0086/` (forward slash). Do not co-schedule
`Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`.

| Test | Where | What it pins | Red today? |
|---|---|---|---|
| `LaunchDetachedAsync` cancelled after the intermediary starts does not leave a host | `tests/Antiphon.PtyHost.Tests/PtyHostLauncherTests.cs` | §2.3. Cancel the token once `Process.Start` has run (already-cancelled token is enough: `Start` is not ct-gated, `ReadToEndAsync(ct)` is). Assert no live pid / `GetProcessById` throws. | **Yes** — pid is never read, nothing kills |
| `SessionRunnerRuntime.StartAsync` that fails after the host exists leaves no `Antiphon.PtyHost` | `tests/Antiphon.Tests/Agents/SessionRunnerRuntimeTests.cs` (existing file, same attributes) | §2.1/2.2. Force the failure with a cancelled token **or** an exe `PtyAgentRunner.StartAsync` will reject (so `client.LaunchAsync` throws `Host launch failed`). Poll `Process.GetProcessesByName("Antiphon.PtyHost")` scoped to this test's pid, or record `HostPid` from a raced `List()` if the throw is after connect. Bound the wait to a few seconds — **do not wait out the 30 s launch timeout**; that passing would mean the backstop hid a missed kill. | Cancel-during-spawn: **yes**. Bad-exe after connect: **no** (inner catch already kills) — still add it; that is the CARD-0056 pin, and it is untested today |

Cleanup in `finally` must `TryKill` any leftover pid so a red run does not leak the thing the
test is about.

## 4. Out of scope

- `DisposeAsync` becoming a kill. That would destroy session survival across runner restarts.
- CARD-0056 D2/D3/D4, `SessionReconciliationService`, server `AgentSessionService`.
- CARD-0050 `EnsureExitedHostGoneAsync` / the pipe-name relaunch race (shipped `3f792ec`).
- Changing `PtyHostLaunchTimeoutSec` or `PtyHostLingerHours`.
- Making `NamedPipeServerStream` construction in `PtyHostServer` catch `IOException` (a new
  host that cannot bind a taken pipe name currently crashes the process — not this card; a
  crash is not a leak).
- Closing or moving CARD-0086. This plan lands; a Code slice implements.

## 5. Slice

One Code slice, in this order: tests first (the cancel-after-spawn launcher test is the red
that proves the gap), then §2.3, then §2.1, then §2.2. Verify:

```
dotnet run --project tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-card0086/ -- --treenode-filter "/*/*/PtyHostLauncherTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0086/ -- --treenode-filter "/*/*/SessionRunnerRuntimeTests/*"
```

Delete the `bin-card0086` directories afterwards (`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0086`).
The two projects run **one after the other**, not concurrently.
