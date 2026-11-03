# task-39e2eabd: CARD-1652 SessionRunner cmd.exe audit (Code, Final)

Branch `feat/card-task-39e2eabd`, runner mirror `/work/worktrees/task-39e2eabd`, base master `907edaebbd301af95629c31095f1b530803baea2`. Owner of the landing: Code task 39e2eabd. Product code is unchanged. No csproj, build or pin files changed. The land graph was not touched.

## Commits

- `0ed54fb424f4b44e37b43c64a9894169768fc139`: test, the audit witness (red)
- `d00d105c2dc5cdfa84930b2628638f3aad954388`: fix, eight classes converted to the portable shell, Windows-only sites guarded, reviewed list
- `0ba721605fb5a26e083964fcd8812c1f3ac4d117`: fix, C502_V25 skips off Windows (it uses the LocalHttpRunner Windows apphost)
- plus this report commit

## Invariant

Every `cmd.exe` literal in `tests/Antiphon.SessionRunner.Tests/**/*.cs` and `tests/Shared/HerdrTestProcess.cs` is one of these:

- behind `OperatingSystem.IsWindows()` on the same line or within the 6 lines above it (a ternary or an exception filter)
- a reviewed site in `WindowsShellGuardAuditTests.Reviewed`, keyed by file, trimmed line and count, with a reason

A Linux test therefore never launches the bare name `cmd.exe`. Off Windows, `Environment.SystemDirectory` is empty. The child then either fails to start, which is a Windows-dependent red, or exits at once, which is a vacuous green.

Witness: `WindowsShellGuardAuditTests` (3 tests, `[Category("Unit")]`):

- the scan
- a stale-entry check: every reviewed key must still match its counted lines
- a self-test of the scanner: a bare literal is flagged; a ternary, comments, a trailing comment and a guard outside the window are handled correctly

## Audit table (cmd.exe sites)

| File | Tests | Base (Linux) | Verdict | Action |
|---|---|---|---|---|
| RemoteControlConditionalInputTests | 4 | 2 red, 2 vacuous green | portable | ShellPath + InteractiveArgs |
| SessionBufferBoundsTests | 2 | 2 red | portable | shell, plus an sh filler loop and a quote-split marker off Windows (Windows input is unchanged byte for byte) |
| FirstWriteRaceTests | 1 | red | portable | ShellPath + InteractiveArgs |
| RunnerSessionGenerationTests | 5 | V27 red, V25 red, V26 x3 vacuous | portable; V25 Windows-only | `exit 3` becomes `-c "exit 3"` off Windows. V25 calls `Skip.Test` because LocalHttpRunner starts `Antiphon.SessionRunner.exe` on modern ConPTY |
| SessionLivenessTests | 1 spawn and 3 sweep tests using the helper | 1 red | portable | `/c exit 0` becomes `-c "exit 0"` off Windows |
| PtyHostAdoptionTests | 5 | 4 red, ToDto vacuous | portable | shell; the batch file becomes `exit-soon.sh` (`sleep 2; exit 7`) off Windows |
| PtyBackendSeamTests | 3 | inbox red | inbox is Windows-only; the rest are portable | `Skip.Test` on the InboxConhost/ModernConPty test; the shell is portable |
| RunnerMultilinePromptDeliveryTests | 1 | red | portable | ShellPath + InteractiveArgs |
| HerdrPaneDisposalGuardedLiveTests | opt-in live | skip (opt-in) | Windows-only | `Skip.Test` off Windows in StartOwnedCodexDummyAsync, which copies a renamed cmd.exe |
| C1022BackendLaunchTests, GrokRulesHttpAcceptanceTests, RunnerCustodyTests | | skipped | already Windows-gated at method or fixture level | reviewed |
| HerdrPaneDisposalGuardTests G063, HerdrPaneDisposalStopRegressionTests G055/G056 | | skipped | SkipLocatorOnLinux (CARD-0864) | reviewed |
| RunnerCustodyLedgerBackendTests, HerdrAttachTests, HerdrLaunchShapeTests x2, HerdrPaneChildKillTests x3 | | n/a | data only (a process name or a ledger request; nothing is spawned) | reviewed |
| RunnerPlatformLaunchTests, RunnerChildClaudeTokenInheritanceTests, Card1636AdoptionIdentityTests, RunnerAbsenceEvidenceRuntimeTests, WorkspaceParkPublicationTests, RunnerWorkspaceServiceTests, CompactionContinuationStopTests, SessionCpuWatchdogTests, TranscriptAdoptionSafetyTests | | | already guarded (ternary or exception filter) or fixed by CARD-1650 | none |

## Evidence (server2, native default output, UseAppHost=false, one build-slot lease per build or run)

- Base `907edaebb`, whole project: 1914 total, 137 failed, 1695 passed, 82 skipped. TRX: `.antiphon/c1652/base/c1652-base.trx`.
- Red witness at `0ed54fb4`: audit class 3 total, 1 failed (`Every_command_interpreter_literal_is_behind_a_windows_guard`, 22 offenders), 2 passed. TRX: `.antiphon/c1652/red/`.
- Narrow green at `0ba72160`: the audit plus the 9 touched classes, 38 total, 0 failed, 28 passed, 10 skipped. Of the 10 skipped, 2 are new (V25 and the inbox test). The other 8 were already skipped: 6 HerdrPaneDisposalGuardedLive opt-in skips and 2 modern-ConPTY tests. TRX: `.antiphon/c1652/green2/`.
- Whole project at `0ba72160`: 1917 total, 123 failed, 1710 passed, 84 skipped. Every tip failure was also red at base. 12 base reds now pass, and 2 are skipped as Windows-only (V25 and the inbox test). TRX: `.antiphon/c1652/tip/c1652-tip.trx`.
- Mutant (uncommitted, run once, then restored): `RemoteControlConditionalInputTests.Cmd` was put back to `Path.Combine(Environment.SystemDirectory, "cmd.exe")`. Audit plus class: 7 total, 3 failed. The audit flagged exactly `RemoteControlConditionalInputTests.cs:15`, and C514_Input and C514_Replacement were red. TRX: `.antiphon/c1652/mutant/`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 907edaebb... -HeadRef HEAD`: commits=3, entries=0, violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean. origin/master is still `907edaebb`.
- Slot waits per lease were 105 to 391 s.

## Residue: 123 Linux reds that are not cmd.exe (not fixed; outside this card's cmd.exe scope)

All 123 are identical at base.

- pwsh.exe/powershell.exe `ShellResolutionException` and Win32Exception (93): RunnerRestartHealthTests 55, RunnerRestartPreflightSafetyTests 26, RunnerRestartScriptCompatibilityTests 8, DaemonLogRotationTests 4. These are rows parameterised by Windows PowerShell engines; they need a `Skip.Test` per engine or a pwsh mapping. Follow-up card recommended.
- Windows launch policy and host asserts (23): CodexWindowsLaunchPolicyTests 19 (Windows `should throw`), CodexCliVersionWindowsTests 4 (`OperatingSystem.IsWindows() should be True`). These are Windows-only by design but have no skip.
- Single reds that need a Debug (7): HerdrGkpLaunchGuardTests 3, HerdrGrokResumeGuardTests 1, PtyHostLeakSweepTests 1, PhoneHomeConnectionServiceTests 1, LinuxRunnerCustodyLifecycleTests 1 (the host helper `/usr/local/bin/antiphon-custody-kill` fails with `id: unbound variable`; this depends on the host).
- Possible vacuous greens of the `if (!OperatingSystem.IsWindows()) return;` form (they pass on Linux without running anything; they are not cmd.exe): RunnerCustodyLedgerBackendTests.cs:84 and :104, CompletedCardWorkspaceS5Tests.cs:614.
- `tests/Antiphon.Tests` has 122 `cmd.exe` hits in 75 files. The brief scoped this task to SessionRunner.Tests, so they were not audited. Follow-up card recommended, reusing `WindowsShellGuardAuditTests.Scan`.

## Pending for Mutation (PCs)

- PC-1: an unguarded literal in any SessionRunner test file turns the audit red. Shown once above; still pending for SourceLanding.
- PC-2: removing the 6-line guard window, or treating comments as code, turns `Scan_flags_...` red.
- PC-3: a converted test with a live child goes red when its product condition is mutated. Candidates: C514 conditional-input generation check, SessionBufferBounds trim cap, PtyHostAdoption real exit code.
