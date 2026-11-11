# Linux SessionRunner Unit lane remeasure (CARD-1212)

Measured 2026-11-11 on `origin/master` `584a169521a763d682da8313a9bc84f3c24583fb`. That SHA was also the base of `feat/card-task-a8a16d5a`. The card's 21 failures are the `Antiphon.SessionRunner.Tests` Unit category (378 results on 2026-10-13), not the `Antiphon.Tests` Unit lane. Entry point from `docs/testing-and-build.md`: one leased `dotnet build tests/Antiphon.SessionRunner.Tests -p:UseAppHost=false`, then `dotnet run --no-build -p:UseAppHost=false` with `--treenode-filter /*/*/*/*[Category=Unit]` and `TEMP=/tmp`.

## Remeasure at the base

664 total, 1 failed, 635 passed, 28 skipped, about 30 seconds. One failure:

`WindowsShellGuardAuditTests.Every_command_interpreter_literal_is_behind_a_windows_guard` — `offenders` had 5 items:

- `C1817RunnerRepositoryMaintenanceTests.cs:402` `ShouldBe("cmd.exe")` on `SleepStart`
- `C1817RunnerRepositoryMaintenanceTests.cs:403` `ShouldBe("cmd.exe")` on `TrueStart`
- `MaintenanceFixture.cs:30` `Redirect("cmd.exe")` in `SleepStart`
- `MaintenanceFixture.cs:45` `Redirect("cmd.exe")` in `TrueStart`
- `MaintenanceFixture.cs:145` `Redirect("cmd.exe")` in `CreateJunction`

Message suffix: `C1652_UNGUARDED_INTERPRETER`.

## Diff against the card list

The 2026-10-13 list was 19 `CodexWindowsLaunchPolicyTests` results, `RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed`, and `PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout`. `Nonpositive_budget_is_refused` was two parameter rows (`0` and `-1`), which is why 18 method names were reported as 19 results. All 19 are fixed on current master. The shell-audit failure is new.

| test | cause | fix | commit |
| --- | --- | --- | --- |
| `CodexWindowsLaunchPolicyTests`: `A_configured_budget_above_30000_does_not_raise_the_transport_ceiling`, `A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling`, `Explicit_cmd_launcher_is_capped_at_7000`, `Explicit_node_codex_js_is_left_alone_but_still_budgeted`, `Install_root_with_spaces_and_unicode_round_trips`, `Missing_node_never_returns_the_batch_request`, `Missing_sibling_and_PATH_node_is_codex_launcher_unavailable`, `Native_hop_is_bounded_by_the_longest_installed_native_path`, `Nonpositive_budget_is_refused` (`0` and `-1`), `Nul_in_any_argument_is_refused`, `PATH_node_is_used_when_no_sibling`, `Quote_expansion_is_counted_not_estimated`, `Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported`, `Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused`, `Shim_without_codex_js_is_codex_launcher_unavailable`, `Sibling_node_wins_over_PATH_node`, `Standard_npm_shim_becomes_node_exe_plus_codex_js`, `Surrogate_pairs_count_two_units` | Off Windows, `CodexWindowsLaunchPolicy.Apply` returns the request unchanged. The old tests expected the Windows rewrite or `CommandLineToArgvW`. | `RequireWindowsHost` skips with `CARD-1669: CodexWindowsLaunchPolicy applies only on Windows`. This run: `NotExecuted`. | `897e6509040e4b70fd649bfc66e076d23a772d66` |
| `RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed` | The test expected `VerificationCustodyException` where a probed Linux runner now accepts `LinuxCgroup` and writes `runner-start-intent.json`. | Platform branch: Windows still refuses; Linux accepts. This run: Passed. | `7e0ed16348e83c4f879798b11b87c7c48beaac5b` |
| `PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout` | Under load the accepted socket was unrooted, finalized, and the wait saw `WebSocketException` instead of `OperationCanceledException`. Passed at the old base and on an isolated rerun. | The test keeps the accepted client until the assertion and forces a GC. This run: Passed (1/1). | `1802fa23a418cb29619abbbc9862a6dc64660e2a` |
| `WindowsShellGuardAuditTests.Every_command_interpreter_literal_is_behind_a_windows_guard` | New. CARD-1817 put `cmd.exe` behind `Windows`, which is `ForceWindows ?? OperatingSystem.IsWindows()`. The audit only sees `OperatingSystem.IsWindows()` inside six source lines. The two `ShouldBe("cmd.exe")` lines name a `ProcessStartInfo` the test never starts. | The three spawn sites use `ForceWindows ?? OperatingSystem.IsWindows()` in that window. `CreateJunction` throws off that path. The two assertion lines are `Reviewed` because nothing is spawned. | `fb193a0a1ec0695727f878b749b809a5b108454e` |

No product defect. No backlog card.

## After the fix

Same Unit filter, same build shape, commit `fb193a0a1ec0695727f878b749b809a5b108454e`: 664 total, 0 failed, 636 passed, 28 skipped. `C1817_forced_windows_fixture_uses_no_posix_tool` (Integration, not in the Unit filter) passed 1/1.

Guard union on `tests/Antiphon.Tests`, one leased build, filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(C1514LandingSplitBudgetTests*)|(DerivedImpactIndexTests*)/*`: 107 passed, 0 failed, 0 skipped. Per class: policy 41, classification guard 1, slow registry 5, tripwire 2, NativeLinuxPolicyWitness 3, mechanical 11, TaskPlatformGuidance 5, C1729 9, lane guard 1, C1514LandingSplitBudgetTests 5 (including Passed `C1584_E2EExpandedBudget`), DerivedImpactIndexTests 24.

`git merge-tree --write-tree origin/master HEAD` at the fix commit exited 0. No product source changed.
