# task-ad29ef52: Review (Final, round 1) of CARD-1652, SessionRunner cmd.exe audit

Subject: `feat/card-task-39e2eabd` at tip `991b59357b5167df633ad3c94d9d0fe9ccce612d` (last code commit `0ba721605fb5a26e083964fcd8812c1f3ac4d117`), base `907edaebbd301af95629c31095f1b530803baea2`. Landing owner: Code task 39e2eabd.

**Verdict: Clean. No defects.** Four advisories follow.

## Checks

1. **Diff is test-only.** `git diff --name-only base..tip` lists `.antiphon/task-39e2eabd.md` and 10 files under `tests/Antiphon.SessionRunner.Tests/`. No product, csproj, build, pin or land-graph file changed. None of the changed files is linked into another project. `Antiphon.Tests.csproj` links other SessionRunner.Tests files, but none of these.
2. **The Windows path is unchanged.** `HerdrTestProcess.ShellPath` on Windows is `Path.Combine(Environment.SystemDirectory, "cmd.exe")`, and `InteractiveArgs` is `["/d","/q","/k","@echo off & prompt $G"]` (tests/Shared/HerdrTestProcess.cs:34-38). Both match the base values byte for byte. Each Windows branch was checked against base:
   - FirstWriteRace, RemoteControlConditionalInput, RunnerMultilinePromptDelivery, PtyBackendSeam and SessionLiveness: the launch path and arguments are identical. SessionLiveness keeps `"/c exit 0"`.
   - RunnerSessionGeneration: V27 keeps `["/d","/c","exit 3"]`.
   - PtyHostAdoption: the `exit-soon.cmd` name, its bytes and the `["/d","/c",batch]` arguments are identical. Only an assertion message string changed.
   - SessionBufferBounds: the Windows `FillerInput` and `MarkerInput` strings are identical to the old literals.
   - The new skips are all `!OperatingSystem.IsWindows()`.
   - No assertion was weakened.
3. **The Linux paths are live.** Each converted test drives a real `/bin/sh -i` or `sh <script>` child:
   - SessionBufferBounds: the ansi log must grow past 4 KB (`WaitUntilAsync` throws on timeout), and the quote-split marker cannot be satisfied by the tty echo.
   - PtyHostAdoption: asserts `ExitCode == 7` and an alive child pid across adoption.
   - C514: the mutant below turns it red.
4. **Reviewed list.** I spot-checked 7 entries against source, and each reason holds:
   - C1022 `LaunchAsync`, :30: `SkipTestException`.
   - GrokRules, first line: skip.
   - RunnerCustody: `CustodyFixture` constructor, :247.
   - HerdrPaneDisposalGuard G063, :176: `SkipLocatorOnLinux`.
   - StopRegression `AttachedGuard`: `SkipLocatorOnLinux`.
   - RunnerCustodyLedgerBackend `Request()`: passed only to `Ledger.PrepareStart`.
   - HerdrPaneChildKill: the dummy comes from `HerdrTestProcess.StartDummy()`, and the literal is only fake pane data.

   All 7 sites that the window accepts are truly guarded: ternaries, `catch ... when (OperatingSystem.IsWindows())` filters, and skips. The window can still be fooled; see A-1.
5. **C502_V25 skip.** It uses `Skip.Test("…")` with a reason, the project idiom (12 uses). The skip is correct: `LocalHttpRunner.cs:27` starts `AppContext.BaseDirectory/Antiphon.SessionRunner.exe` with `--SessionRunner:PtyBackend modern`. The test was red on Linux at base, so no Linux coverage is lost.
6. **Runs.** server2, native default output, `-p:UseAppHost=false`, one build-slot lease per build and per run, built from HEAD `991b59357` with a clean status.
   - Audit plus the 9 touched classes: 38 total, 0 failed, 28 passed, 10 skipped. The skips are 6 opt-in GuardedLive tests, 2 modern-ConPTY tests, V25 and the inbox test. Duration 37.5 s.
   - Mutant (`RemoteControlConditionalInputTests.cs:15` set back to the hard-coded cmd.exe): 7 total, 3 failed. The audit flagged exactly `RemoteControlConditionalInputTests.cs:15`, and `C514_Input` and `C514_Replacement` were red. Restored with `git checkout --`, then rebuilt.
   - Whole SessionRunner.Tests project: 1917 total, 123 failed, 1710 passed, 84 skipped, 10m21s. Compared with Code's base TRX (137 failed): 0 new reds.
7. **check-evidence-diff and merge-tree.**
   - `check-evidence-diff.ps1 -Repository . -BaseRef 907edaebb… -HeadRef 991b5935…`: commits=4, entries=1, violations=0, exit 0.
   - `merge-tree` onto origin/master (`907edaebb`) is clean: tree `5947eff0`.
   - rc/2026-11-01 (`10a826b9d`): see A-2.

## Advisories

- **A-1: the guard window can be fooled.**
  - Where: WindowsShellGuardAuditTests.cs:107-108.
  - Any `OperatingSystem.IsWindows()` text in the 6 lines above counts as a guard. That includes an unrelated member, an inverted guard, or a string literal. Example: put `private static bool OnWindows => OperatingSystem.IsWindows();` directly above `private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");`. The scan passes, and the literal is still unguarded. The self-test at :92 even uses the inverted form `if (OperatingSystem.IsWindows()) return;` as its guard.
  - No current site exploits this.
  - Optional hardening: accept only the ternary or `when (` forms, or a `!OperatingSystem.IsWindows()` skip or throw line.
- **A-2: an rc port needs CARD-1650 first.**
  - The three code commits cherry-pick onto rc/2026-11-01 cleanly (per-commit `merge-tree --merge-base` chain, tree `9464ca53`).
  - A whole-branch merge onto rc conflicts only in the unrelated `client/src/api/agentTasks.ts` (master/rc drift).
  - rc still hard-codes cmd.exe at CompactionContinuationStopTests:14, SessionCpuWatchdogTests:22 and TranscriptAdoptionSafetyTests:1854. The audit is a Unit test and runs on Windows too, so it would be red on rc unless CARD-1650 is ported first.
- **A-3: some waits can be satisfied by the tty echo alone.**
  - PtyHostAdoption (`before-restart-marker`, `after-adopt-marker`) and FirstWriteRace (`RACE-MARKER-OK`) wait for a marker that the tty echo of the typed line also prints.
  - This is the same as cmd's echo at base, so it is not a regression. SessionBufferBounds avoids it with the quote split.
- **A-4: V25 Linux port.** Starting the runner dll with `dotnet` would give V25 Linux coverage. That is a follow-up, not this card.
