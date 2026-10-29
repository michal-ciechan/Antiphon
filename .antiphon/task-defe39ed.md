# Gate policy round 2: body-scoped Linux-only witness

Outcome: the Windows exclusion witness now judges each test on its own body. The 45 shipped policy rows are unchanged and still pass. The neighbour-window hole and the conditional-gate holes fail the witness.

Commit: `19435beb086327d5c557b299cfb9330c541b47a7` on `feat/card-task-defe39ed`.
Parent: `3b544b9f7939405c2338e131a3916e005217bacc`.
Code tip this round started from: `65862ecfdf961619fdac8e1f473d6805fad5a3ab`.

## Files

- `scripts/lib/linux-only-gate-witness.cs` — new `RcPlatformWitness.LinuxOnlyGate.RowProblems`. Loaded once with `Add-Type -Path`. `-Language` together with `-Path` is a parameter-set error and is not used.
- `scripts/test-rc-platform.ps1` — `Test-LinuxOnlyWindowsExclusions` calls that checker. Inventory of 45 rows and the forbid regex are unchanged. `tests/test-execution-policy.json` is unchanged in this commit.
- `tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs` — `LinuxOnlyWindowsExclusions` expects 8 rows, including `C1323 synthetic-gate-holes-refused`.

The tokenizer keeps spans and does not emit braces inside comments, character literals, verbatim strings, interpolated strings, or raw `"""` / `$"""` / `$$"""` literals. Interpolation holes are not code. A gate check uses only that test's own body, plus a class `[Before(Test)]` / `[Before(Class)]` or an instance constructor when the row is a class row.

## Forms the 45 rows use

1. Top-level `if (!OperatingSystem.IsLinux())` then `Skip.Test` or `throw …SkipTestException`, including a block that only skips and returns.
2. Top-level `if (OperatingSystem.IsWindows())` then skip or throw.
3. Top-level `NativeLinuxGate.Require` or `RequireNativeLinux`, including a call through `C1008HostFixture`.
4. Top-level call whose local body is a gate: `RequireLinuxJoin`, `RequireLinuxCustody`, `RequireUnix`, `RunProbeAsync`, `ExternalBuild`, `RequireLinuxIntegration`, `C1105AuditContract`.
5. `[Before(Test)]` whose body is a gate, including expression-bodied `RequireNativeLinux()` and `RequirePlatform(OperatingSystem.IsLinux() ? "linux" : "other")` when that helper throws `SkipTestException` unless the platform is linux.
6. Top-level `new` of a fixture whose instance constructor starts with the skip.
7. Top-level `if (!Linux()) return;` when `Linux()` is `if (OperatingSystem.IsLinux()) return true;` followed only by an unconditional skip and/or `return false`.
8. One straight-line preamble call immediately before form 1 (`RequireClient()`).

No row uses a class-level skip attribute. A return-true helper is accepted only when every other statement skips or returns false. A gate nested under another condition, loop, or try is not a gate. `if (!OperatingSystem.IsLinux() && …)` is not a gate.

## Synthetics that must fail

`synthetic-gate-holes-refused` refuses all of these:

- class row with an ungated test placed before `NativeLinuxGate.Require`
- that ungated method named alone
- `Linux()` ending in `return File.Exists("C:\\tools\\wsl-sh.exe")` (M2)
- `NativeLinuxGate.Require` nested under `Environment.GetEnvironmentVariable("X") is null` (M3)
- method row `C1008_Recycle_exact_default_volumes` with its gate removed, while the sibling stays gated
- `if (!OperatingSystem.IsLinux() && !File.Exists(...)) Skip.Test(...)`

The same assert admits the neighbour's gated method, the real `Skip.Test` then `return false` helper, and the gated sibling. `ungated-class-refused` still requires exactly `forbid Case` for a skip message that matches the forbid regex.

## Verification

`LinuxOnlyWindowsExclusions` on the fixed script: 8 passed, 0 failed, including `every-new-row-has-linux-gate`, `ungated-class-refused`, and `synthetic-gate-holes-refused`. A real-row failure would have been named and left red. None failed.

Same slotted harness, `TEMP=/tmp` and an explicit results directory:

| Case | Result |
|---|---|
| C1415_SkipExclusions | 10 passed, 0 failed |
| LinuxSessionRunnerAdmission | 13 passed, 0 failed |
| C1323_PlatformPolicy | 19 passed, 0 failed |
| C1323_PlatformExecution | 12 passed, 0 failed |
| C1404_Group10MeasuredBudget | 1 passed |
| C1577_Group71MeasuredBudget | 1 passed |
| C1584_E2EExpandedBudget | 1 passed |
| C1410_MeasuredCostGroups | 1 passed |
| G2865_Group28Group65MeasuredBudget | 1 passed |

Isolated build `tests/Antiphon.Tests` with `UseAppHost=false` and `OutputPath=bin-defe39ed/`: 0 errors. `dotnet exec` of that DLL, commit `19435beb0`:

- `/*/*/(ReleaseGatePlatformTests*)|(RcRosterRepairTests*)|(NightlyScriptsTests*)|(TestPhaseSupervisorTests*)|(C1514LandingSplitBudgetTests*)/*` — 19 passed, 0 failed, 0 skipped. TRX names the four release-gate cases (including `LinuxOnlyWindowsExclusions`), three roster-repair cases (`Mirror`, `Explicit`, `Costs`), five nightly-script cases, `C1514T_PlannedManifestAndEnvironment`, `C1514T_ActualAndFailurePaths`, and the five census wrappers.
- `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` — 1 passed, 0 failed.

The 33 `bin-defe39ed` directories were removed by literal path. No slot timed out (exit 4 did not occur).

## Publication

`git merge-tree --write-tree origin/rc/2026-10-27 HEAD` exits 0. rc tip is `cf1a124035f45857e3e2bf205ab9f8b05cf7a8e4`.

`git merge-tree --write-tree origin/master HEAD` conflicts. `origin/master` is `f8bddb6efda7b0e8f50f3b45744087e52188bc66`. The same five paths already conflicted at parent `3b544b9f7`:

- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `scripts/test-nightly-tests.ps1` (content)
- `scripts/test-rc-platform.ps1` (content)
- `tests/test-execution-policy.json` (content; policy-hash both sides)

Master was not merged into this branch. The witness was not loosened.
