# CARD-1978 RcBridge Windows placement skip

Code owner: `c099d1cf-69a2-4b12-a935-d88cc5668612`.
Base: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`, current origin/master at start.

Add the existing Windows probe identity to WindowsUnitSkipRoster.WindowsOnly and
route its unchanged off-Windows omission through SkipOnLinux. Preserve native
Windows measurement assertions and the Linux unmeasured-probe assertions. No
production, startup, migration, delivery, lease or persistence behavior changes.

## Verification design

V-1: A Linux receipt containing exactly the RcBridge Windows method skip is accepted
by the real committed-source validator. Extend the existing offline receipt fixture;
commit and run it before the roster fix. Expected red is skip_identity_unlisted.
V-2: Run RcBridgeProbePlatformTests; expect six passing cases and one explicitly
rostered placement skip on Linux. Inspect each fresh TRX identity and its reason.
R-1: Existing receipt controls retain acceptance of C1210 placement and Linux pass,
and rejection of unknown method, Windows host, and unknown host skips.
R-2: WindowsUnitPlatformAdmissionTests, the named classification/selector/pin guard
union, and matching impacted-classes rows retain their invariants. Census source
skip sites and report any broader unrostered sites without silently broadening this
one-card fix. The Windows-only native execution remains unexecuted on this Linux task.
R-3: Recompute all three nightly pins, layering VIOLATIONS=0/LISTED=162,
land-graph -Check, full base..HEAD evidence-diff and conflict-free merge-tree.

Manual M-1: validate real checkpoint provenance using the actual committed SHA;
synthetic fixture CHECKPOINT lines are test input, never actual build evidence.
M-2: rebuild the derived-index driver in native output, regenerate twice (second
unchanged), and commit a changed index last. Inspect the full-range impact selection.
M-3: inspect changed OS branches and callers; test-only changes require no restart.

PC-1 pending SourceLanding Mutation: remove the RcBridge roster entry; the
rc-probe-linux-skip case in CP-1 must refuse with skip_identity_unlisted.
PC-2 pending SourceLanding Mutation: remove the SkipOnLinux call; the roster/source
closure witness must fail. Operator waived mutation execution; neither is passed.

## Performance budget

CP-1 is six offline validator subprocesses with the existing 10-second per-child
deadline, no database, services or compiler outputs. Estimate under one minute;
first-row EstimatedMinutes 15 is the brief's required cold-build allowance, not a
new test timeout or measured p95. Run red once then green once. Preserve per-case
exit results and checkpoint wall/admission timings. No repeated green proof needed.

The reopened task explicitly permits checkpoint-owned bin-<name>/ outputs. Direct
bootstrap builds keep native output. Final rows below share one isolated build;
CP-2 EstimatedMinutes 15 is the existing cold-build allowance (see CARD-1958
Debug 32f24400), not a widened test timeout. No row-timeout override. The slower
derived-index and receipt methods are split into bounded selections. Estimates
are provisional, not measured p95: collect build, slot and execution costs once;
no green repetitions. Read /proc/loadavg before each driver, admit below 16, poll
every 30 seconds, and set ANTIPHON_LINUX_COMPILER_POLICY=0. Retain checkpoint
outputs until receipt inspection, then remove the exact literal owned path and
verify its absence plus clean git status.

### Checkpoints

Run through the checkpoint tool with --expected-source-sha set to committed HEAD,
--serial and --timeout 570s. Wait until terminal completion; never end on exit 75.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | placement-receipt | `pwsh -NoProfile -File scripts/test-checkpoint-placement-skip.ps1 -Label c1978` | V-1,R-1 | six synthetic receipt cases, 0 assertion failures | n/a | 15 | true |
| CP-2 | Final | tests/Antiphon.Tests -> bin-c1978-final-a/ | rc-probe-closure | `/*/*/(RcBridgeProbePlatformTests*)\|(LinuxOnlyExpectedSkipClosureTests*)/*` | V-2,R-2 | 7 pass; 1 rostered Linux placement skip | 7 | 15 | true |
| CP-3 | Final | CP-2 | classification-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2,R-3 | all listed, 0 failed/skipped | 78 | 3 | true |
| CP-4 | Final | CP-2 | platform-admission | `/*/*/WindowsUnitPlatformAdmissionTests*/*` | R-2 | 6 pass; 1 rostered Linux placement skip | 6 | 3 | true |
| CP-5 | Final | CP-2 | derived-builder | `/*/*/DerivedImpactIndexTests/(committed_index_matches_the_builder*)\|(regenerate_script_reports_changed_then_unchanged*)\|(merge_process_names_the_regenerate_step*)\|(solution_build_produces_the_regenerate_driver*)\|(declaration_diff_selects_the_new_tests_class*)\|(type_edge_on_a_fixture_diff_selects_the_lease_class*)\|(one_hop_path_selects_the_temp_family_class*)` | R-2 | all listed, 0 failed/skipped | 7 | 4 | true |
| CP-6 | Final | CP-2 | derived-selectors | `/*/*/DerivedImpactIndexTests/(dotted_script_name_selects_the_lease_class*)\|(a_path_with_26_classes_is_capped*)\|(solution_file_selects_no_derived_class*)\|(migration_only_diff_keeps_hand_classes_and_the_derive_set*)\|(schema_2_is_still_refused*)\|(merge_subset_on_a_derived_hit_emits_the_fixed_classes*)\|(tool_only_diff_selects_the_budget_method*)` | R-2 | all listed, 0 failed/skipped | 7 | 4 | true |
| CP-7 | Final | CP-2 | derived-replay | `/*/*/DerivedImpactIndexTests/s1_replay_matches_declaration_and_index_edges*` | R-2 | all listed, 0 failed/skipped | 1 | 4 | true |
| CP-8 | Final | CP-2 | derived-merge | `/*/*/DerivedImpactIndexTests/merge_subset_matches_the_hand_selector*` | R-2 | all listed, 0 failed/skipped | 1 | 4 | true |
| CP-9 | Final | CP-2 | derived-platform | `/*/*/DerivedImpactIndexTests/(index_bytes_stay_lf_when_the_newline_source_is_crlf*)\|(capped_real_paths_keep_the_specific_class*)\|(windows_only_classes_stay_out_of_linux_filters*)\|(reviewer_census_classes_are_windows_only*)\|(undeclared_class_with_an_os_token_stays_on_both_lanes*)\|(linux_only_methods_stay_off_the_windows_filter*)\|(has_windows_specifics_stays_on_both_lanes*)\|(class_level_linux_only_stays_off_the_windows_filter*)\|(override_list_forces_an_undeclared_class_onto_windows*)\|(override_file_names_exist_and_are_undeclared*)\|(parser_agrees_with_platform_taxonomy_derive*)` | R-2 | all listed, 0 failed/skipped | 11 | 4 | true |
| CP-10 | Final | CP-2 | privacy-callers | `/*/*/(CardFilePrivacyGitTests*)\|(CardFilePrivacyPathTests*)/*` | R-2 | 14 pass; 6 rostered Windows junction skips | 14 | 3 | true |
| CP-11 | Final | CP-2 | receipt-source | `/*/*/RunCheckpointSourceScriptTests/(C1035*)\|(C835*)` | R-2 | all listed, 0 failed/skipped | 6 | 4 | true |
| CP-12 | Final | CP-2 | receipt-allowance | `/*/*/RunCheckpointSourceScriptTests/C1958_Receipt_skip_allowance_is_closed*` | R-2 | all listed, 0 failed/skipped | 19 | 3 | true |
| CP-13 | Final | CP-2 | release-impact | `/*/*/ReleaseImpactEnvelopeTests*/*` | R-2 | all listed, 0 failed/skipped | 11 | 3 | true |

CP-1 is already green on its committed repair. Run CP-2..CP-13 with --after Final
as one serial run, keeping outputs until final inspection. The Unit lane stays
nightly/rc. No full assembly is required: the impact selection is bounded.

## Continuation state

The CP-1 red source is 58bed81e89dc61ae43002f2b136abcb87e7d6eba. Its six fixture
cases have one intended failure: rc-probe-linux-skip expected 0, actual 2,
skip_identity_unlisted. The five existing controls pass. The two-line behavioral
repair is 53e6fc335467a3907f0ebd6d154c5387b4e4ee08; its CP-1 rerun passes all six.
These are synthetic-receipt tests of the real validator, not fresh TUnit evidence.
The original generated reports stay in .antiphon/checkpoints/20261118-074635-8bd7
and .antiphon/checkpoints/20261118-075222-438a respectively.

The full-range selector names CardFilePrivacyGitTests, CardFilePrivacyPathTests,
LinuxOnlyExpectedSkipClosureTests, RcBridgeProbePlatformTests,
RunCheckpointSourceScriptTests, TestLaneCategoryGuardTests, and
WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates.
The regenerated index also selects DerivedImpactIndexTests and
ReleaseImpactEnvelopeTests. No capped or owed classes were reported. The table
covers that full-range selection and the brief's named guard union. Each selected
class is covered in full across bounded rows; inspect fresh per-method counts.

The card-wide Linux SkipTestException source-census witness remains unimplemented.
Existing unrostered off-Windows skips include AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash
and ScriptHarnessSharingTests.Held_reader_accepts_shim_sentinel_append. A global
all-sites-equal-WindowsOnly assertion would therefore be red for more than this
card's RcBridge defect. Do not silently roster those methods or claim the narrow
receipt regression establishes that global invariant. The reopening instruction
explicitly permits omitting the repository-wide runtime census under SHORT when
it requires whole slow classes. That census is omitted, not passed: discovering
all runtime skip sites requires unrelated slow/native classes. The existing roster
closure witness remains included in CP-4. Windows native probe execution remains
nightly/rc placement work; this Linux task proves its declared omission.
