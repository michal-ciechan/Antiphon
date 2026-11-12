# Run ae84478d group 131: DerivedImpactIndex + ReleaseGateFollowup (Windows)

Run ae84478d, rc/2026-11-17 = `3db1a43b02246706dc28075fce7e7b5e84804ddb` (= master at the cut),
desktop Windows, chunk `antiphon-all-group-131`, 2026-11-12 00:56:00Z to 01:11:11Z.
Sources, read only: `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\`
`antiphon-all-group-131.trx`, `-tests.log`, `.timing.json`, `-execution\log_261112005556374.diag`.

Chunk totals: 226 results; 217 Passed, 2 Failed, 7 NotExecuted (all 7 are skipped
`VolumeRetirementApplyGateTests` rows, as before).

Both failures are deterministic. Each one asserts on returned state, with no poll, timeout or sharing
violation, and neither overlapped another test (the TRX shows no other result whose window overlaps
either failure). They are two different defects, both new to the rc line at rc/2026-11-16 (present
in rc/2026-11-16 and rc/2026-11-17, absent from rc/2026-11-15 = run 6591e378, which is why C1336 was
green there).

## 1. DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged

This is NOT the CARD-1838 stale-index drift. `committed_index_matches_the_builder` PASSED (7.8 s).
The committed `tests/derived-impact-index.json` matched the builder at the cut.

- Duration 1.949 s, 01:04:33.015Z to 01:04:34.964Z.
- Message:

  ```
  ShouldAssertException: first.Exit
      should be
  0
      but was
  1

  Additional Info:
      The application to execute does not exist: 'C:\Antiphon\releases\checkout\scripts\regenerate-derived-impact-index\bin\Debug\net9.0\Antiphon.DerivedImpactIndex.dll'
  ```
- Expected exit 0, actual 1. Stack top: `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged()`
  `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs:204`, the first `first.Exit.ShouldBe(0, ...)`.
- stdout: only the `ANTIPHON_TEST_PHASE_ID` line. stderr: empty. tests.log lines 4-32 carry the same text.

Cause: the test runs `scripts/regenerate-derived-impact-index.ps1 -Repo <fixture> -NoBuild`, which
runs `dotnet run --project scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj --no-build`.
That driver project (CARD-1838, 4f111d1ed) was not in `Antiphon.sln`. The qualification build is
`dotnet build Antiphon.sln -c Debug` (`scripts/lib/nightly-tests-impl.ps1:1834`), so nothing built the
driver and its DLL never existed. The failure is the same on any host whose only build is the solution
build. It is not a Windows-only builder difference: no line-ending, separator or ordering issue is
involved, and the other 25 siblings passed.

Passing siblings (25): capped_real_paths_keep_the_specific_class, merge_subset_matches_the_hand_selector,
windows_only_classes_stay_out_of_linux_filters, reviewer_census_classes_are_windows_only,
linux_only_methods_stay_off_the_windows_filter, parser_agrees_with_platform_taxonomy_derive,
class_level_linux_only_stays_off_the_windows_filter, override_list_forces_an_undeclared_class_onto_windows,
override_file_names_exist_and_are_undeclared, index_bytes_stay_lf_when_the_newline_source_is_crlf,
has_windows_specifics_stays_on_both_lanes, s1_replay_matches_declaration_and_index_edges,
undeclared_class_with_an_os_token_stays_on_both_lanes, merge_subset_on_a_derived_hit_emits_the_fixed_classes,
**committed_index_matches_the_builder**, merge_process_names_the_regenerate_step,
declaration_diff_selects_the_new_tests_class, type_edge_on_a_fixture_diff_selects_the_lease_class,
one_hop_path_selects_the_temp_family_class, tool_only_diff_selects_the_budget_method,
dotted_script_name_selects_the_lease_class, a_path_with_26_classes_is_capped,
solution_file_selects_no_derived_class, migration_only_diff_keeps_hand_classes_and_the_derive_set,
schema_2_is_still_refused.

These siblings bound the cause. The builder runs in-process and gives the same bytes on Windows,
LF is kept, and the docs check passes. Only the step that needs a separately built executable fails.

Fix (this branch): add `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
to `Antiphon.sln` (Debug and Release, all platforms). That puts it in the build the qualification already runs.
`tests/rc-project-categories.json` does not list this csproj. It was already uncategorised at the cut,
along with 14 other csproj files (pre-existing, fail-safe `unknown-project`). That is not changed here.

## 2. ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity

This is a regression, not a flake or an environment problem.

- Duration 57.596 s, 01:07:04.001Z to 01:08:01.598Z. Sibling `C1336_WrapperRef` PASSED (1.19 s).
- Message: `ShouldAssertException: result.ExitCode should be 0 but was 1`. The harness reports
  `C487: 18 passed, 7 failed, 25 rows` and `C487 HARNESS EXIT CODE: 1`.
- Stack top: `ScriptHarness.Validate` `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25` <-
  `ScriptHarness.RunHarnessCaseAsync` `:18` <- `ScriptHarnessProcess.RunAsync` `:132`/`:193`.
- The rows that failed are `sync-selection`, `hop-child`, `workload-child`, `completion`, `rc-green`,
  `nosync-selection` and `noreport`. Each failing run logs the same first error:

  ```
  [2026-11-12T01:07:35.1456566Z] Exception calling "Invoke" with "5" argument(s): "Unexpected fixture process:
  ...\candidate fixture\checkout\tests\Antiphon.Server.Git.Tests\bin\Debug\net9.0\Antiphon.Server.Git.Tests.exe
  --no-progress --no-ansi --report-trx --report-trx-filename server-git-all.trx ..."
  ```
- The 18 passing rows include omitted, whitespace, full-ref, repeat, explicit, different-pin, foreign,
  missing-sha, short-sha, invalid-sha, bad-ref, legacy, master-root, moved-sync, moved-nosync,
  master-unchanged, help and no-authority. These are all identity and admission rows that stop
  before any suite executable is launched. Only rows that reach the workload fail.

Cause: `tests/fixtures/card1336-release-gate-followups.ps1` wraps the synthetic native I/O seam
in a fail-closed allowlist. Suite executables were admitted by a hand regex:
`^Antiphon\.(Tests|SessionRunner\.Tests|PtyHost\.Tests|Agents\.Pty\.Tests|Messaging\.Tests|Observability\.Tests|E2E|MarkdownPdf\.Tests)\.exe$`.
CARD-1546 (9e7016e2f) registered the `server-git` suite, and CARD-1829 (1b16bcfe7) registered the
`resilience` suite, in `tests/test-execution-policy.json`. The fixture copies that policy into its
clone, so the nightly workload now launches `Antiphon.Server.Git.Tests.exe`, which the regex refuses.
`Antiphon.Resilience.Tests.exe` would be refused next.

Fix (this branch): the fixture now derives the admitted suite executables from the copied policy's
`suites[*].project` and passes that list into the seams as `$script:C1336SuiteExecutables`. Admission
is a case-sensitive exact leaf match. It stays fail-closed: an executable that is not a registered suite
(for example `Antiphon.Server.exe`) is still refused, and the fixture throws if the census lacks
`Antiphon.Tests.exe` or `Antiphon.E2E.exe`. No assertion or row count changed.
Static check at the cut: the derived set is the old eight plus `Antiphon.Resilience.Tests.exe` and
`Antiphon.Server.Git.Tests.exe`. Those two are exactly what the old regex rejects. The fixture parses
with 0 errors and stays ASCII-only. Master since the cut (b02f110fb) has also registered
`Antiphon.GitInProcess.Tests` (CARD-1830, 0686117e5) and still carries the old regex, so master is red
on this row for three suites. The derived list admits all three. The branch merges cleanly with
b02f110fb (`git merge-tree` exit 0).

## Why missed

- **C1336 regex.** The fixture reads the execution policy at runtime. That is a data dependency, and
  the derived impact index cannot see it. The index's only edges to `ReleaseGateFollowupTests` are
  through `ScriptHarness.cs`/`ScriptHarnessProcess.cs`. A diff that touches only
  `tests/test-execution-policy.json`, a new test csproj or `Antiphon.sln` therefore does not select it
  (`solution_file_selects_no_derived_class` pins the .sln part). Neither the CARD-1546 nor the CARD-1829
  Merge guard union named it, so the first run that executed it after the suite registration is this
  rc run. The fix also removes the hand list, so the next suite registration cannot repeat this.
- **Regenerate driver.** CARD-1838's own verification and the Merge path build the driver: the Merge
  step runs the script without `-NoBuild`, and `dotnet build` builds it. The test alone relies on
  `-NoBuild` against an output that only an explicit driver build produces. Neither the CARD-1838
  plan nor Review checked that the solution build the qualification runs produces it.
  The test landed 2026-11-11 01:25Z, and this is its first rc execution.
- **Index drift (the family the brief suspected).** It did not recur. `committed_index_matches_the_builder`
  passed at the cut, so the Merge guard union (DerivedImpactIndexTests + C1584) and the regenerate step
  did their job for every class landed up to 3db1a43b0. There is no "classes landed after the last
  regeneration" miss in this run. The family's red here is the regenerate test's build prerequisite,
  not stale bytes.

## rc action

The rc line cannot be pushed. Both fixes are on `feat/card-task-2d562b37` and must reach master,
then the next cut. Native Windows proof is still owed for both tests:
`ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity` and
`DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged`, after a
`dotnet build Antiphon.sln`. Adding a project to `Antiphon.sln` changes the land-graph input,
so expect the usual land-graph refresh at Merge.
