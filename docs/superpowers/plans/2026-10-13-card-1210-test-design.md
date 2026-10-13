# CARD-1210 Windows Unit lane — frozen verification design

Date: 2026-10-13. TestDesign task `e99f75c4`; inspected source and plan base
`2b963fe673de1e3b6d1a578fbfdfb60fc76d4160`. This is a verification addendum to
[the S1–S6 fix plan](2026-10-13-card-1210-windows-unit-lane-plan.md); that document is unchanged.
The checkpoint table here supersedes its proposed verification table. No production/test code,
build, test execution, mutation, operational change, or schedule was performed in TestDesign.
Read-only Roslyn syntax inspection and literal data-source enumeration supplied the counts.
Task authorization was **bound** (`ANTIPHON_TASK_TOKEN` present; value not read into this document).
Triage source: `origin/feat/card-task-8db33c5e`,
`docs/superpowers/reports/2026-10-13-card-1210-windows-unit-triage.md`.
CARD-1210 was read through `scripts/card.ps1 get CARD-1210 -Board Antiphon`.

**Disposition: next `decide` for OD-5 (S5 scope), with S1/S2/S3 independently dispatchable.**
The artifact is complete under the stated defaults; the literal global S5 change is not released.
Its census is 340 classes in 338 files, not about 20, and conflicts with preserved method-level
endurance budgets. The recommended narrower S5 scope below is a proposed operator choice, not
an unannounced rewrite of D-7. S4/S5 also retain the CARD-1208 collision hold. PCs are designed,
not executed: post-land Mutation remains paused by this brief. Review judges their design;
Code executes ordinary V/R. No pending PC is represented as passing.

## Decision audit and binding corrections

| Ref | Finding and verification disposition |
|---|---|
| D-1 | Keep entry `SkipTestException` gate before work and fixture Linux assertions. Convert the four HostJq expression bodies to blocks. Preserve native Linux execution; an unknown prerequisite is a failure, not permission to skip. See B1 and V-1. |
| D-2 | The new roster really is 57 identities / 68 results, with four portable results. The existing C1050 literal roster is **not** the closure of existing gates: Remote has 29 directly gated methods plus seven methods gated through `C1105AuditContract`; Rolling has four. The S1 closure audit needs an independent `Existing` roster and an explicit helper edge; do not edit Remote just to satisfy the audit. See B2. |
| D-3 | There are **19** tracked shell files, not eleven. Preserve repository-wide LF recommendation. The plan's `git rm --cached ... && git checkout -- ...` repair is invalid: after removal from the index plain checkout has no path to restore. On a clean Windows checkout use `git restore --source=HEAD --worktree -- scripts/fixtures/*.sh`; if Git leaves unchanged CRLF files, remove only these clean tracked working files then restore them from HEAD. Do not delete index entries. Assert actual bytes afterwards. |
| D-4 | Split names are new, not present yet. Existing class is 20 methods / 32 results; split gives 21 / 33. A null directory link is failure. File privilege classification is Windows-specific and must distinguish unknown I/O errors. The downstream native open is an independent guard, so test `ValidatePath` directly as well as the public read; otherwise the planned Windows PC can stay green. |
| D-5 | Three names exist. Read-only cause confirmation below resolves the disputed third test. Seven payload helpers are the initial closure, not a complete transitive closure. Include/fail-closed stub their referenced volume-only dependencies and preflight after definitions but before case actions. `C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` also consumes the shared harness, so CP-11/21 include it. |
| D-6 | Both heal names exist. Add isolated, labelled prerequisite cases for `unshare` and `/state` to the existing methods, with healthy companion execution. Remove the fixture's real-root `mkdir -p /state`; absence stays `PREREQ /state`. No host provisioning. |
| D-7 / A-1 | **A-1 resolved from installed package XML**, below. Method fallback is unnecessary at 1.44.0. Scope remains OD-5; a timeout attribute does not establish process cleanup, bound scheduler queue time, or guarantee a complete TRX. |
| D-8 | Keep the native Windows apphost and build stamp. `Antiphon.Tests.exe` existence, assembly hash/MVID, stamp hash and correlated child TRX precede any outer green claim. No Windows `UseAppHost=false`. |
| D-9 | Platform omitted for portable/static work; native Linux fixtures use the default Linux placement. Windows runtime rows require `-Platform Windows`, no fixed runner. Caller re-reads effective settings/runner defaults before dispatch. This document does not infer current capacity from the Plan's snapshot. |
| D-10 | Partition remains an explicit selection, not whole Unit. Import proves syntax only; CP-23 proves the selected class outcomes. CP-24 is currently Category Integration despite being part of this Windows qualification portfolio. Scheduling/spend remains OD-1. |
| D-11 | Keep deadlines/assertions and no retry policy. CP-23 timeout is an unresolved measurement and routes to Investigate with durations/retained artifacts. Import derives 3 × estimate (105 minutes for CP-23 here); estimate is not a 35-minute hard stop. Declare that budget before commissioning; do not silently extend it after red. |
| D-12 | Separate C983 harness hardening remains caller-owned work; no claim here fixes its unbounded driver EOF/exit path. No new card or schedule was written by this design-only task. |

A-1 evidence: `tests/Antiphon.Tests/Antiphon.Tests.csproj` pins TUnit 1.44.0.
Installed `/home/app/.nuget/packages/tunit.core/1.44.0/lib/net9.0/TUnit.Core.xml`,
member `T:TUnit.Core.TimeoutAttribute` (lines 3299–3329), explicitly documents method, class
and assembly application, including method override of the class default. Its adjacent
constructor documentation repeats that contract. The package nuspec identifies repository
commit `42e3be6d99bb637d21e1dac711d76991a99e49c3`. This is pinned local package evidence,
not a test run or a claim about newer TUnit. The brief forbids the Plan's suggested slow probe;
no slow probe was run. If the package changes or that contract cannot be confirmed at Code,
withhold S5 and bind per-method `[Timeout(600_000)]` on the selected spawning methods;
rename the policy test to `Spawning_methods_carry_an_effective_timeout` and revise CP-12/22
before execution. Do not silently substitute that unbound name.

Read-only C849 cause confirmation (no CP-12 TRX accessible from this Linux mirror):
`C849_Seed_refuses_invalid_donors_and_partial_payloads` first validates trees at lines 5553–5578;
`c849_validate_seed_tree` has no payload-selector dependency. Its **second** sub-harness at
5579 extracts `c849_seed` alongside `CacheStatusHarness`, which defines status helpers only.
`c849_seed` calls missing `c849_payload_select` before the donor-idle refusal. The subshell's
`2>&1` puts the shell's `c849_payload_select: command not found` diagnostic before
`seed-result=false:CacheDonorNotIdleDrained` in `$root/result`; the method's exact contiguous
`sessions verdict=seed-result=false:CacheDonorNotIdleDrained` assertion therefore cannot match.
This confirms the triage's gate-harness cause from source, not from retained failure text.
The later cleanup sub-harness independently extracts `c849_seed_failure` without
`c849_payload_root`/`c849_payload_fs`, so it would retain the stage and fail `stage-cleaned`.
Repair both; fixing the tree validator alone addresses neither. The saved-donor Docker fake
also recognizes obsolete `cp -a /seed/.` whereas production now passes the source positionally
as `cp -a "$1/." ... sh "$seed_path"`; align the fake's exact argv handler and assert it is
reached before trusting payload-imported/ownership assertions. No production script edit is
part of S4.

## Bound inventory

Paths below are relative to `tests/Antiphon.Tests/` unless prefixed otherwise. `NEW` means
absent at the inspected commit and is a Code delivery obligation, **binding required after
Code**, not an existing passing method. Existing spellings were checked against source bodies.
Helpers are not TUnit executions. Existing plan PC-1's `C1025_Check_has_no_install_effects`,
PC-2's `Qualification_grades_the_jq_row_for_both_targets`, and PC-13's
`Entrypoint_heal_chowns_the_tmp_directory_once` exist with those exact spellings.

| Binding | File / exact method(s) | Current binding / post-Code count |
|---|---|---|
| B1 | `Scripts/WindowsUnitPlatformAdmissionTests.cs`: `C1210_Guard_matches_native_platform`; `C1210_Native_linux_entries_guard_before_work`; `C1210_Roster_closure_matches_source_gates`; `C1210_Audit_rejects_empty_or_incomplete_outcomes`; `C1210_Windows_linux_only_outcomes_are_exact` | NEW file and five methods. Four portable tests, one Windows child audit. Use internal case loops, not Arguments, so counts stay 4 / 5. |
| B2 | `Scripts/WindowsUnitSkipRoster.cs`; `TestHelpers/NativeLinuxGate.cs` | NEW helpers, zero test executions. Literal rosters below. S1 initially audits four classes; S3 extends the same audit's child filter to storage. |
| B3 | `Infrastructure/ShellFixtureLineEndingContractTests.cs`: `Shell_scripts_are_forced_lf_by_gitattributes`; `Shell_scripts_have_no_carriage_returns` | NEW file and two methods. Nearest fixture is `SessionRunnerGrokLineEndingContractTests.cs`; its existing method is `Session_runner_grok_shell_and_mjs_are_forced_lf`, not either proposed name. |
| B4 | `Application/ChannelOutboundStorageTests.cs`: `C1059_Source_reads_reject_directory_links`; `C1059_Source_reads_reject_file_links` | NEW split names; replace existing `C1059_Source_reads_reject_file_and_directory_links`. Retired name must disappear from filter/roster references. Class grows from 32 to 33 results. |
| B5 | Same file: `Output_rename_and_link_swap_after_inspection_seals_the_opened_file`; `TestHelpers/FileLink.cs` | Method exists, helper NEW. Preserve its barrier and byte-equality assertions; add a labelled Should.NotThrowAsync around sealing so the retained-handle mutation fails at a test assertion; probe privilege before staging. |
| B6 | `Scripts/RemoteScriptContractTests.cs`: `C849_Saved_donor_archive_is_checked_and_imported_without_a_container`; `C849_Seed_refuses_invalid_donors_and_partial_payloads`; `C849_Seed_publishes_complete_payloads_before_its_marker`; `C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | Existing, one result each. First three are triage repair identities; fourth is shared-helper regression. |
| B7 | Same file: `C849_Closure_preflight_names_a_missing_dependency`; `C849PayloadClosure()` | NEW test and helper. One TUnit result containing healthy/missing-dependency vectors. `CacheSeedTreeHarness`, `CacheStatusHarness`, `C1066FixtureHarness`, `Block`, `Order`, `LinuxShell` exist. |
| B8 | `Scripts/ProcessSpawnTimeoutPolicyTests.cs`: `Spawning_classes_carry_a_class_timeout`; `Spawn_timeouts_do_not_exceed_ten_minutes` | NEW file and two methods; binding required after OD-5 and Code. Nearest source-policy fixture read: `ProcessSpawnLimitTests.cs`; nearest Roslyn fixture: B1's C1050 model. |
| B9 | `ProcessSpawnLimitTests.cs`: `Caps_concurrent_process_spawning_tests_at_one`; `Process_spawning_classes_carry_the_limiter`; `Live_runner_send_input_method_carries_the_limiter` | Existing three results. Plan CP-13's floor 1 becomes 3. |
| B10 | `docs/superpowers/plans/2026-10-13-card-1210-windows-unit-partition.md` | NEW S6 artifact, not a test method. `PlanTableImporter.ImportFile` exists; CP-25 binds its CLI import command. |

### Literal native skip roster (S1)

Each line is `method | expected results`. The namespace is the file's namespace; join
namespace + class + method, not display name. Reasons are independent literals in the audit,
not read back from the constants under test. `H`, `J`, `G`, `C` below are reason keys:

- H: `CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly.`
- J: `CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly.`
- G: `CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly.`
- C: `CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state`

`Scripts/HostJqPrerequisiteScriptTests.cs`, class `HostJqPrerequisiteScriptTests`, H:

```text
C1058_Open_descriptor_must_match_canonical_leaf | 1
C1058_Digest_is_bound_to_open_descriptor | 1
C1058_Installed_pin_is_checked_before_execution | 1
C1058_Version_probe_uses_open_descriptor | 1
C1058_True_probe_uses_open_descriptor | 1
C1058_False_probe_uses_open_descriptor | 1
C1058_Receipt_formatter_uses_open_descriptor | 1
C1058_Changed_leaf_cannot_emit_success_proof | 1
C1058_Pre_execution_link_checks_are_independent | 1
C1058_Identity_comparisons_include_device | 1
C1058_Final_link_checks_are_independent | 1
C1058_Final_permission_checks_are_independent | 1
C1058_Proof_is_buffered_until_final_admission | 1
C1058_Metadata_syntax_refuses | 1
C1058_Observation_failures_are_invalid | 1
C1058_Check_rejects_canonical_hardlink | 1
C1058_Provision_removes_only_owned_stage_link | 1
C1058_Staged_unlink_failure_refuses | 1
C1025_Check_qualifies_deployment_shell | 1
C1025_Check_rejects_canonical_leaf_symlink | 1
C1025_Receipt_rejects_canonical_lookup_with_unapproved_target | 1
C1025_Check_has_no_install_effects | 1
C1025_Provision_requires_missing_jq | 1
C1025_Provision_admits_host_prerequisites | 1
C1025_Provision_refuses_unsafe_destination | 1
C1025_Provision_serializes_and_rechecks | 1
C1025_Provision_verifies_download_before_use | 1
C1025_Provision_publishes_complete_no_clobber | 1
C1025_Provision_cleans_only_owned_staging | 1
C1025_Provision_requalifies_published_jq | 1
C1025_Provision_requires_canonical_source | 1
C1025_Transport_is_bounded_and_reaped | 1
C1025_Receipt_requires_complete_current_proof | 1
C1025_Receipt_persistence_is_required | 1
C1025_Transport_preserves_secret_custody | 1
C1025_Explicit_tool_phases_never_deploy | 1
C1025_Preflight_precedes_every_phase | 1
C1025_Preflight_is_fresh_for_each_entry | 1
C1025_Final_recycle_guard_remains | 1
```

`Infrastructure/JqRunnerImageContractTests.cs`, class `JqRunnerImageContractTests`, J:

```text
C1054_Jq_row_rejects_home_shadow | 1
C1054_Jq_row_accepts_canonical_file_and_alias | 1
C1054_Jq_row_rejects_canonical_leaf_symlink | 1
C1054_Jq_row_records_found_and_resolved_paths | 1
Version_row_accepts_only_exact_successful_pin_without_stderr | 12
```

`Infrastructure/GrokSha256PinTests.cs`, class `GrokSha256PinTests`, G:

```text
Runner_grok_sha_rejects_multiline_pin | 1
Runner_grok_sha_rejects_trailing_newline | 1
Runner_grok_sha_rejects_devnull_second_line | 1
Runner_grok_sha_rejects_embedded_newline | 1
Runner_grok_sha_rejects_uppercase_pin | 1
Runner_grok_sha_rejects_short_pin | 1
Runner_grok_sha_rejects_long_pin | 1
Runner_grok_sha_rejects_empty_pin | 1
Runner_grok_sha_rejects_tampered_file | 1
Runner_grok_sha_rejects_empty_download | 1
Runner_grok_sha_accepts_matching_file | 1
```

`Infrastructure/CodexRootExecContractTests.cs`, class `CodexRootExecContractTests`, C:

```text
Entrypoint_heal_chowns_the_tmp_directory_once | 1
Entrypoint_heal_never_names_a_path_outside_tmp | 1
```

Totals: 39 + 5 + 11 + 2 = **57 identities**, 39 + 16 + 11 + 2 = **68 results**.
The four portable identities (one result each) are Jq's
`Pinned_download_is_verified_before_root_owned_install_in_every_runner_target`,
`Qualification_grades_the_jq_row_for_both_targets`, and CodexRoot's
`Entrypoint_heals_only_the_codex_tmp_directory`, `Provider_exec_into_the_runner_names_uid_1654`.
CP-14's S1 child therefore has total 72, executed/passed 4, failed 0, notExecuted 68.

S3 Conditional list is exactly B4's file-link test and B5's rename test (one result each).
Freeze `FileLink.PrivilegeReason` as
`CARD-1210: file-link controls require symbolic-link creation privilege on Windows; capability unavailable.`
A storage child has 33 total, 31–33 Passed and 0–2 NotExecuted, with that exact result-local
reason on only those two identities. Combined S3 child: total 105, Passed/executed 35–37,
NotExecuted 68–70, Failed 0. A failed directory-link setup is a failure, not a conditional skip.
S1's roster file reserves an empty Conditional list; S3 fills it and extends the child filter.
S3 lands after S1 even when authoring proceeds in parallel.

### Existing gate closure and CP-23 enumeration

C1050's 12 Remote / 4 Rolling host entries remain unchanged. S1's new closure validator must
account for additional existing uses, without treating them as new platform policy. The full
Remote literal host roster is below. The seven `C1105_Git_audit_*` methods marked `helper`
enter `C1105AuditContract` before fixture work; that helper's first statement is the gate.
That is an explicit allowed edge, not an exemption for arbitrary indirect gates.

```text
C1087_Host_census_filters_and_names_cause | 1 | direct
C1008_Recycle_exact_default_volumes | 1 | direct
C1008_Recycle_refuses_references_and_unknown_census | 1 | direct
C1008_Recycle_audits_work_as_1654 | 1 | direct
C1008_Recycle_refuses_unpublished_and_dirty_work | 1 | direct
C1008_Recycle_refuses_uninspectable_git | 1 | direct
C1008_Recycle_audits_promisor_checkout | 1 | direct
C1105_Git_audit_volume_scale | 1 | direct
C1105_Jq_argv_limit | 3 | direct
C1008_Recycle_preserves_tmp_copyup | 1 | direct
C1105_Verify_tmp_accepts_an_absent_pty_host | 10 | direct
C1008_Recycle_resume_requires_matching_receipt | 1 | direct
C1008_Recycle_receipt_records_disk_and_partial_failure | 1 | direct
C1008_Retire_temp_rechecks_absence_and_retirement | 1 | direct
C1008_Retire_temp_reclaims_below_cache_disk_gate | 1 | direct
C1008_Recycle_dry_run_never_mutates | 1 | direct
C1105_Redeploy_accepts_previous_generation_and_requires_new_bind | 1 | direct
C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout | 1 | direct
C1105_V218_dry_run_reaches_the_generation_line | 1 | direct
C1105_Relative_dry_run_refuses_before_removal | 1 | direct
C1105_Generation_identity_refuses_mismatch | 1 | direct
C1105_Main_state_init_without_session_runner_refuses | 1 | direct
C1105_Compose_cmp_refuses_before_removal | 1 | direct
C1105_Resume_identity_refusals | 1 | direct
C1105_Resume_state_init_image_follows_the_journal | 1 | direct
C1105_Resume_foreign_leftover_is_not_an_image_mismatch | 1 | direct
C1105_Resume_digest_pins_previous_sha12 | 4 | direct
C1105_Resume_post_removal_census_tracks_open_work | 12 | direct
C849_Deploy_prepares_and_verifies_before_acceptance | 1 | direct
C1105_Git_audit_recovery_tips | 10 | helper
C1105_Git_audit_seed_content | 11 | helper
C1105_Git_audit_index_content | 5 | helper
C1105_Git_audit_object_completeness | 4 | helper
C1105_Git_audit_overall_timeout | 1 | helper
C1105_Git_audit_resume_preserves_proof | 1 | helper
C1105_Git_audit_hidden_locations | 132 | helper
```

Rolling's four existing entries are `C1008_Option_manifest_is_strict`,
`C1008_Documentation_and_transport_pins_match`, `C1008_Refusal_receipts_do_not_leak_secrets`,
`C1008_Retired_absent_null_is_accepted`, one each. They use the unchanged CARD-1050 reason.
The closure checker also explicitly recognizes the gate wrapper's implementation call and
C1050's `C1050_Guard_matches_native_platform` probe call. Test literals/comments are syntax
trivia or strings, not invocation nodes. Outside those named implementation/probe sites,
unrecognized gate calls fail the closure assertion.

Additional existing direct gate callsites found by the global closure scan (all namespace
Antiphon.Tests.Scripts, one result each) are also literal Existing entries:

`RollingVolumeRecycleCensusChurnTests.cs`: `C1099_Fixture_census_skips_a_vanished_foreign_container`.
`RetiredTempContainerHostTests.cs`: `C1105_Temp_cleanup_uses_previous_generation`.
`RollingProductionMountTests.cs`: `C1105_Previous_generation_roster_is_derived_from_its_compose`,
`C1105_V218_compose_model_accepts_the_host_shape`,
`C1105_V218_previous_model_accepts_the_host_shape`, `C1105_Relative_secret_target_is_refused`,
`C1105_Relative_secret_target_equal_to_its_name_is_accepted`,
`C1105_Absolute_secret_target_keeps_the_base_rule`, `C1105_Malformed_secret_target_is_refused`.
Their gate callsites/fixture boundaries were read for the closure inventory. Their unchanged
product assertions are outside the new runtime roster: these nine are source-closure accounting,
not additional executions in CP-23. Runtime qualification of those classes is not claimed.

Current Remote: 178 Test methods, 125 Arguments on 22 methods (103 extra executions), and
one MethodDataSource replacing a single execution with 132. Thus **178 + 103 + 131 = 412**
results. `C1105HiddenLocations` applies `(?m)^    '([a-z0-9-]+)': \[` between the two
HIDDEN-LOCATIONS markers in `scripts/fixtures/c1105-git-audit.cjs`; the read-only enumeration
found 132, including the current additional location vectors. Direct gated results are 54;
helper-gated results are 164; Windows skips **218 results across 36 methods**.
After B7 adds one ordinary test: **413 total = 195 Passed + 218 NotExecuted** on a qualified
Windows host. CP-23 floor is 195, not 166. No other platform/capability skip is accepted there.
An additional source/data-source row at Code is binding drift requiring a table amendment,
not permission to retain these counts and call the run complete.

## Slice dispatch and operator choices

| Slice | Source area / collision gate / commissioning order |
|---|---|
| S1 | B1/B2, the four native classes, and `Scripts/RollingVolumeRecycleScriptTests.cs` gate delegation. First Code slice; CP-1–6 on native Linux, CP-14–16 on Windows. Additional existing Remote gates are read by the new audit, not edited. |
| S2 | `.gitattributes`, B3, `docs/testing-and-build.md`. Independent authoring/Code in parallel with S1/S3. CP-7–9 and CP-17–19. No shell-content edits or fixture byte normalization in the runner. |
| S3 | B4/B5 and B2 Conditional entries plus the existing B1 child audit's storage selection. Can author in parallel; merge/land S1 first and run integrated CP-20 after that. Reserve the two shared roster/audit files: do not concurrently land edits to them. CP-10/20. |
| S4 | `Scripts/RemoteScriptContractTests.cs` and its extracted closures. **Wait for CARD-1208** on this file and `scripts/c590-remote.sh` to land/settle. Rebind function references at that tip before authoring. CP-11/21. |
| S5 | B8 and limiter-bearing test classes selected by OD-5. Wait for S4, CARD-1208, and any S1/S3 edits to those classes. Literal D-7 touches 340 classes / 338 files across Agents 21, AgentTui 1, Application 200, Checkpoints 15, Infrastructure 34, Scripts 64, TestHelpers 5. No blanket dispatch under an "about 20 files" scope. CP-12/13/22 are metadata-policy checks, not behavior qualification of those 340 classes. |
| S6 | B10, `docs/testing-and-build.md`, and one new selection-validation test in `Checkpoints/CheckpointImportTests.cs` (B11 below). After S1–S5, CP-23–25. Caller proposes/edits CARD-1114 and schedules only with OD-1. |

B11 (NEW, binding required after Code):
`tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs`,
`C1210_Windows_partition_selection_is_explicit`. One portable Unit test importing the actual
partition with `isWindows: true`, then checking a hand-written expected selection, counts,
unique IDs/build outputs and allowed Windows build properties, using the real
`BuildStep.PropertyArguments(manifest.Build.Properties, isWindows: true)` to inspect resolved argv. This replaces the Plan's
CP-25 bare CLI import: importer exit zero alone does not prove a safe/exact portfolio.
The S6 change is an added verification test, not a change to the fix architecture.

- **OD-1:** Plan recommends weekly CARD-1114 scheduled card action with `acceptSpend`, Windows
  placement, checkpoint partition. Alternative is nightly policy integration. No spend scheduled here.
- **OD-2:** Plan recommends ten-minute per-test default over five minutes. A class annotation sets
  an individual-test default, not an aggregate class budget. Current child bounds remain unchanged.
- **OD-3:** Plan recommends repo-wide `*.sh text eol=lf`, retained by this design.
- **OD-4:** Plan recommends S1/S2/S3 parallel authoring, subject to live effective caps and the S1/S3
  shared-file landing gate. Standing six-in-flight authority covers queueing, not scope expansion.
- **OD-5 (new, human choice):** choose the S5 policy scope. Recommendation: the **11 named classes
  in this portfolio** — HostJqPrerequisiteScriptTests, JqRunnerImageContractTests, GrokSha256PinTests,
  CodexRootExecContractTests, CodexRunnerImageContractTests, RollingVolumeRecycleScriptTests,
  C1008PlatformContractTests, WindowsUnitPlatformAdmissionTests, ShellFixtureLineEndingContractTests,
  ChannelOutboundStorageTests, RemoteScriptContractTests — with a literal scope in B8, preserving
  unrelated tests. Alternative: commission a separate repository-wide policy plan with a reviewed
  exception roster and behavior checkpoints. Literal D-7 would add defaults to 340 current classes;
  `CodexBootWedgeProbeTests` has two 3,600,000 ms overrides and
  `GrokRulesCompactionAcceptanceTests` has two 7,260,000 ms overrides. Preserving them and
  asserting a global ten-minute effective ceiling are incompatible. Even Remote's
  `C1105_Git_audit_volume_scale` has an existing 12-minute child allowance: do not represent the
  proposed ten-minute outer default as a validated Linux behavior bound. OD-5 must be recorded
  before CP-12/22 can certify the chosen scope. Other slices can proceed with their own rows.

## Verification design

### Inspection

- Read the plan, card, triage report, testing/checkpoint/mutation owners, project conventions and
  stage-report owner. Read `BuildStep.PropertyArguments`, `PlanTableImporter.ImportMarkdown`/roster extraction and
  `CheckpointImportTests` import/schema examples; read the skip handling contract in
  `run-checkpoint.ps1` and the testing owner. Boundaries: selection/schema/provenance → V-10/R-4.
- Read C1008PlatformContractTests in full (gate, Roslyn entry checks, child launcher,
  TRX correlation, negative fixtures), C1008HostFixture.RequireNativeLinux and its fixture setup,
  HostJq test bodies including expression-bodied probes and HostJq/C1025 prerequisite/process
  helpers, Jq and Grok SHA test/fixture bodies, CodexRoot heal and shell-builder bodies.
  Boundaries: admission, before-fixture ordering, outcome identity/reason → V-1/2/3, R-2.
- Read .gitattributes and SessionRunnerGrokLineEndingContractTests in full; enumerate tracked
  shell paths; read Codex image consumers and Remote C1192/C913 dispatch/receipt assertions and
  LinuxShell file-vs-stdin behavior. Boundary: disk bytes and consuming process → V-4/5.
- Read the storage link/read/rename tests, DirectoryLink/TryCreate/Complete/Dispose,
  DirectoryLinkCommand and nearest DirectoryLinkFixtureTests, ChannelReplyAttachmentReader's
  ValidatePath and both native opens, ChannelOutboundFileStore's retained-reader copy.
  Boundaries: capability vs refusal vs sealed bytes → V-6, R-5. Other storage tests are unchanged
  regression scope, not newly claimed transport-delivery proof.
- Read the four C849 consumer bodies, CacheSeedTreeHarness, CacheStatusHarness,
  C1066FixtureHarness and its caller, Block/Order/LinuxShell, payload controller/select/root/
  note/fs/call, seed/failure/tree validators, publication and the stale saved-copy Docker handler.
  Read C973/C983 methods and their Linux-pwsh prerequisites. Boundaries: closure availability,
  cleanup and publication order → V-7/8. CARD-1208's future bodies remain binding required.
- Read ProcessSpawnLimitTests and ProcessSpawnLimit; inspect pinned TUnit XML/nuspec and use
  Roslyn to census class/method limiter and Timeout attributes. Read the existing long-budget
  annotations and gating bodies. Boundary: policy metadata → V-9/R-1; aggregate queue/process
  completion is excluded. The 338-file literal S5 behavior surface is not claimed inspected or
  qualified; OD-5 prevents releasing that scope without a new plan.
- Read the native Windows argv test's launch/parser fixture and seven test identities.
  Boundary: argv round trip → R-3; actual session input receipt is excluded.

### Delivery inventory

No new/changed asynchronous product delivery path exists in S1–S6. S3 invokes existing storage
and attachment-reader APIs; it does not change a producer, queue, persistence/recovery state,
recipient, or acknowledgement. There is consequently no new durable delivery identity or queue
handoff to qualify. No outbound-message, deployment, or session-delivery claim is made here.

The new **test-evidence** path is enumerated separately: outer audit → already-built native
child apphost → child raw TRX persisted in a fresh evidence directory → outer Audit joined by
TRX `testId` to exactly one definition/class/method, plus assembly MVID/hash and source-stamp
hash. A crash, missing TRX, nonzero exit, mismatch or ambiguous result withholds qualification;
there is no retry/recovery that converts it to pass. Parent PID/start metadata is diagnostic;
root exit or a CHECKPOINT banner is not sufficient. CP-14/20 require complete child results.
Guards G-16–25, G-38–40 and G-43–46 cover this evidence seam.

Substitutes: synthetic XDocument vectors prove audit rejection, not child execution; Roslyn
proves source shape, not runtime scheduling; C849 shell/Docker doubles prove extracted control
flow and private filesystem observations, not deployment or container transport; sealed reply
bytes prove local storage, not channel receipt; CommandLineToArgvW proves argv, not UserPrompt.
A later product-delivery change must add the real queue producer-to-recipient cases (busy and
already-eligible recipients, enqueue/crash recovery at each persistence handoff, complete matching
UserPrompt transcript for session input). None of the substitutes or enqueue/Sent/ack facts here
can satisfy that requirement.

### Proves it works now

All entries are **Code obligations**, not TestDesign run results. Shared recipe: commit first,
use the closed CP table through the checkpoint tool and build-slot gate, retain fresh TRX/source
receipts, compare actual identities/counts to this document, and await owned work to completion.
Case loops have hand-written expected outcomes; expected values must not be computed with the
predicate under test. A mutation must reach the specified assertion; fixture/compile errors and
zero execution do not count as PC red.

| ID | Behavior / layer / recipe and variants | Oracle, reachability, classification | Rows |
|---|---|---|---|
| V-1 | Native gate + original bodies; B1 guard/first-statement methods. Native Linux and non-Linux; H/J/G/C and retained C1050 constants. | Compare constants to independent literals even on Linux; Linux guard returns, non-Linux exact SkipTestException. Roslyn finds each identity once and first statement is Require. Fixture work comes after it; native rows still execute original assertions. Unit source/runtime admission. | CP-1–6,14–16 |
| V-2 | B1 closure and audit methods. Closed source roster both ways; S1 four-class child, S3 storage extension. | Complete correlated TRX multisets and counter equality. In-memory healthy document first; one-field corruptions below must fail at named label. Child apphost uses same build; no discovering expected roster from actual results. Unit evidence integration. | CP-5,14,20 |
| V-3 | C1050 gate delegation and unchanged exact reason. | Existing `C1050_Guard_matches_native_platform`, both source-entry tests and negative Audit method pass; native Windows two child audits pass. Their child rosters are intentionally narrower than the complete legacy inventory. | CP-6,16 |
| V-4 | B3 methods using tracked shell census, active/effective attribute evaluation and raw File.ReadAllBytes. | Literal `*.sh text eol=lf` present; each tracked shell effective eol is lf; no byte 13. Require a nonempty census including three formerly CRLF fixtures. Git `ls-files -z` is the tracked census and
`check-attr` corroborates effective working-tree attributes; any subprocess is limited, bounded,
and joined. Table cases: commented rule, missing rule, later crlf/-eol override, healthy LF, injected CRLF. Fail names file/rule and clean re-checkout remedy. Unit filesystem contract. | CP-7,17 |
| V-5 | Existing CodexRunnerImageContractTests; Linux CP-9 selects 35 of the original 40 Remote fixture consumers under the explicit S2 exclusion decisions below; Windows CP-19 retains all 40 pending Review. | Named PASS/exit and absence-of-failure assertions reached with actual on-disk fixtures; WSL bash/pwsh/jq required on Windows. Any prerequisite skip refuses this row's qualification. Five named base-red methods remain caller-owned follow-up work. Behavioral shell fixture; no deployment proof. | CP-8,9,18,19 |
| V-6 | Split B4 and changed B5; real directory junction/symlink and file symlink. | Real non-link companion reads exact bytes; `ValidatePath` link rejection message then public API rejection. Probe null is allowed only for Windows privilege classification. Directory test also runs FileLink injected create-error table: Win32 1314/Unauthorized on Windows → unavailable, Linux same errors/Windows other IOException → original exception, successful link → observed ReparsePoint. File-link test requires non-null on Linux before optional Windows skip. Unit filesystem/native behavior. | CP-10,20 |
| V-7 | B6/B7 shell extraction with shared closure; healthy host, missing select/root/fs/call/note, malformed tree, busy/unknown donor, cleanup failure, valid saved archive, failed smoke. | Preflight sees definitions, healthy host is entered and volume-only stubs have zero calls; missing name yields exactly `MISSING <name>`, actual child exit 97, case-body sentinel absent. Refusal/cleanup/import bytes and source-order predicates below. Unit shell harness. | CP-11,21 |
| V-8 | Existing `C983_Required_jq_driver_rejects_successful_false_probe` and six C973 cold-marker cases. | Preserve internal case assertions; record method durations and owned child completion. A passing sample establishes only selected cases. Existing bounds unchanged. | CP-11,21 |
| V-9 | B8 source-policy methods; scope fixed only after OD-5. | Roslyn resolves selected class and method attributes (including constant expressions); require class default, positive value ≤600000, preserve existing shorter method override, reject missing/unresolved attributes. Internal syntax vectors exercise absent/class/method/override/above-ceiling independently. Static Unit policy, not runtime kill/queue proof. | CP-12,22 |
| V-10 | S6 partition import/selection test B11, real Windows Remote class and argv selection. | B11 imports actual artifact then compares literal row/filter/count/build-property inventory; actual CP-23 complete 413-result TRX has 195 pass/218 rostered skips, no failures. Record measurement timeout as failure rather than widening budget. | CP-23–25 |

B1 synthetic audit recipe: start with a hand-written valid complete document for S1 and S3
(Passed and privilege-skipped variants). Each vector changes exactly one fact. Use labels
`c1210-trx-root`, `c1210-definition`, `c1210-method`, `c1210-host-roster`,
`c1210-result-reason`, `c1210-portable-roster`, `c1210-conditional-roster`,
`c1210-counters`, `c1210-child-exit`. Cases: wrong root; missing/duplicate definition;
unknown result testId; missing TestMethod; wrong namespace/name; absent/duplicate host result;
host Passed; missing/empty/wrong/duplicate result-local reason; missing/failed/skipped portable;
conditional failed or unrelated identity skipped; each counter independently wrong; nonzero exit.
Perform root/join/method checks before roster/reason, then counters, then child exit. Negative
fixture verifies a ShouldAssertException containing the intended label, not any exception.
All these vectors stay inside one method (one execution).

B7 recipe: select host deliberately, initialize CASE_DIR/SERVER2_ROOT/RUN in private scratch,
load extracted real functions plus declared test doubles, compute the referenced `c849_*` set
from those extracted bodies, and `declare -F` each before the case body. Include the seven
D-5 payload helpers. `c849_fixture_work_mount`, `c849_fixture_helper_exec` and volume-only
helpers referenced by `declare -f` must have declared fail-closed doubles or real definitions;
unknown dependencies exit 97. Do not make missing names invisible by pruning to current
healthy call paths. Assert host backend and zero volume-helper calls. Preserve LinuxShell's
existing API; a local child wrapper records the actual preflight exit, since LinuxShell itself
returns only stdout and discards its process exit code. Healthy control precedes each
one-dependency removal. This dedicated assertion is what makes a missing-closure PC red,
instead of accepting a command-not-found fixture crash as mutation evidence.

B6 publication contract explicitly requires presence/uniqueness and order of:
`docker stop "$donor"` before donor copy (retained), smoke before
`c849_payload_fs writer 'mv "$1" "$2"' bash "$stage" "$recovery"`, that recovery move before
`docker start "$donor"`, and restart before `mv -T -- "$C849_READY.tmp-$RUN" "$C849_READY"`.
The source-order assertions are separate labelled checks; a missing anchor fails, not index -1
accidentally passing. The saved-import test's filesystem bytes/recovery/marker assertions remain
behavioral companions. The malformed-tree test keeps independent handwritten diagnoses and
sibling sentinel bytes.

D-6 prerequisite recipes are added inside the two existing heal tests without creating extra
TUnit cases: controlled shell lookup for unavailable unshare and a substituted nonexistent
scratch `/state` target must emit `PREREQ unshare` / `PREREQ /state`, nonzero and zero heal calls.
Healthy real unshare/mount then reaches the existing chown/retained-auth assertions. A failure of
the real prerequisite withholds the Linux row; it is not a skip or an intended mutation red.
New deadlines use CancellationTokenSource/Stopwatch or TimeProvider monotonic timestamps;
UTC is metadata only. No new DateTime-based polling loop.

### Guards the regression

- R-1: limiter capacity and application remain intact | `ProcessSpawnLimitTests`' three named
  methods in B9; capacity equals 1 and both class/method attributes remain present | CP-13.
- R-2: C1050 child audits still execute with native Windows apphost | six exact outer results
  and their independently correlated child outcome/reason multisets | CP-16.
- R-3: argv transport spelling | seven existing `DelegateLaunchArgvIntegrityTests` methods;
  character/length/index equality through real Win32 parser | CP-24. No session receipt claim.
- R-4: selection cannot silently expand to Unit/assembly or hide skipped-only rows | B11 imports
  real partition and checks explicit portfolio, counts and apphost properties | CP-25.
- R-5: shared storage and C849 helper consumers | storage's 33 results and the fourth B6 method
  retain their existing decisive assertions; no swallowed link/closure failures | CP-10/11/20/21.

### Guard inventory

Only guards changed by this plan, introduced by its tests, or relied on for its safety-critical
qualification are inventoried. Existing HostJq/cache/storage product guards whose assertions and
behavior are unchanged remain regression coverage; this addendum does not recommission their
historical mutation portfolios. Independently bypassable checks below have distinct PCs.

| Guard | Plan reference + invariant | Control |
|---|---|---|
| G-1 | D-1: roster method gates before fixture work | PC-1 |
| G-2 | D-2: extra source gate cannot escape literal roster | PC-2 |
| G-3 | D-1: class reason agrees with independent literal | PC-3 |
| G-4 | D-1/2: non-Linux gate produces skip, not Passed | PC-4 |
| G-5 | D-3: repo-wide active LF rule exists | PC-5 |
| G-6 | D-3: tracked shell working bytes have no CR | PC-6 |
| G-7 | D-4: ancestor directory reparse path rejected before open | PC-7 |
| G-8 | D-4: leaf file reparse path rejected before open | PC-8 |
| G-9 | D-5: healthy extracted closure defines its dependencies | PC-9 |
| G-10 | D-5: smoke precedes recovery publication | PC-10 |
| G-11 | D-7: selected spawning class has timeout default | PC-11 |
| G-12 | D-7: selected effective timeout is positive and ≤600000 | PC-12 |
| G-13 | D-6: unshare unavailable refuses before heal effects | PC-13 |
| G-14 | D-6: missing state prerequisite refuses before heal effects | PC-14 |
| G-15 | D-2: roster entries cannot outlive their source gate/recognized helper edge | PC-15 |
| G-16 | D-2: namespace/class/method identity bound exactly | PC-16 |
| G-17 | D-2: each result has exactly one definition joined by testId | PC-17 |
| G-18 | D-2: joined definition contains one complete TestMethod | PC-18 |
| G-19 | D-2: host result multiset complete with exact multiplicity | PC-19 |
| G-20 | D-2: skip reason is exact and result-local | PC-20 |
| G-21 | D-2: portable multiset complete and Passed | PC-21 |
| G-22 | D-4: Conditional outcomes admit only named capability skips or Passed | PC-22 |
| G-23 | D-2: counters agree with correlated result totals | PC-23 |
| G-24 | D-2: child exit succeeds after result validation | PC-24 |
| G-25 | D-2: document is complete TRX TestRun in expected namespace | PC-25 |
| G-26 | D-4: capability exception whitelist cannot swallow unrelated I/O | PC-26 |
| G-27 | D-4: Linux real file-link proof cannot be skipped as privilege absent | PC-27 |
| G-28 | D-4: disposable link removes link only, retains target sentinel | PC-28 |
| G-29 | D-4: post-inspection swap still seals retained-handle bytes | PC-29 |
| G-30 | D-5: missing dependency preflight names it, exits 97 before case | PC-30 |
| G-31 | D-5: recovery move precedes donor restart | PC-31 |
| G-32 | D-5: donor restart precedes ready marker publication | PC-32 |
| G-33 | D-5: seed failure removes owned stage and retains sibling | PC-33 |
| G-34 | D-3: effective LF honors later override/refusal, not just rule presence | PC-34 |
| G-35 | R-1: assembly-local spawn capacity remains one | PC-35 |
| G-36 | R-1: named process classes retain limiter | PC-36 |
| G-37 | R-1: singled-out live runner method retains limiter | PC-37 |
| G-38 | D-8: native apphost is required for child execution | PC-38 |
| G-39 | D-8: child uses current output's source stamp/provenance | PC-39 |
| G-40 | D-2: child launch strips worker selectors | PC-40 |
| G-41 | D-10: imported partition exactly matches explicit intended selection | PC-41 |
| G-42 | D-8/10: Windows partition preserves apphost build profile | PC-42 |
| G-43 | D-2: child launch strips C885 repeat selectors | PC-43 |
| G-44 | D-2: child launch strips checkpoint nonce | PC-44 |
| G-45 | D-2: child launch targets dead production-runner base | PC-45 |
| G-46 | D-2: child launch disables production interpreter probe | PC-46 |

G-13/14 use separate one-flip vectors inside the two existing heal methods. G-28's target
sentinel and G-26's exception table are internal cases in the new directory-link method;
these do not add TUnit executions. G-38/39/40/43–46 use a child-launch preparation validator:
B1's negative fixture supplies absent apphost, absent/wrong source stamp, seeded worker/repeat
keys and wrong runner base URL as independent corruptions, checking the named refusal.
For environment cases, feed unsafe inherited input through PrepareChildLaunch, then assert the
hand-written safe output independently; removing a sanitizer must fail that output assertion,
not merely provoke a child error. The live child audit then consumes that validated launch spec. This is required test/helper
work in the NEW B1 file, not a claim the existing C1050 helper already has those negative cases.

### Positive controls

Exact method filters are obtained by `/*/*/<Class>/<Method>` from the Detecting method column
(no whole-class PC runs). Parameterized B9 methods are not used; all selectors below are one
TUnit method. Original PC-1–13 IDs are retained with the corrections noted in their recipes.
All defects must compile; shell changes must parse and reach the expected assertion. Changes
under test are local scratch mutations, restored exactly; no defect is committed.

| PC | Break guard by this one compiling defect | Exact detecting method / decisive red assertion | Lane |
|---|---|---|---|
| PC-1 | Remove entry Require from HostJq `C1025_Check_has_no_install_effects` | `WindowsUnitPlatformAdmissionTests.C1210_Native_linux_entries_guard_before_work`: `c1210-first-statement` names that identity | portable |
| PC-2 | Add Require to Jq `Qualification_grades_the_jq_row_for_both_targets` | `WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates`: `c1210-unrostered-gate` | portable |
| PC-3 | Change Grok NativeLinuxReason one character, leaving literal oracle unchanged | `WindowsUnitPlatformAdmissionTests.C1210_Guard_matches_native_platform`: `c1210-exact-reason` (constant checked on Linux too) | portable |
| PC-4 | Replace shared non-Linux throw with return, retaining caller entry statements | `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`: `c1210-host-roster`, observed Failed/Passed instead of 68 required skips | Windows |
| PC-5 | Delete active `*.sh text eol=lf` | `ShellFixtureLineEndingContractTests.Shell_scripts_are_forced_lf_by_gitattributes`: missing literal rule | portable |
| PC-6 | Change one LF to CRLF in working copy of `scripts/fixtures/c913-seed-contract.sh` | `ShellFixtureLineEndingContractTests.Shell_scripts_have_no_carriage_returns`: `c1210-shell-cr` with exact path | portable |
| PC-7 | Remove ReparsePoint from ValidatePath mask | `ChannelOutboundStorageTests.C1059_Source_reads_reject_directory_links`: direct `ValidatePath` must throw `InvalidDataException`, `c1210-directory-path-rejected` | Windows junction |
| PC-8 | Same mask defect, separate cycle | `ChannelOutboundStorageTests.C1059_Source_reads_reject_file_links`: direct `ValidatePath` must throw, `c1210-file-path-rejected` | Linux real link |
| PC-9 | Omit payload_select from shared closure | `RemoteScriptContractTests.C849_Closure_preflight_names_a_missing_dependency`: healthy companion `c1210-closure-complete` expected exit 0 observes 97; do not count command-not-found as red | default shell lane |
| PC-10 | Move smoke after recovery move in inspected c590 scratch source | `RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker`: `c1210-smoke-before-recovery` | default shell lane |
| PC-11 | Remove selected CodexRoot class Timeout | `ProcessSpawnTimeoutPolicyTests.Spawning_classes_carry_a_class_timeout`: `c1210-timeout-present` names class | portable, OD-5 bound |
| PC-12 | Set selected CodexRoot default to 1200000 | `ProcessSpawnTimeoutPolicyTests.Spawn_timeouts_do_not_exceed_ten_minutes`: `c1210-timeout-ceiling` | portable, OD-5 bound |
| PC-13 | Bypass only generated heal unshare prerequisite check; negative probe retains failing unshare stub | `CodexRootExecContractTests.Entrypoint_heal_chowns_the_tmp_directory_once`: `c1210-prereq-unshare` requires named refusal before heal; replaces Plan's invalid unconditional-PREREQ fixture-error control | Linux |
| PC-14 | Bypass only state existence preflight in generated heal script | `CodexRootExecContractTests.Entrypoint_heal_never_names_a_path_outside_tmp`: `c1210-prereq-state` | Linux |
| PC-15 | Remove a rostered gate (leave roster unchanged), or its permitted helper edge's gate | `WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates`: `c1210-roster-without-gate` | portable |
| PC-16 | Audit ignores namespace/class identity and compares method alone | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: wrong-class one-flip accepted, `c1210-negative: identity` fails | portable |
| PC-17 | Audit takes FirstOrDefault definition and removes uniqueness rejection | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: duplicate-definition vector expected refusal, `c1210-negative: definition` | portable |
| PC-18 | Audit substitutes expected metadata when TestMethod absent | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: missing-method vector accepted, `c1210-negative: method` | portable |
| PC-19 | Compare host sets rather than multisets (Distinct both sides) | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: duplicate-host vector, `c1210-negative: host-multiplicity` | portable |
| PC-20 | Remove exact per-result reason comparison | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: wrong-reason vector, `c1210-negative: result-reason` | portable |
| PC-21 | Treat an absent portable result as optional | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: missing-portable vector, `c1210-negative: portable` | portable |
| PC-22 | Conditional validator admits Failed | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: conditional-failed vector, `c1210-negative: conditional` | portable |
| PC-23 | Remove counter equality checks | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: each counter one-flip, `c1210-negative: counters` | portable |
| PC-24 | Remove child-exit-zero requirement | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: valid TRX with exit 1, `c1210-negative: child-exit` | portable |
| PC-25 | Remove TestRun root name/namespace check, retain child sections | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: root renamed with intact children, `c1210-negative: trx-root` | portable |
| PC-26 | FileLink catches arbitrary IOException as unavailable | `ChannelOutboundStorageTests.C1059_Source_reads_reject_directory_links`: injected sharing/path I/O error must be same exception, `c1210-filelink-unknown-error` | portable |
| PC-27 | FileLink returns null for successful Linux creation | `ChannelOutboundStorageTests.C1059_Source_reads_reject_file_links`: non-null required before any optional skip, `c1210-filelink-native-required` | Linux |
| PC-28 | FileLink.Dispose removes target as well as link | `ChannelOutboundStorageTests.C1059_Source_reads_reject_directory_links`: scratch FileLink disposal case, `c1210-link-target-retained` sentinel bytes | Linux |
| PC-29 | Reopen output path after the swap barrier instead of reading retained reader | `ChannelOutboundStorageTests.Output_rename_and_link_swap_after_inspection_seals_the_opened_file`: `c1210-retained-read-completes` Should.NotThrowAsync fails on the replaced-path length/hash refusal after reached=true; exact byte assertions follow in green | Linux |
| PC-30 | Disable missing-definition refusal in preflight while preserving definitions | `RemoteScriptContractTests.C849_Closure_preflight_names_a_missing_dependency`: missing-select vector expects exit 97, exact MISSING and absent body sentinel, `c1210-preflight-refusal` | default shell lane |
| PC-31 | Move recovery publication after donor restart | `RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker`: `c1210-recovery-before-restart` | default shell lane |
| PC-32 | Move ready-marker mv before donor restart | `RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker`: `c1210-restart-before-ready` | default shell lane |
| PC-33 | Replace seed_failure owned-stage rm with `:` (preserve shell success) | `RemoteScriptContractTests.C849_Seed_refuses_invalid_donors_and_partial_payloads`: `stage-cleaned` absent / `stage-left` present; sibling retained | default shell lane |
| PC-34 | EffectiveEol stops at first matching rule instead of last | `ShellFixtureLineEndingContractTests.Shell_scripts_are_forced_lf_by_gitattributes`: synthetic later-crlf override is rejected, `c1210-effective-eol` | portable |
| PC-35 | ProcessSpawnLimit.Limit returns 2 | `ProcessSpawnLimitTests.Caps_concurrent_process_spawning_tests_at_one`: expected 1 | portable |
| PC-36 | Remove class limiter from AgentTaskLandPublicationTests | `ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter`: named class attribute missing | portable |
| PC-37 | Remove method limiter from AgentSessionRuntimeTests.Backend_runtime_can_send_input_to_live_runner_session_after_restart | `ProcessSpawnLimitTests.Live_runner_send_input_method_carries_the_limiter`: method attribute missing | portable |
| PC-38 | Launch validator accepts missing native apphost | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: absent-apphost vector expected rejection, `c1210-negative: apphost` | portable |
| PC-39 | Launch validator accepts missing/mismatched current-output stamp | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: stamp one-flip vector, `c1210-negative: source-stamp` | portable |
| PC-40 | Omit worker-selector stripping from launch preparation | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: seeded unsafe selector remains, `c1210-negative: child-worker` | portable |
| PC-41 | Change one actual partition filter to `/*/*/*/*[Category=Unit]` without changing oracle | `CheckpointImportTests.C1210_Windows_partition_selection_is_explicit`: `c1210-partition-selection` | portable |
| PC-42 | Remove `!isWindows` from BuildStep.PropertyArguments default-apphost condition | `CheckpointImportTests.C1210_Windows_partition_selection_is_explicit`: `c1210-partition-apphost` rejects Windows profile | portable |
| PC-43 | Omit C885 selector stripping from launch preparation | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: seeded C885 one-flip, `c1210-negative: child-repeat` | portable |
| PC-44 | Omit checkpoint nonce stripping from launch preparation | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: seeded nonce one-flip, `c1210-negative: child-nonce` | portable |
| PC-45 | Preserve inherited production runner base instead of dead base | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: unsafe-base one-flip, `c1210-negative: child-runner` | portable |
| PC-46 | Preserve inherited true interpreter-check flag | `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes`: interpreter one-flip, `c1210-negative: child-interpreter` | portable |

The full detecting identity is repeated in each PC row; no run may use a class filter. G-12's ordinary cases
also include zero/negative/unresolved values, not just the single above-ceiling mutation.
G-40's ordinary table covers worker keys, C885 repeat selectors, nonce, base URL and interpreter
setting individually. Mutations sharing files/methods run sequentially. Defect insertion into a
source-reading fixture targets its copied source or the actual guarded source as described;
it must not replace the independent expected outcome.

Post-land execution contract when the caller resumes Mutation: method-scoped baseline green,
break → expected assertion red → restore source/timestamps → rebuilt same-method green, with
fresh isolated outputs and per-PC receipts. Confirm healthy reachability and exact assertion;
zero tests, build failures or prerequisite/fixture errors are not red. SourceLanding evidence
stays outside the snapshot; no snapshot commit/push. Code runs V/R, Review judges before land.

Accounting: **guards=46, mapped=46, missing=0, duplicate PC maps=0. PCs run=0/46; all 46 pending**
under the caller's explicit Mutation pause. "All PCs run" is a future completion requirement,
not satisfied by this design. New methods/helpers and any CARD-1208-changed payload bindings
stay **binding required after Code** until real bodies, assertions and green source receipts
exist. In particular PC-11/12 also await OD-5; design status cannot become a mutation certificate.

### Out of scope

- Product/session/channel delivery, real queue recovery and UserPrompt receipts: no affected
  delivery implementation and no delivery claim; see the delivery inventory.
- Whole Unit/assembly completion, historical Linux whole-lane timeout, provider/live/endurance
  tests, CARD-1209 repairs, nightly-policy changes, privilege elevation/Developer Mode and
  production restarts/deployments. No whole-Unit checkpoint is permitted.
- Automatic child kill/EOF correctness from a TUnit Timeout attribute; C983 driver hardening is
  D-12. The sampled C973/C983 rows prove their named cases only.
- Literal repository-wide S5 qualification: withheld for OD-5 and a new scope/exception plan if
  chosen. The two metadata checks below cannot certify behavior across 338 source files.

### S2 Linux CP-9 selection amendment (Code 650b7652)

The caller's explicit S2 follow-up decisions authorize excluding five inherited C1192
harness reds from Linux CP-9 and assigning their repair to a follow-up Backlog card
that the caller will file. Each exclusion below reproduced as Failed at committed
base `c2d0085a9ac7ae7989217a892df6b8c8f26df35e`; all five methods still exist
in `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`. No test, assertion,
harness, timeout, other slice, or Windows checkpoint selection is changed.

| Exact excluded method | Base SHA with reproduced failure | Decisive base failure | Owner |
|---|---|---|---|
| `C1192_Lock_probe_distinguishes_shared_and_private_scratch` | `c2d0085a9ac7ae7989217a892df6b8c8f26df35e` | shared-waiter-blocked: expected 1, actual 0; FixtureNuGetLockHolderFailed | Caller-filed follow-up Backlog card (ID pending) |
| `C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy` | `c2d0085a9ac7ae7989217a892df6b8c8f26df35e` | C1192_FAIL init 2 CacheHelperImageMissing; C1192_EXIT=1 | Caller-filed follow-up Backlog card (ID pending) |
| `C1192_Race_programs_and_feed_share_the_work_volume` | `c2d0085a9ac7ae7989217a892df6b8c8f26df35e` | race-boundary: expected 1, actual 0; FixtureNuGetLockHolderFailed | Caller-filed follow-up Backlog card (ID pending) |
| `C1192_Partial_workspace_acquisition_is_cleaned` | `c2d0085a9ac7ae7989217a892df6b8c8f26df35e` | first-create-failure-cleaned: assertion false | Caller-filed follow-up Backlog card (ID pending) |
| `C1192_Race_barriers_observe_volume_state` | `c2d0085a9ac7ae7989217a892df6b8c8f26df35e` | volume-release-observed: expected 1, actual 0 | Caller-filed follow-up Backlog card (ID pending) |

First baseline execution: four executed, zero passed, four failed, zero skipped;
`sourceState=clean`, `buildSource=verified`. Evidence is the task's retained
`.antiphon/c1210-baseline-evidence/C1210-S2-inherited-20261013-095155-d93d/run.trx`.
The original CP-9 TRX had exactly the 40 selected source methods, 36 Passed and the
first four Failed. The later 36-method run had 35 Passed and the fifth method Failed;
its exact volume-release-observed assertion also reproduced in a one-method base
run (one executed, zero passed, one failed, zero skipped, clean source and verified
build). That evidence is retained at
`.antiphon/c1210-barriers-base-evidence/C1210-S2-barriers-base-20261013-101539-651c/run.trx`.
The caller then explicitly authorized the fifth exclusion with the same rules.
The approved Linux selection is the original 40 minus only these five:
26 C1192 methods, six C913 methods, and three other fixture methods, totaling 35.
CP-9's filter below explicitly names all 35; no broad C1192 operand can re-admit
an excluded method. The rerun must have exactly this roster, every result Passed,
zero Failed and zero NotExecuted, with a clean committed-source receipt.

Literal selected roster (all in `Antiphon.Tests.Scripts.RemoteScriptContractTests`):

- `C1191_Lock_probe_is_created_before_uid_handoff`
- `C1192_Fixture_has_no_host_payload_tree_or_payload_bind`
- `C1192_Finalization_precedes_success_and_preserves_primary_failure`
- `C1192_Evidence_export_preserves_fixture_roster_without_payloads`
- `C1192_Cleanup_consumer_requires_positive_absence`
- `C1192_Cleanup_helper_requires_positive_absence`
- `C1192_Cleanup_masked_requires_positive_absence`
- `C1192_Cleanup_work_requires_positive_absence`
- `C1192_Cleanup_mask_requires_positive_absence`
- `C1192_Cleanup_cache_requires_positive_absence`
- `C1192_Cleanup_network_requires_positive_absence`
- `C1192_Daemon_loss_after_compose_refuses_success`
- `C1192_Acquisition_unknown_ownership_retains_registration_and_residue`
- `C1192_Workspace_registration_is_absent_only_and_survives_prepare`
- `C1192_Cleanup_releases_only_registered_current_run_resources`
- `C1192_Toxic_probe_stays_in_work_volume`
- `C1192_Summary_validator_runs_under_the_production_interpreter`
- `C1192_Foreign_volume_is_refused_after_create`
- `C1192_Harness_cleanup_stays_inside_its_private_directory`
- `C1192_Harness_cleanup_refuses_ancestor_swap`
- `C1192_Helper_argv_keeps_discarded_env_file_visible`
- `C1192_Npm_seed_install_and_refill_use_work_volume`
- `C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed`
- `C1192_Real_cache_cases_keep_the_host_payload_backend`
- `C1192_Fixture_seed_and_rerun_keep_payloads_in_volume`
- `C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest`
- `C1192_Apphost_mask_is_an_empty_exact_volume_mount`
- `C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content`
- `C913_Seed_accepts_complete_ordinary_packages_without_framework_packs`
- `C913_Full_marker_binds_verified_recovery_before_publication`
- `C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct`
- `C913_Prune_validates_recovery_without_refilling_image_packs`
- `C913_Fixture_uses_owned_payloads_and_proven_absent_temp`
- `C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims`
- `C849_Prune_refuses_stale_or_busy_authority`

CP-17/18/19 remain PENDING for desktop Windows Final Review with their exact
filters and counts below. CP-19 still selects the original 40; its five inherited
reds are not qualified by Linux CP-9's approved exclusion. This amendment releases
only the narrowed Linux S2 verification; it does not discharge the excluded
methods, Windows qualification, or any SourceLanding Mutation PC.

### Checkpoints

Closed ordinary scope: 25 rows, **one separate isolated build per row**, one exact filter each.
There is no build reuse. `Min` is executed TUnit results, never internal vector counts or minutes.
CP-1–13 use the default Linux lane for native work; CP-14–25 require Windows runtime placement
where indicated by their group. Static tests themselves are portable; they repeat on Windows
to inspect that checkout/build. Default Windows apphost is retained. Rows share no output path.
All estimates include the row build and are estimates, not measured timings.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp1/` | linux-hostjq | `/*/*/HostJqPrerequisiteScriptTests/*` | V-1 | 39 Passed, 0 skipped | 39 | 20 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp2/` | linux-jq | `/*/*/JqRunnerImageContractTests/*` | V-1 | 18 Passed (12 Version arguments), 0 skipped | 18 | 8 | true |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp3/` | linux-grok-sha | `/*/*/GrokSha256PinTests/*` | V-1 | 11 Passed, 0 skipped | 11 | 7 | true |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp4/` | linux-codex-heal | `/*/*/CodexRootExecContractTests/*` | V-1 | 4 Passed; healthy heal reached; no prerequisite skip | 4 | 7 | true |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp5/` | linux-admission-static | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)` | V-2 | 4 Passed, 0 skipped; B1 internal vectors | 4 | 7 | true |
| CP-6 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp6/` | linux-c1050-static | `/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform*)\|(C1050_Remote_entries_guard_before_work*)\|(C1050_Rolling_entries_guard_before_work*)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes*)` | V-3 | 4 Passed, 0 skipped | 4 | 6 | true |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1210-cp7/` | linux-lf-contract | `/*/*/ShellFixtureLineEndingContractTests/*` | V-4 | 2 Passed; current tracked census 19 files | 2 | 7 | true |
| CP-8 | S2 | `tests/Antiphon.Tests -> bin-c1210-cp8/` | linux-codex-image | `/*/*/CodexRunnerImageContractTests/*` | V-5 | 13 Passed, 0 skipped | 13 | 8 | true |
| CP-9 | S2 | `tests/Antiphon.Tests -> bin-c1210-cp9/` | linux-remote-fixtures | `/*/*/RemoteScriptContractTests/(C1191_Lock_probe_is_created_before_uid_handoff*)\|(C1192_Fixture_has_no_host_payload_tree_or_payload_bind*)\|(C1192_Finalization_precedes_success_and_preserves_primary_failure*)\|(C1192_Evidence_export_preserves_fixture_roster_without_payloads*)\|(C1192_Cleanup_consumer_requires_positive_absence*)\|(C1192_Cleanup_helper_requires_positive_absence*)\|(C1192_Cleanup_masked_requires_positive_absence*)\|(C1192_Cleanup_work_requires_positive_absence*)\|(C1192_Cleanup_mask_requires_positive_absence*)\|(C1192_Cleanup_cache_requires_positive_absence*)\|(C1192_Cleanup_network_requires_positive_absence*)\|(C1192_Daemon_loss_after_compose_refuses_success*)\|(C1192_Acquisition_unknown_ownership_retains_registration_and_residue*)\|(C1192_Workspace_registration_is_absent_only_and_survives_prepare*)\|(C1192_Cleanup_releases_only_registered_current_run_resources*)\|(C1192_Toxic_probe_stays_in_work_volume*)\|(C1192_Summary_validator_runs_under_the_production_interpreter*)\|(C1192_Foreign_volume_is_refused_after_create*)\|(C1192_Harness_cleanup_stays_inside_its_private_directory*)\|(C1192_Harness_cleanup_refuses_ancestor_swap*)\|(C1192_Helper_argv_keeps_discarded_env_file_visible*)\|(C1192_Npm_seed_install_and_refill_use_work_volume*)\|(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed*)\|(C1192_Real_cache_cases_keep_the_host_payload_backend*)\|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)\|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*)\|(C1192_Apphost_mask_is_an_empty_exact_volume_mount*)\|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*)\|(C913_Seed_accepts_complete_ordinary_packages_without_framework_packs*)\|(C913_Full_marker_binds_verified_recovery_before_publication*)\|(C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct*)\|(C913_Prune_validates_recovery_without_refilling_image_packs*)\|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp*)\|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims*)\|(C849_Prune_refuses_stale_or_busy_authority*)` | V-5 | exactly 35 Passed, 0 Failed, 0 skipped; five named base-red exclusions above | 35 | 13 | true |
| CP-10 | S3 | `tests/Antiphon.Tests -> bin-c1210-cp10/` | linux-storage | `/*/*/ChannelOutboundStorageTests/*` | V-6, R-5 | 33 Passed, 0 skipped; real links | 33 | 8 | true |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-c1210-cp11/` | linux-c849-harness | `/*/*/RemoteScriptContractTests/(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)\|(C849_Seed_publishes_complete_payloads_before_its_marker*)\|(C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters*)\|(C849_Closure_preflight_names_a_missing_dependency*)\|(C983_Required_jq_driver_rejects_successful_false_probe*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)` | V-7, V-8, R-5 | 5 C849 + 1 C983 + 6 C973 = 12 Passed, 0 skipped | 12 | 14 | true |
| CP-12 | S5 | `tests/Antiphon.Tests -> bin-c1210-cp12/` | linux-timeout-policy | `/*/*/ProcessSpawnTimeoutPolicyTests/*` | V-9 | 2 Passed after OD-5; metadata policy only | 2 | 7 | true |
| CP-13 | S5 | `tests/Antiphon.Tests -> bin-c1210-cp13/` | linux-spawn-limit | `/*/*/ProcessSpawnLimitTests/*` | R-1 | 3 Passed, 0 skipped | 3 | 6 | true |
| CP-14 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp14/` | win-admission | `/*/*/WindowsUnitPlatformAdmissionTests/*` | V-1, V-2 | 5 outer Passed; S1 child 4 Passed + 68 NotExecuted; exact reasons | 5 | 12 | true |
| CP-15 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp15/` | win-portable-direct | `/*/*/(JqRunnerImageContractTests*)\|(CodexRootExecContractTests*)/(Pinned_download_is_verified_before_root_owned_install_in_every_runner_target*)\|(Qualification_grades_the_jq_row_for_both_targets*)\|(Entrypoint_heals_only_the_codex_tmp_directory*)\|(Provider_exec_into_the_runner_names_uid_1654*)` | V-1 | 4 Passed, 0 skipped; only four portable methods | 4 | 8 | true |
| CP-16 | S1 | `tests/Antiphon.Tests -> bin-c1210-cp16/` | win-c1050-platform | `/*/*/C1008PlatformContractTests/*` | V-3, R-2 | 6 outer Passed; exact existing child rosters | 6 | 9 | true |
| CP-17 | S2 | `tests/Antiphon.Tests -> bin-c1210-cp17/` | win-lf-contract | `/*/*/ShellFixtureLineEndingContractTests/*` | V-4 | 2 Passed; restored working files contain no CR | 2 | 9 | true |
| CP-18 | S2 | `tests/Antiphon.Tests -> bin-c1210-cp18/` | win-codex-image | `/*/*/CodexRunnerImageContractTests/*` | V-5 | 13 Passed, 0 skipped; qualified WSL | 13 | 9 | true |
| CP-19 | S2 | `tests/Antiphon.Tests -> bin-c1210-cp19/` | win-remote-fixtures | `/*/*/RemoteScriptContractTests/(C1192_*)\|(C913_*)\|(C1191_Lock_probe_is_created_before_uid_handoff*)\|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*)\|(C849_Prune_refuses_stale_or_busy_authority*)` | V-5 | 40 Passed, 0 skipped; WSL pwsh/jq prerequisite | 40 | 17 | true |
| CP-20 | S3 | `tests/Antiphon.Tests -> bin-c1210-cp20/` | win-storage-and-audit | `/*/*/(ChannelOutboundStorageTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | V-2, V-6, R-5 | 38 total: 36–38 Passed, 0–2 exact Conditional skips; integrated child total 105 | 36 | 12 | true |
| CP-21 | S4 | `tests/Antiphon.Tests -> bin-c1210-cp21/` | win-c849-harness | `/*/*/RemoteScriptContractTests/(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)\|(C849_Seed_publishes_complete_payloads_before_its_marker*)\|(C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters*)\|(C849_Closure_preflight_names_a_missing_dependency*)\|(C983_Required_jq_driver_rejects_successful_false_probe*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)` | V-7, V-8, R-5 | 12 Passed, 0 skipped; method durations and child completion | 12 | 17 | true |
| CP-22 | S5 | `tests/Antiphon.Tests -> bin-c1210-cp22/` | win-timeout-policy | `/*/*/ProcessSpawnTimeoutPolicyTests/*` | V-9 | 2 Passed after OD-5; metadata policy only | 2 | 9 | true |
| CP-23 | S6 | `tests/Antiphon.Tests -> bin-c1210-cp23/` | win-remote-class | `/*/*/RemoteScriptContractTests/*` | V-10 | 413 total: 195 Passed + 218 exact legacy skips; complete TRX, 0 Failed | 195 | 35 | true |
| CP-24 | S6 | `tests/Antiphon.Tests -> bin-c1210-cp24/` | win-argv-roundtrip | `/*/*/DelegateLaunchArgvIntegrityTests/*` | R-3 | 7 Passed, 0 skipped (Integration category) | 7 | 7 | true |
| CP-25 | S6 | `tests/Antiphon.Tests -> bin-c1210-cp25/` | win-partition-import | `/*/*/CheckpointImportTests/C1210_Windows_partition_selection_is_explicit*` | V-10, R-4 | 1 Passed; real artifact import and exact portfolio/profile assertions | 1 | 7 | true |

Ordinary floor totals: Linux **118 min**, Windows **151 min**, combined **269 min**.

Dispatch selection is explicit: S1 Linux `CP-1,CP-2,CP-3,CP-4,CP-5,CP-6`, Windows
`CP-14,CP-15,CP-16`; S2 Linux `CP-7,CP-8,CP-9`, Windows `CP-17,CP-18,CP-19`;
S3 Linux `CP-10`, Windows `CP-20`; S4 Linux `CP-11`, Windows `CP-21`;
S5 Linux `CP-12,CP-13`, Windows `CP-22`; S6 Windows `CP-23,CP-24,CP-25`.
Do not use `--after S1` alone on this mixed-platform table: it selects both lanes. Use `--rows`
with the listed lane subset and the committed slice's full `--expected-source-sha`.

Tool bootstrap is an explicitly budgeted setup build through `scripts/build-slot.ps1`, isolated
`bin-c1210-tool/` on each lane (Linux default UseAppHost=false, Windows default apphost). Use
that built tool's `run --plan docs/superpowers/plans/2026-10-13-card-1210-test-design.md --rows
<above-list> --expected-source-sha <committed-sha> --serial --max-wait 55s`; join `wait` calls
until exit is not 75. The checkpoint executor owns row build slots. A slot timeout is not-run,
not permission for an unleased retry. Clean only manifest-owned `bin-c1210-*` outputs on each
producer host after joined runs. Keep raw evidence gitignored and retain unedited CHECKPOINT
lines, SHA/provenance, actual rosters and per-row counts in the stored Code report.

Windows skip audit:
CP-14 and CP-16 have positive outer audit results even though children skip. CP-15 is now
narrowed to four portable methods, with **zero** skip tokens in its executed Expect roster.
Neither HostJq nor GrokSHA is selected directly on Windows. CP-20 has 36 or more ordinary
outer executions; its only two possible outer skips are the exact Conditional identities.
CP-23 has 195 ordinary executions and 218 legacy skips. An unrecognized skip in CP-18/19/21,
or a zero-execution row, is refused. The imported Expect tokens are class names, not skipped
method names. Qualification for CP-20 and CP-23 requires raw-TRX exact roster checks in addition
to the checkpoint's execution floor; no strict zero-skip certificate for either. A CP-14/16
outer clean certificate does not turn its intentionally skipped children into executed proof.

### Cost

All numbers are **estimated**, including the expanded-build floor. No build/test timing was
measured by TestDesign. Ordinary Code V/R floor = **269 minutes** (sum of 25 EstimatedMinutes:
Linux 118 + Windows 151). Per slice S1 84, S2 63, S3 20, S4 31, S5 22, S6 49 minutes.
Each filter is named in its CP row; 25 separate builds contribute an assumed 5 minutes each
(**125 minutes**), leaving **144 minutes** for the selected V/R work. Tool setup is two
lane bootstraps × 3 minutes = **6 minutes**, ordinary setup+V/R total **275 minutes**.
Actual slot wait is additional and reported; scheduling/authoring is not hidden in this floor.
These estimates apply to the bounded S5 recommendation. Choosing repository-wide S5 requires
new behavior scope and cost, not relabelling this 22-minute metadata allocation.

Mutation floor when resumed: 46 exact method filters as named in PC-1–46. Allocate each
baseline-green, red and restored-green phase a 4-minute isolated build + 1-minute test floor,
and 1 minute per control for mutation/restore checks: **46 × (3 × 5 + 1) = 736 minutes**.
The native Windows child audit PC-4 gets an additional 3 × 5 = **15 minutes**; its whole
per-control allowance is 31 minutes. PC-7's native junction method uses the ordinary 16-minute
cycle allowance. The other 44 selectors also receive 16 minutes each. Mutation subtotal
**751 minutes**, plus two lane setup builds **6 minutes** = **757 minutes**.
This budget includes baseline as well as the required red/restore/green, with no class/suite
mutation runs. Actual slow shell/native phases may require a newly stated estimate, not wider
assertions or deadlines. Mutation is paused, so current authorized executed-PC cost is zero;
that does not discharge the 757-minute prospective obligation.

Full prospective setup + ordinary V/R + Mutation = **275 + 757 = 1,032 minutes**.
The Plan estimated 168 ordinary minutes using reused builds and incomplete counts. This
closed-table requirement spends **101 additional estimated ordinary minutes**, rather than
claiming a saving. Build-reuse savings are zero because this brief requires one isolated build
per row. Exact-method PCs avoid broad-suite repeats; no numeric savings against an unmeasured
full Windows Unit run are asserted. S1/S2/S3 parallel authoring reduces elapsed calendar time
only within live concurrency/build-slot limits; it does not reduce this summed work floor.

--- next stage ---
next: decide
handoff: Decide OD-5: recommend S5 limited to the 11 named portfolio classes; literal D-7 reaches 340 classes/338 files and conflicts with preserved endurance overrides. S1 native gates/roster is first Code slice; S2/S3 can author in parallel with S1-first roster landing. Hold S4/S5 behind CARD-1208; use this corrected 25-row table and pending PCs.
artifact: docs/superpowers/plans/2026-10-13-card-1210-test-design.md
