# task-24eb34b0: Review (Final, round 1 of 3) of CARD-1669 slice A — Code task 87cbe3ef

Verdict: **Clean.** Tested SHA `d9d9b217a913cded71e72b277d344da06591c81b` (base master `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c`). No defects.

## Checks

1. Scope: `git diff --stat 7dc9e1168..d9d9b217a` touches 8 files under `tests/Antiphon.SessionRunner.Tests` plus `.antiphon/task-87cbe3ef.md`. No product, csproj, build, pin or land-graph file changed.
2. Windows path, checked file by file. Every new branch is `if (!OperatingSystem.IsWindows()) Skip.Test(...)`, `!hang && !IsWindows`, or `IsWindows() || ...`. None of them can fire on Windows.
   - `RestartFixture.HostShell` returns the name unchanged on Windows. `Script` and `Run` reassign the same value.
   - `Malformed_*` adds a bare `HostShell(shell)` call. On Windows it has no side effect.
   - `Resolved_shell` passes `Resolve(HostShell(shell))`, which is the identity on Windows. The version assertion still runs unconditionally on Windows.
   - `CodexCliVersionWindowsTests` replaced `ShouldBeTrue(IsWindows)` with a skip. Both are no-ops on Windows.
   - The `return;` lines that became `Skip.Test` in CustodyLedger x2 and S5 sit behind the same condition as before.
   - Command lines, arguments, environment, assertions and timing are unchanged on Windows.
3. No vacuous conversions:
   - The 66 rows that went from Failed to Passed (Health 50, PreflightSafety 13, ScriptCompat 3) run the real copied restart entry script under the resolved `/usr/bin/pwsh`.
   - I spot-checked five of them: Default_budget, Explicit_60_second, Wall_clock_jumps, Probe_and_sleep, and Wait_exception (pwsh rows). Each asserts outcome, exit code, wait budget, trace probe counts, JSON fields and mutations.
   - Skip-swallowing audit: `powershell.exe` rows inside `Should.ThrowAsync<Exception>` would catch a skip and pass vacuously. Warm_cache already calls `f.Run(shell)` unwrapped first, and Malformed raises the skip before the wrapper. No other wrapped call site takes a shell argument.
4. Skip reasons. I checked 9 against the code, and all are true:
   - `CodexWindowsLaunchPolicy.Apply` returns the request unchanged off Windows (product line 41).
   - `run-daemon.ps1` launches through `cmd.exe /s /c ... >>`.
   - `AntiphonRestartTcpOwner` uses `DllImport("iphlpapi.dll")`.
   - The CodexCliVersion probe has Windows-only PATHEXT/MZ branches, and those tests were already Windows-asserting.
   - The WindowsJob custody rows and the S5 PC-587 rows were previously `return;`.
   - The Bound_inputs share-mode skip is correct: Unix allows unlink and rename of open files.
   - The 7 Codex policy tests that passed at base (claude_request, codex_transcript non-shim, path_based custom exe, already_normalized, Explicit_codex_exe, Relative_direct, incident_fixture) only exercised `Apply`, which is the identity off Windows. They tested nothing on Linux.
5. `WindowsShellGuardAuditTests` passes 3/3 at the tip. The new `cmd.exe` reason string sits one line under its `IsWindows()` guard.
6. Whole project at the tip (one build lease, one run lease, native output, UseAppHost=false):
   - **1917 total, 7 failed, 1766 passed, 144 skipped**, duration 15m20s.
   - Against the Code base TRX: 66 Failed→Passed, 50 Failed→Skipped, 10 Passed→Skipped (the vacuous ones), and **0 Passed→Failed**.
   - The 7 reds are the group-3 singles, also red at base: HerdrGkpLaunchGuard x3, HerdrGrokResumeGuard, PhoneHomeConnectionService, PtyHostLeakSweep, LinuxRunnerCustodyLifecycle C1614.
   - TRX: `.antiphon/r24/tip/r24-tip.trx` (mirror only, gitignored).
7. Mutants: one build in a detached scratch worktree, filtered to the 4 classes, 42 total, 30 failed. Each mutant went red:
   - M1, `HostShell` returns the name unchanged: the ScriptCompat pwsh rows went red 3/3 with `ShellResolutionException: pwsh.exe`.
   - M2, `RequireWindowsHost` emptied: 19 red, and the 7 vacuous tests passed.
   - M3, DaemonLogRotation skip removed: 4/4 red with `Win32Exception pwsh.exe`.
   - M4, C1031 skip removed: red on the codexCliVersion assertion.
8. `check-evidence-diff` reported violations=0. `merge-tree` onto origin/master (still `7dc9e1168`) is clean.
9. rc port: chained `--merge-base` picks of `22d82e7ec` and `aafb98682` onto `rc/2026-11-01` (`10a826b9d`) are textually clean. CARD-1650 and CARD-1652 are not on rc, so port them first; the audit class is absent on rc without CARD-1652.

## Advisories

- A-1: On Linux, `Resolved_shell_is_the_process_that_runs` no longer compares the FileVersion line, because the ELF pwsh has no version resource. This affects Linux only.
- A-2: On Linux, skipping `The_incident_fixture_is_accepted_on_the_node_path` drops an incidental `WindowsCommandLine.Measure` length check of the incident fixture. Windows still runs it.

## PCs

PC-1, PC-2 and PC-3 remain pending for the SourceLanding Mutation. My M1–M4 runs do not discharge them, and the Windows qualification is still the PC-3 witness.
