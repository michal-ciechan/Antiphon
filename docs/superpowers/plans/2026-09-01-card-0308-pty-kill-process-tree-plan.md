# CARD-0308 — `Kill()` must terminate the ConPTY job, not only the top-level process

**Date:** 2026-09-01 (Plan pass, task d8a8a0e8 — design only; no code changed)
**Card:** CARD-0308 "`ModernConPtyConnection.Kill()` doesn't kill the process tree — MCP children can outlive a settled task and lock worktree dirs"
**Diagnosis:** done on the card (Gym Stat CARD-0033, closed). This pass verified it against source and chose the kill shape.

**Sources (verified this pass):** `ModernConPtyConnection.Kill` / `CreateKillOnCloseJob` / `Dispose`, `IPtySession` / `PortaPtySession`, `PtyAgentRunner.KillAsync` / `WindowsJobObject.TryTerminate`, `HostSession.KillAsync`, `SessionRunnerRuntime` HandleExited → Shutdown ack, `SessionRunnerSettings.PtyHostLingerHours = 24`, AppHost `ANTIPHON_PTY_BACKEND=modern`, ADR 0002 teardown order, CARD-0220/0221 worktree + zombie notes, `WorktreeManager.RemoveAsync`.

**Corroborating evidence (this session, not a new repro):** worktree `card-task-2935a868` survived after its git registration went stale (`gitdir file points to non-existent location`). Same class as the card: a leftover directory after `git worktree remove` could not finish because something still held the tree. Do not chase that directory as a live repro; the MCP-child handle is the measured cause.

---

## Decision

`Kill()` must **terminate the kill-on-close Job Object** that spawn already created, then belt-and-braces `Process.Kill(entireProcessTree: true)` on the top-level pid. Do **not** dispose the job in `Kill()` — `Dispose` keeps the documented ConPTY teardown order (pseudoconsole → pipes → process/thread handles → job last).

The reporting session's two options are not equal:

| Option | Verdict |
|---|---|
| `TerminateJobObject` on the spawn job | **Primary.** That job is created *before* `CreateProcess`, has only `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (no breakaway), and every MCP grandchild (`cmd.exe` → `node.exe`) is in it by inheritance. `WindowsJobObject.TryTerminate` already uses this API for the memory-limit job. |
| `Process.Kill(entireProcessTree: true)` alone | **Belt, not the fix.** Toolhelp snapshots miss processes that reparent or race; the job is the set we actually own. Still call it so a process that somehow is not in the job dies too, and so the inbox Porta path (whose job handle we do not own) has a tree kill. |
| Rely on PtyHost `Dispose` / 24 h linger | **Rejected.** That is the gap. `KillAsync` is the settlement/stop path; Dispose is teardown. They must not be the same moment. |

Production is **modern** (AppHost exports `ANTIPHON_PTY_BACKEND=modern`; session-runner appsettings too). The card's file is the live path. Inbox still needs a tree kill because tests and fallback still spawn Porta.

This card does **not** change pool-vs-kill policy (CARD-0221: killed, pooled warm, or owned). A pooled Worktree delegate is *supposed* to keep its cwd until retire; a failed `worktree remove` there is correct. The bug is Kill claiming the tree is gone while MCP children still hold `.worktrees/*`.

---

## Ground truth

### What Kill does today

```
PtyAgentRunner.KillAsync
  → IPtySession.Kill
       ModernConPtyConnection: _process.Kill()          // top-level only
       PortaPtySession:        _connection.Kill()       // Porta's, same shape
  → wait for Exited TCS (top-level exit)
```

Spawn already created a kill-on-close job (`CreateKillOnCloseJob`, flag `0x00002000`) and `AssignProcessToJobObject`. `Dispose` closes that handle last, which is when KILL_ON_JOB_CLOSE fires. `Kill()` never touches `_job`.

`_process.Kill()` can also **throw** `InvalidOperationException` if the process has already exited. `KillAsync` does not catch that.

### Why MCP children survive

Claude/Grok MCP servers are typically `cmd.exe` wrapping `node.exe`, cwd = the session working directory (a worktree for `WorkspaceMode.Worktree`). They are job members. Signalling only `claude.exe` leaves them alive with the directory open. `git worktree remove` then leaves an empty (or half-removed) directory the OS will not delete. Confirmed on Gym Stat CARD-0033: once the real holders exit, the leftover dir deletes in < 200 ms. PtyHost, Search, Defender, OneDrive, Explorer were ruled out.

### The 24 h linger is a second delay, not a second cause

After a successful Kill, `HandleExited` fire-and-forgets `ShutdownHostAsync` (ack so the host deletes its manifest and `Dispose`s). If that ack never arrives, `PtyHostLingerHours` default 24 keeps the host — and the still-open job — until linger expiry. Terminating the job **inside Kill** makes child death independent of that ack. Do not shorten linger on this card (it exists so a runner restart can re-adopt a live host).

### Nested memory job

When `memoryLimitMb > 0`, `PtyAgentRunner` assigns a **second** job (`WindowsJobObject`, also kill-on-close + memory caps) after spawn. On modern Windows that nests. Terminating the **spawn** job is enough for the tree; also terminate the memory job from `KillAsync` (make `TryTerminate` internal) so a nested-job oddity cannot leave a descendant.

---

## Slices

### S1 — `ModernConPtyConnection.Kill` terminates the spawn job

**File:** `src/Antiphon.Agents.Pty/ModernConPtyConnection.cs`

```
public void Kill()
{
    TryTerminateJob();
    try { _process.Kill(entireProcessTree: true); }
    catch (InvalidOperationException) { /* already exited */ }
    catch (Win32Exception) { /* already exited / access */ }
}

private void TryTerminateJob()
{
    if (_job.IsInvalid || _job.IsClosed) return;
    _ = TerminateJobObject(_job, 1);  // P/Invoke next to CreateJobObjectW
}
```

- Do **not** `_job.Dispose()` here. `Dispose` still closes the job last.
- `TerminateJobObject` on an empty/already-killed job is fine; ignore the BOOL.
- Comment why this diverges from Porta: the job existed to survive a crashed host; Kill is the operator/settlement path and must not wait for Dispose.
- ADR 0002 "mirrors Porta step for step" still holds for **spawn**. Kill is the measured exception.

`PtyAgentRunner.KillAsync`: after `_conn.Kill()`, `_jobObject?.TryTerminate()` (promote `WindowsJobObject.TryTerminate` to `internal`). Order: session Kill (spawn job + tree) then memory job.

### S2 — Inbox `PortaPtySession.Kill` also kills the tree

We do not own Porta's job handle. `PortaPtySession.Kill`:

```
try
{
    using var p = Process.GetProcessById(_connection.Pid);
    p.Kill(entireProcessTree: true);
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
{
    try { _connection.Kill(); } catch { /* already gone */ }
}
```

Same already-exited swallow. Production modern does not depend on this; tests on the default inbox backend do.

### S3 — Pin: grandchild holding cwd dies, directory deletes

**File:** `tests/Antiphon.Agents.Pty.Tests/PtyKillProcessTreeTests.cs`

`[NotInParallel("Headed")]` is wrong — this is not headed. `[ParallelLimiter<ProcessSpawnLimit>]`, add the type to `ProcessSpawnLimitTests`'s allowlist. Windows-only; skip otherwise.

Shape (no real Claude, no MCP):

1. Temp directory `held/`.
2. Spawn **modern** ConPTY (`RequireShippedDll` / skip if absent) whose child is `pwsh -NoProfile -Command` that:
   - starts a **grandchild** (`Start-Process ping -ArgumentList '-t','127.0.0.1'` or a tiny sleeper) with `WorkingDirectory = held`;
   - prints `CHILD_PID=<n>`;
   - waits on that process (so the pty child stays alive).
3. Read `CHILD_PID`. Confirm both pids alive and `held` cannot be deleted (or a file opened exclusive by the grandchild).
4. `connection.Kill()` (or `runner.KillAsync` 2 s).
5. Within 2 s: pty pid gone, grandchild pid gone, `Directory.Delete(held, recursive: true)` succeeds.

A control (optional, or a second test with a private hook) that only `_process.Kill()` would leave the grandchild — we do not add a hook; the red is "without S1, step 5 fails on the grandchild / delete". Implementers verify once by commenting out `TryTerminateJob` if they want the red, then restore. Do not leave a "broken Kill" test API.

Drive the same assertion through `PtyAgentRunner("modern").KillAsync` so the memory-job terminate arm is not a dead line.

Inbox arm: one test via `new PtyAgentRunner("inbox")` (or unset) with the same grandchild recipe, so S2 is not untested.

### S4 — Docs

- ADR 0002: Kill is the exception to "mirrors Porta"; terminate the spawn job in `Kill()`, Dispose order unchanged.
- Session-runtime gotcha (next number): a ConPTY `Kill()` that only signals the TUI leaves MCP children holding the worktree; settlement/land `git worktree remove` then leaves a dir whose `.git` gitdir can go stale (`card-task-2935a868`). Kill must terminate the job.
- CARD-0221 three-state rule unchanged.

---

## What this card does not do

- Shorten `PtyHostLingerHours`.
- Change pool-idle worktree lifetime or land's `RemoveQuietlyAsync` warning.
- Herdr pane kill (different transport; `KillPidBestEffort` already uses `entireProcessTree: true`).
- Reaping already-orphaned MCP node processes from past incidents (`reap-zombie-agents.ps1` / manual). Execution brief may kill leftover `node.exe` whose cwd is `.worktrees\*` after deploy.
- Cleaning `card-task-2935a868` as part of the code change.

---

## Test matrix

| Layer | Test |
|---|---|
| Pty, modern | Grandchild ping/sleeper dies on `ModernConPtyConnection.Kill`; temp dir deletes |
| Pty, runner | Same through `PtyAgentRunner("modern").KillAsync` |
| Pty, inbox | Same through inbox runner (S2) |
| Pty | `Kill()` on an already-exited child does not throw |
| Allowlist | `ProcessSpawnLimitTests` includes the new class |

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0308/ -- --treenode-filter "/*/*/PtyKillProcessTreeTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0308/ -- --treenode-filter "/*/*/ProcessSpawnLimitTests/*"
```

Forward slash on OutputPath. Do not co-schedule with `Antiphon.Tests`. Delete `bin-card0308*` after.

---

## Sequencing and risks

**Order: S1+S2 together (one behaviour), S3 with them, S4 docs.** Do not ship Kill without the grandchild test — that is how `_process.Kill()` looked complete.

| Risk | Disposition |
|---|---|
| `TerminateJobObject` vs nested memory job | Terminate spawn job first; also `WindowsJobObject.TryTerminate` from `KillAsync` |
| Grandchild started with `CREATE_BREAKAWAY_FROM_JOB` | Spawn job does not set BREAKAWAY_OK, so they cannot. `entireProcessTree` is the belt |
| `start /b` / `UseShellExecute=true` grandchild not in job | Test must spawn with `UseShellExecute=false` / `Start-Process` without `-UseNewEnvironment` that would break away. Recipe above is `Start-Process ping` as child of pwsh-in-pty |
| Kill throws on already-dead pid, fails `KillAsync` | Caught in S1 |
| Dispose after Kill double-terminates | Harmless; job handle still closed last |
| Pooled worktree still locks dir | By design until retire Kill (which this card makes complete) |
| Fakeclaude tests spawn no grandchildren | Unaffected |

---

## Execution notes

- After deploy, one Worktree delegate that uses an MCP (todoist-mcp is the measured offender) should leave a deletable `.worktrees/card-task-*` within seconds of settle/stop, not after linger.
- Leftover dirs from before the fix are operator cleanup, not a failed fix.
