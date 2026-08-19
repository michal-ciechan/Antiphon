# CARD-0026 — `JobObject_kills_session_when_memory_limit_exceeded` standing red: root cause and fix

**Status: fix verified and shipped alongside this plan** (one-line test change; see §4).
Plan task 94d55e58, 2026-08-19.

## 1. Root cause (proven, not guessed)

**The test's memory hog escapes the job object because it is an MSIX-packaged binary. The
production feature works.**

The test drives `cmd.exe` (assigned to the job at spawn) → a batch file → **`pwsh.exe`**, which
allocates 16 MB buffers in a loop until the job's memory kill fires. On this machine — and any
machine where PowerShell 7 is winget/Store-installed — `pwsh.exe` on PATH resolves through the
WindowsApps app-execution alias to the MSIX package
(`C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.4.0_x64__…\pwsh.exe`). On Windows 10, a
process with package identity is **not placed into the caller's job object**: the AppModel puts it
into its own per-package job instead, silently, even though our job does not allow breakaway.

Evidence, all measured 2026-08-19 on the red checkout:

1. **Reproduced red in isolation** (`--treenode-filter`, 46.7 s, `Task.Delay(45s)` wins the
   `WhenAny`) — same shape as the 2026-08-10 and 2026-08-18 reports. Not flaky: red on every run
   before the fix, green on every run after.
2. **The hog is unconstrained by the job.** Sampling the pwsh child during the failing run: private
   bytes climbed ~1 GB/s through the 256 MB soft limit and the 320 MB hard commit limit without a
   single allocation failure, plateauing at **33 GB** (the machine's commit limit). A process inside
   a job with `JOB_OBJECT_LIMIT_JOB_MEMORY` = 320 MB cannot commit 33 GB — so it was never in the
   job.
3. **Direct membership probe** (standalone script, same Win32 calls as `WindowsJobObject`, no pty
   involved): create job → spawn `cmd.exe /c <grandchild>` → assign cmd → wait → `IsProcessInJob`:

   | grandchild | in our job | in any job |
   |---|---|---|
   | MSIX `pwsh.exe` 7.6.4 | **False** | True (the package's own job) |
   | `powershell.exe` 5.1 (inbox) | **True** | True |

   `cmd.exe` itself reports `inJob=True` in both arms — assignment works; only the packaged
   grandchild escapes.

Given the hog is outside the job, every kill path is inert by construction: the job's commit
counters never move (cmd + conhost stay at a few MB), so `WindowsJobObject.HasReachedMemoryLimit()`
never trips — its pid-list private-bytes sum only covers job members, and the job peaks stay tiny —
so `TerminateJobObject` is never called; and the hard limit constrains nothing that matters. The
child never dies, `runner.Exited` never completes, the 45 s timeout fires.

Also ruled out along the way:

- **Not backend-related.** The suite clears the machine-wide `ANTIPHON_PTY_BACKEND=modern` at
  startup (CARD-0045); the red run was on the default inbox conhost. The probe reproduces the escape
  with no pty at all.
- **Not the test's wait/poll logic.** `Task.WhenAny(runner.Exited, Task.Delay(45s))` is sound; the
  kill genuinely never happens.
- **Not a silent assignment failure.** `AssignProcessToJobObject` failures throw out of
  `StartAsync`; the test would have errored at start, not timed out.

Why red "since 2026-08-10": the test was presumably written/verified on a machine (or at a time)
where `pwsh.exe` resolved to a non-packaged install (`C:\Program Files\PowerShell\7\`). This
machine's pwsh is MSIX (CLAUDE.md records the MSIX pwsh explicitly, including the Scheduled-Task
path-vanishing gotcha), so the test has been deterministically red here ever since.

## 2. Verdict: test bug, not production bug

The job-object memory kill works exactly as designed for normal (non-packaged) process trees — the
probe's 5.1 arm lands in the job, and the fixed test proves the full pipeline: soft-limit trip →
`TerminateJobObject` → tree dies → `ExitReason == MemoryKilled`, within ~2 s of the hog starting.
The test bug is choosing an allocator binary that Windows exempts from job containment.

## 3. Is the feature live in production?

**Wired but dormant.** `MemoryLimitMb` flows end-to-end (server `AgentSessionSettings` →
`AgentLaunchSpec` → `SessionRunnerHttpClient` → `SessionRunnerRuntime` → pty-host `HostSession` →
`PtyAgentRunner.StartAsync`), but the setting defaults to 0 and **no appsettings/config anywhere in
the repo sets it** — so no real session currently gets a memory-limited job. No urgency beyond the
test.

**Caveat worth keeping (recorded here, no code change):** if `MemoryLimitMb` is ever enabled for
real sessions, any MSIX-packaged descendant (agents shell out to `pwsh` constantly, which is MSIX on
this fleet) escapes the cap — the limit bounds the non-packaged part of the tree only. A future
enablement slice should decide whether that's acceptable or needs a mitigation (e.g. monitoring
descendants by walking the process tree instead of the job pid list).

## 4. The fix (shipped with this plan, verified)

`tests/Antiphon.Agents.Pty.Tests/PtyAgentRunnerTests.cs`, in
`JobObject_kills_session_when_memory_limit_exceeded` only:

1. The batch line launches **`powershell.exe`** (Windows PowerShell 5.1) instead of `pwsh.exe`.
   5.1 ships with Windows, is never MSIX, and stays in the job. The allocation script
   (`[byte[]]::new(16777216)` into a `List`) is valid 5.1 syntax unchanged, and the script content
   is pure ASCII so 5.1's no-BOM/CP1252 reading quirk is moot.
2. `SkipIfPwshUnavailable()` dropped from this test (5.1 always exists; `SkipIfNotWindows` already
   gates). The helper stays — the stdin-cap tests still use pwsh legitimately (they only need *a*
   console client, containment doesn't matter there).
3. XML doc comment on the test pinning the MSIX-breakaway reason, so nobody "modernizes" it back to
   pwsh and re-introduces a machine-dependent red.

Verification: 3/3 isolated runs green, 8–12 s each (kill fires ~2 s after the hog starts, far
inside the 45 s budget). Full `Antiphon.Agents.Pty.Tests` suite: 275 total, 0 failed, 40 skipped
(one unrelated test flaked in a first full-suite pass and did not reproduce on rerun — consistent
with the known PTY-timing flakiness under full parallel load; this test passed in both passes).

## 5. CI/tagging

No headed or live-Windows-only tag needed. The test already skips on non-Windows
(`SkipIfNotWindows`) and sits in the `[NotInParallel("Headed")]` serial group with the rest of the
class; with the 5.1 allocator it is deterministic on any Windows box. Runtime dropped from
45 s-and-fail to ~2 s-and-pass, so it also stops being the slowest thing in the suite.
