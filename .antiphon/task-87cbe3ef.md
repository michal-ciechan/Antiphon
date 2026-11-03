# task-87cbe3ef: CARD-1669 slice A, Linux SessionRunner.Tests groups (1), (2) and (4) (Code, Final)

Branch `feat/card-task-87cbe3ef`, runner mirror `/work/worktrees/task-87cbe3ef`, base master `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c`. Landing owner: Code task 87cbe3ef. No product code, csproj, pin or land-graph files changed. Only test files changed.

## Commits

- `22d82e7ec4038b70aadc2dc334ec290d4328b76b`: the restart fixture maps shell rows to the `pwsh` host off Windows, and adds skips with reasons
- `aafb98682f47816ab00ac4086a26ab77a7ce54c3`: the hung-HTTP 200 row skips off Windows (it was red in the tip run)
- plus this report commit

## Invariants

- I-1 (group 1): on Linux, a restart-script row that names `pwsh.exe` runs the real PowerShell 7 `pwsh` host. A row that names `powershell.exe` (Windows PowerShell 5.1) is a skip with a reason. On Windows, `RestartFixture.HostShell` returns the name unchanged.
- I-2 (groups 1, 2 and 4): a test whose subject exists only on Windows skips off Windows with a reason, instead of going red or passing vacuously. Those subjects are run-daemon.ps1 under cmd.exe, CodexWindowsLaunchPolicy (`Apply` is the identity off Windows, product line 41), the Windows Codex npm/PATHEXT resolution, iphlpapi TCP owner lookup, Windows share-mode denial of delete and move, and the WindowsJob custody backend.
- Windows behaviour: every new branch sits behind `!OperatingSystem.IsWindows()`, or behind `OperatingSystem.IsWindows() ||`. `ShouldBeTrue(IsWindows)` became `if (!IsWindows) Skip.Test`, which is a no-op on Windows.

## Changes by file (all under tests/Antiphon.SessionRunner.Tests)

- `Fixtures/RunnerRestart/RestartFixture.cs`: `HostShell(shell)` is used by `Script` and `Run`. I did not change the resolver, because `RunnerRestartPreflightCacheTests` puts literal `pwsh.exe` files on PATH on Linux.
- `RunnerRestartPreflightSafetyTests.cs`:
  - `Resolve(HostShell(shell))`
  - the ELF pwsh has no version resource, so the version line is compared only when one exists
  - `Malformed_*` raises the host skip before `Should.ThrowAsync<Exception>`, which would otherwise swallow it
  - `Bound_inputs_deny_writes` skips off Windows for its shell rows (Windows share modes)
- `RunnerRestartScriptCompatibilityTests.cs`: the `Hung_http(…, false)` row skips off Windows (iphlpapi).
- `DaemonLogRotationTests.cs`: `RunSupervisorAsync` skips off Windows.
- `CodexWindowsLaunchPolicyTests.cs`: `RequireWindowsHost()` is called first in all 25 methods. 7 of them passed vacuously on Linux at base. Making the policy platform-injectable needs a product change, which the brief forbids.
- `CodexCliVersionWindowsTests.cs`: 4 `IsWindows().ShouldBeTrue(tag)` became `Skip.Test` with the same tag.
- `RunnerCustodyLedgerBackendTests.cs` (lines 84 and 104) and `CompletedCardWorkspaceS5Tests.cs` (line 614): `return;` became `Skip.Test`. In S5, the portable late-content half still runs before the skip.

## Evidence (server2, native default output, UseAppHost=false, TEMP=/tmp, one build-slot lease per build or run)

- Base 7dc9e116, whole project: 1917 total, **123 failed**, 1710 passed, 84 skipped. TRX: `.antiphon/c1669/base/c1669-base.trx`. The 123 match the card exactly. This run is the red witness.
- Tip 22d82e7e, whole project: 1917 total, **8 failed**, 1766 passed, 143 skipped. TRX: `.antiphon/c1669/tip/c1669-tip.trx`.
  - Transitions: 66 Failed→Passed (Restart classes on Linux pwsh: Health 50, PreflightSafety 13, ScriptCompat 3); 49 Failed→Skipped; 10 Passed→Skipped (vacuous: Codex policy 7, CustodyLedger 2, S5 1).
  - Remaining 8: the 7 group-(3) singles (HerdrGkpLaunchGuard 3, HerdrGrokResumeGuard 1, PhoneHomeConnectionService 1, PtyHostLeakSweep 1, LinuxRunnerCustodyLifecycle 1), all red at base and outside this task's scope. The eighth is `Hung_http_response_is_canceled_at_wait_deadline(pwsh.exe, False)`, red as `wait-failed` because `[AntiphonRestartTcpOwner]::Find` uses iphlpapi. It was fixed in aafb9868.
- Narrow green at aafb9868, ScriptCompatibility class: 8 total, 0 failed, 3 passed, 5 skipped. TRX: `.antiphon/c1669/green/`.
- Expected whole-project result at aafb9868, not rerun: 7 failed (group 3 only), 1766 passed, 144 skipped.
- Mutants: one per fix, in 4 files, one build, run once, then restored with `git checkout -- <path>` (status clean). Filter: the 4 classes. Result: 42 total, 27 failed. TRX: `.antiphon/c1669/mutant/`.
  - M1 (`HostShell` returns the name unchanged): ScriptCompat pwsh rows 3/3 red with `ShellResolutionException: pwsh.exe`
  - M2 (`RequireWindowsHost` emptied): Codex policy 19 red, plus 7 vacuous passes
  - M3 (DaemonLogRotation skip removed): 4/4 red with `Win32Exception pwsh.exe`
  - M4 (C1031 skip removed): red
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 7dc9e116 -HeadRef HEAD`: violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean (origin/master is still 7dc9e116).
- Slot waits per lease were up to about 10 minutes.

## Pending for Mutation (PCs)

- PC-1: the `HostShell` pwsh.exe→pwsh mapping (M1 run once here).
- PC-2: each Windows-only skip guard (M2–M4 run once here). Still pending: the PreflightSafety `Bound_inputs` and `Malformed` pre-skip, the S5/CustodyLedger skips (outcome Passed vs Skipped), and the ScriptCompat iphlpapi skip.
- PC-3: the Windows lane. Every touched class must stay byte-identical in behaviour on native Windows, which this Linux lane cannot show. The live Windows qualification is the witness.
