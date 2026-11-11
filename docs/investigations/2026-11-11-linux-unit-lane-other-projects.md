# Linux Unit lanes other than SessionRunner (CARD-1212)

Debug task 4c8eac9f, 2026-11-11. Host `6f03ea7dd3f9`, 24 logical CPUs, Debian 12 bookworm, linux-x64, .NET 9.0.22, TUnit 1.44.0, Microsoft Testing Platform v2.2.2. Branch `feat/card-task-4c8eac9f`. Measurement base `188ac942166884f060fdcc7cbf3b15f670a27b43`. The one product change is `f98170eca23fab26c7afb79ed584c0381beb7891`.

Prior debug a8a16d5a measured only `tests/Antiphon.SessionRunner.Tests` (664 total, 1 failed, fixed on `fb193a0a1`). This run measures the other named Unit lanes. `TEMP`, `TMP`, and `TMPDIR` were `/tmp`. Each build and `dotnet run` went through `scripts/build-slot.ps1`. `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` used `UseAppHost=false` on the build and on the `--no-build` run. One leased `Antiphon.Tests` build (4m 21s, 0 errors) served every later slice.

The whole `/*/*/*/*[Category=Unit]` category is past 45 minutes, so `Antiphon.Tests` was split by class-name prefix. An MTP `--timeout` abort writes no TRX. Console summaries are the record for those two slices. Cancelled tests were waiting on `ParallelLimiter<ProcessSpawnLimit>` and were re-run; they are not failures.

## Verdict

Messaging, Server.Git, and Agents.Pty Unit lanes are green. `Antiphon.Tests` class prefixes A-Z were all run. After the re-runs below, one product failure was fixed and one load-only regex timeout did not reproduce. Three tests stay red under the plain Unit filter. They are the CARD-1832 witnesses: each body passes only when `ANTIPHON_PLATFORM_FILTER` is the value of the lane that should select it. The documented include filter executed 2 and failed 0. The documented exclude filter executed 1 and failed 0. A plain Unit process sets neither value, so those three fail together by design. Assertions were not loosened and no skip was added. CARD-1832 is in Review and owns that taxonomy. A board search for `ANTIPHON_PLATFORM_FILTER` and `PlatformTaxonomyFilterTests` found no separate card.

## Other projects

| Project | Total | Passed | Failed | Skipped | Duration |
|---|---:|---:|---:|---:|---|
| `tests/Antiphon.Messaging.Tests` | 3 | 3 | 0 | 0 | 1s 970ms |
| `tests/Antiphon.Server.Git.Tests` | 14 | 14 | 0 | 0 | 3s 902ms |
| `tests/Antiphon.Agents.Pty.Tests` | 446 | 442 | 0 | 4 | 55s 766ms |

Messaging has one Unit class, `InMemoryInboxReceiptStoreTests`. Server.Git Unit classes are `ServerGitAssemblyTests` and `GitIndexLockTests`. Agents.Pty skips are Windows-only: `Measure_equals_the_line_the_child_parses` (`CommandLineToArgvW`) and `Probe_reports_unavailable_for_a_junction_to_a_missing_directory` for `sessions`, `home`, and `parent` (CARD-1675 junctions).

`tests/*/*.csproj` also contains harnesses, probes, E2E, and these test projects that the brief did not name: `Antiphon.IO.WriteBehind.Tests`, `Antiphon.MarkdownPdf.Tests`, `Antiphon.Observability.Tests`, `Antiphon.PtyHost.Tests`, `Antiphon.TimeoutAudit.Tests`. They were not run.

## Antiphon.Tests Unit slices

| Slice | Filter | Total | Passed | Failed | Skipped | Duration | Record |
|---|---|---:|---:|---:|---:|---|---|
| A-B | `/*/*/(A*)\|(B*)/*[Category=Unit]` | 598 | 595 | 0 | 3 | 2m 42s 415ms | `unit-ab.trx` |
| C | `/*/*/(C*)/*[Category=Unit]` | 1150 | 1146 | 0 | 4 | 17m 45s 919ms | `unit-c.trx` |
| D-L | `/*/*/(D*)\|(E*)\|(F*)\|(G*)\|(H*)\|(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit]` | 1769 | 1750 | 0 | 19 | 34m 44s 801ms | `unit-dl.trx` |
| M-R | `/*/*/(M*)\|(N*)\|(O*)\|(P*)\|(Q*)\|(R*)/*[Category=Unit]` | 1942 | 1929 | 4 | 9 | 39m 59s 912ms | aborted, no TRX |
| S | `/*/*/(S*)/*[Category=Unit]` | 477 | 475 | 0 | 2 | 2m 06s 619ms | `unit-s.trx` |
| T-Z | `/*/*/(T*)\|(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | 836 | 829 | 2 | 5 | 39m 59s 919ms | aborted, no TRX |

M-R left 94 `RemoteScriptContractTests` cases cancelled. Re-running the 19 methods that owned those cases (`--timeout 40m`) passed 211, failed 0, skipped 0, in 33m 49s 371ms (`unit-remote.trx`). That 211 includes cases of those methods that had already passed inside the 1929, so it is not an extra 211 tests on top of M-R.

T-Z left these classes cancelled: `VolumeRetirementReceiptTests`, `VolumeRetirementWrapperTests`, `WindowsUnitPlatformAdmissionTests`, `TestPhaseSummaryTests`, `TestPhaseSupervisorTests`, `TestTimingLifecycleTests`, `TestTimingRecorderTests`, and `TestClassificationPolicyTests`. The first seven, re-run together, passed 51, skipped 2, failed 0, total 53, in 28m 40s 356ms (`unit-tz-tail.trx`). `TestClassificationPolicyTests` is in the guard union below.

### Skips

All skips that completed are Windows-only, not failures.

- A-B: `The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine`, `Resolves_sibling_flavor_when_configured_one_is_gone`, `V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash`.
- C: `Off_settings_path_round_trips_through_LaunchArgvGuard`, `C1050_Windows_remote_outcomes_are_exact`, `C1050_Windows_rolling_outcomes_are_exact`, `windows_confined_build_leaves_use_app_host_unset`.
- D-L: six `System.IO.Abstractions` drive-letter listing cases, twelve `Unsafe_raw_rules_are_refused_server_side_before_runner_calls` argv cases (CARD-0382), and `tool_copy_removal_retries_while_a_file_is_still_held_open`.
- M-R: four modern/inbox/unknown/silent runner delivery-profile proofs, four `windows_*` adoption cases, and `Windows_probe_reports_a_measured_count`.
- S: `Phone_home_Grok_never_uses_local_modern_evidence` and `Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind` (ConPTY only on Windows).
- T-Z: `Windows_registration_uses_the_wmi_census`, `windows_quick_row_finishes_beside_a_slow_row`, `windows_row_arguments_round_trip_intact`, `windows_chatty_row_drains_interleaved_stdout_and_stderr`, `windows_row_timeout_kills_the_start_b_grandchild`.
- Tail: `C1210_Windows_linux_only_outcomes_are_exact` (CARD-1210) and `C1514T_PS51AndRerun` (CARD-1514 PowerShell 5.1 custody).

### Failures that stay red

Plain filter `/*/*/*/*[Category=Unit]` does not set `ANTIPHON_PLATFORM_FILTER`.

| Test | Expected | Actual |
|---|---|---|
| `PlatformTaxonomyClassFilterTests.Class_shorthand_is_selected_with_the_linux_only_property` | `include` | null |
| `PlatformTaxonomyFilterTests.LinuxOnly_is_a_property_not_a_skip` | `include` | null |
| `PlatformTaxonomyFilterTests.Unannotated_sibling_is_excluded_from_the_linux_only_selection` | `exclude` | null |

Proof lanes, same binary:

| Lane | Filter | Env | Total | Failed |
|---|---|---|---:|---:|
| include | `/*/*/(PlatformTaxonomyFilterTests*)\|(PlatformTaxonomyClassFilterTests*)/*[Platform=LinuxOnly]` | `include` | 2 | 0 |
| exclude | `/*/*/PlatformTaxonomyFilterTests*/*[Platform!=LinuxOnly]` | `exclude` | 1 | 0 |

### Failure that was fixed

`PostLandEarlyWarningTests.Manifest_uses_no_letter_or_and_partitions_unit_classes` failed because `Get-PostLandClasses` stored every class that has a `[Test]`. In `DerivedImpactIndexTests.cs` a raw string declares uncategorized `namespace N; public partial class ProbeTests` with a `[Test]`. A later raw string in the same file is `[Category("Unit")] public sealed class ProbeTests` while the file-scoped namespace is still `N`. The census records only Unit rows, so it kept `N.ProbeTests`. The planner kept the first, uncategorized sighting and dropped the Unit one. Planner Unit keys were 577; the census had 578; the only extra census key was `N` / `ProbeTests`.

`scripts/lib/post-land-early-warning.ps1` now skips a class that is neither Unit nor Integration, and a later Unit sighting replaces a stored non-Unit row of the same key. A local census compare then matched 578 to 578. The one method, re-run `--no-build` against the script on disk, passed in 19s 294ms. The script change does not alter compiled class membership, so `tests/derived-impact-index.json` was not regenerated.

### Failure that did not reproduce

During T-Z, `WorktreeIgnoredContentClassifierTests.Both_classifiers_protect_the_secret_floor_under_a_disposable_override` failed twice with `RegexMatchTimeoutException` from `IgnoredSecretFloor.Matches` (`IgnoredSecretFloor.cs:57`, 1 second match timeout). The arguments were `bin/appsettings.Development.json` and `bin/appsettings.Development.json/token` (TUnit prints `.` as a middle dot in the test name). Host load at the start of that slice was 14.83 / 24.11 / 24.79, and other worktrees were running `Antiphon.Tests` at the same time. Re-run of that method alone: 41 passed, 0 failed, 9s 289ms. The timeout was not widened and the regex was not changed.

## Guards

Nine-class guard union plus `DerivedImpactIndexTests`, not in the same process as `C0824WorkspaceSlotRoleGateTests`: 102 passed, 0 failed, 0 skipped, 14m 21s 002ms. The documented nine-class count without `DerivedImpactIndexTests` is 78 (CARD-1827). `C1514LandingSplitBudgetTests`, which contains `C1584_E2EExpandedBudget`: 5 passed, 0 failed, 58s 652ms.

The script change is not tests-only, so the next stage is review. The task report carries the `git merge-tree --write-tree origin/master HEAD` result.
