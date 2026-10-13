# CARD-1210 Windows Unit triage

All 89 inherited methods were mapped and exercised with narrow Windows selections. There are platform-admission, Windows fixture, stale-harness and build-profile failures; no Windows production defect was established. Neither sampled child wait reproduced as a permanent hang. This is completed Debug analysis, not a passing Windows Unit qualification.

Task: 8db33c5e-6fb9-4e26-93ad-952f33475461. Branch: feat/card-task-8db33c5e.
Host: DESKTOP-KTLKPIF, Windows 10.0.19045, .NET 9.0.20, TUnit 1.44.
Task base: 1ff3e901898e0b3b96983ee942c1d25ea68832bd.
Tested report-only commits: a7dc8596b082b524bd76074a05223d164d4bf255 and 0057e1997b526925a01c88dbad3fc3530a97aed8. Test/production source was unchanged between them.

## Evidence and counts

CARD-1171's stored report is at `C:\Antiphon\worktrees\card-task-1527665f\.antiphon\task-1527665f.md`. Its base TRX survives at `C:\Antiphon\worktrees\card-task-1527665f\.antiphon\c1171-inherited-retained\20261013-012108-1d99\rows\CP-B1\run.trx`. Definitions and result IDs establish **100 failed results, 89 distinct methods**, including **27** RemoteScriptContractTests methods (the interim scope note incorrectly said 26). The original tested base was 118bba94f1dae3499b84260b26d062cd75e1a6c4. No new baseline run was needed: this task changes only a report, and every requested inherited failure already has base evidence.

The named CARD-0971 report was absent at the current tip of feat/card-task-bc957ed9. It was recovered read-only from commit bac6b083d, path `docs/superpowers/reports/2026-10-13-card-0971-code-evidence.md`. Its C983 sample was on Linux, not proof of a Windows never-exiting process. It explicitly says that the sample identified a wait, not its root cause.

| Row | Filter scope | Executed | Passed | Failed | Skipped | Outcome |
|---|---|---:|---:|---:|---:|---|
| CP-1 | C1008PlatformContractTests | 6 | 4 | 2 | 0 | UseAppHost=false diagnostic refusal |
| CP-2 | ChannelOutboundContractTests | 2 | 2 | 0 | 0 | green; CARD-1209 repair present |
| CP-3 | ChannelOutboundStorageTests | 32 | 30 | 2 | 0 | missing Windows symlink privilege |
| CP-4 | CodexRootExecContractTests | 4 | 2 | 2 | 0 | WSL mount fixture cannot create /state |
| CP-5 | CodexRunnerImageContractTests | 13 | 10 | 3 | 0 | CRLF shell fixtures |
| CP-6 | GrokRunnerImageContractTests | 26 | 26 | 0 | 0 | green; CARD-1209 repair present |
| CP-7 | GrokSha256PinTests | 11 | 0 | 11 | 0 | direct /bin/sh on Windows |
| CP-8 | HostJqPrerequisiteScriptTests | 39 | 0 | 39 | 0 | native Linux assertion, no skip |
| CP-9 | JqRunnerImageContractTests | 18 | 2 | 16 | 0 | five Linux fixture methods, 12 version arguments |
| CP-10 | RemoteScriptContractTests | unknown | unknown | unknown | unknown | 3-minute class timeout; no TRX |
| CP-11 | exact C983 driver false-probe method | 1 | 1 | 0 | 0 | green, testsWall 33.73s |
| CP-12 | exact 27 inherited remote methods | 27 | 0 | 27 | 0 | complete TRX, testsWall 9.31s |
| CP-13 | exact C973 cold-marker reader method | 6 | 6 | 0 | 0 | green, testsWall 49.61s |
| CP-14 | C1008PlatformContractTests, native apphost | 6 | 6 | 0 | 0 | green, testsWall 20.51s |

Totals of completed diagnostic rows: **191 results, 89 passed, 102 failed, zero skipped**, including repeated C1008 diagnostics. CP-10 contributes no counters. There are 91 distinct methods with a failed result in that total; two are C1008's false-apphost refusals, which CP-14 resolves, leaving **89 currently failing methods**. Those comprise 85 still-red inherited methods plus four additional HostJq descriptor-probe methods. All 89 requested inherited methods occur in fresh TRX definitions/results. A whole Unit or whole assembly run was never launched.

Runs:

- `20261013-030351-6bb2`: CP-1..CP-11, exit 5 due to CP-10 timeout; isolated UseAppHost=false build, 8m33s outer wall.
- `20261013-031452-5770`: CP-12..CP-14, exit 1 from the 27 remote failures; native Windows apphost build, 4m57s outer wall.
- `20261013-030206-d76e`: erroneous bare timeout arguments meant seconds, giving a one-minute total deadline; build stopped before tests, zero executed. Corrected to `3m` / `40m` for the diagnostic run. An earlier short expected SHA was refused before any driver; the first follow-up YAML was also refused before drivers. These are launcher errors, not test verdicts.

Every actual driver used a granted host build slot. ANTIPHON_TASK_TOKEN was present and never printed. Both request.json files bind ownerTaskId to this task and ownerSessionId to ced558f7-f665-4867-8f00-9d58e456c677. One owner HTTP read timed out during the follow-up build; polling recovered, without owner-unverified or unleased execution.

Validator: source-valid for CP-2, CP-6, CP-11 at a7dc8596b082b524bd76074a05223d164d4bf255 and CP-13, CP-14 at 0057e1997b526925a01c88dbad3fc3530a97aed8. Red rows and CP-10 have no green certificate. Final report edits do not relabel those receipts as testing the final report commit.

## Cause key and fixes

**L1 — needs native Linux, lacks platform gate.** GrokSha256PinTests invokes `/bin/sh` directly. HostJqFixture asserts IsLinux before discovering jq, bash, flock and GNU tools and exercising `/proc/self/fd` identity/modes. JqFixture asserts IsLinux before POSIX ownership/symlink probes. These assertions currently fail instead of skip. Codex heal tests use unshare/user/mount namespaces and `/state`; WSL here returns `mkdir: cannot create directory /state: Permission denied`. Follow the repository's `C1008HostFixture.RequireNativeLinux()` admission pattern: an explicit SkipTestException at the test entry before constructing a fixture or doing work. Use a reason specific to each contract. The proposed Windows child audit selects all four classes: 72 child results should comprise 68 exact Linux-only skips and four portable passes (57 skipped method identities), with no fixture side effects. Keep all portable text methods active on Windows, and qualify every admitted behavior on native Linux. A whole-class gate on JqRunnerImageContractTests or CodexRootExecContractTests would incorrectly discard their portable checks.

**W1 — real Windows checkout/fixture defect, fix rather than gate.** `core.autocrlf=true` and `.gitattributes` pins `scripts/*.sh`, but not the relevant nested fixtures. `c1192-workspace-contract.sh` contains 2,957 CRLF lines and no LF-only lines; Git reports eol unspecified. WSL/Bash errors include `set: pipefail\r`, `$'{\r'`, `$'do\r'`, and paths with CR embedded. Files implicated: `scripts/fixtures/c913-image-contract.sh`, `c913-seed-contract.sh`, and `c1192-workspace-contract.sh`. Pin shell fixtures to LF recursively and preserve their executable semantics. This is a test checkout defect, not evidence of production deployment failure.

The summary-validator failure is also W1: its awk block extractor stops only at `^}$`; a CRLF closing brace fails that predicate, so it extracts from exec_in through later functions and falsely sees the legitimate later `bash -c` near line 2375. exec_in itself distinguishes bin=sh from bin=bash. Do not weaken the interpreter guard; fix source byte fidelity and retain the negative controls.

**W2 — Windows fixture capability defect, not product behavior.** Two portable ChannelOutboundStorageTests fail at File.CreateSymbolicLink with `A required privilege is not held by the client`, before the production assertions run. The repository's DirectoryLink fixture uses a Windows junction for directory links without privilege. Reuse that for the directory case; retain real file-link coverage with a capability-qualified explicit skip when file links cannot be created, plus Windows capable-host and Linux rows. Never silently pass a missing file-link control or classify the entire storage class Linux-only. Do not enable Developer Mode, elevate or alter host policy in this task.

**S1 — stale extracted harness.** C849_Saved_donor_archive_is_checked_and_imported_without_a_container omits c849_payload_select/c849_payload_root and dependent payload functions now used by c849_seed. C849_Seed_refuses_invalid_donors_and_partial_payloads similarly omits the payload selector in its gate harness. Refresh the extracted dependency closure or exercise the complete offline controller; preserve the intended custody/refusal assertions. C849_Seed_publishes_complete_payloads_before_its_marker expects the old `c849_smoke "$helper" seed` order; current volume-backed publication uses the payload controller. Update the contract to the current documented publication path and give it a defect-sensitive assertion. These three are not Windows-only; gating them would hide the stale tests on Linux.

**S2 — stale assertion already repaired by CARD-1209.** ChannelOutboundContractTests and GrokRunnerImageContractTests now pass. Commit ea2ea7986 updated the contracts before this task's base. Do not duplicate that remediation.

**B1 — build profile, not Windows defect.** C1008's two native Windows audits require Antiphon.Tests.exe. CARD-1171 used UseAppHost=false; this diagnostic reproduced that profile. CP-14 uses the normal Windows apphost and passes all six outer audits. Each nested audit checks its child TRX's exact passed/skipped roster and result-local reason. Future Windows audit rows must explicitly retain UseAppHost=true; Linux can keep its established false/staged-apphost path.

## All 89 inherited methods

Classes in Application, Infrastructure and Scripts use the corresponding `Antiphon.Tests.<namespace>` namespace shown by their source location. Names below come from TRX TestMethod@className/name, not display-name parsing. The Base column counts argument results; Current reports the final narrow outcome. Cause references above include the evidence and proposed fix for every row.

| Class | Method | Base failed results | Current | Cause | Proposed fix |
|---|---|---:|---|---|---|
| ChannelOutboundContractTests | Server_and_gateway_keep_source_and_transport_boundaries | 1 | Passed | S2: stale, repaired | Keep CARD-1209 repair; no duplicate |
| ChannelOutboundStorageTests | C1059_Source_reads_reject_file_and_directory_links | 1 | Failed | W2: Windows fixture capability | Fix junction/file-link admission; caller card W2 |
| ChannelOutboundStorageTests | Output_rename_and_link_swap_after_inspection_seals_the_opened_file | 1 | Failed | W2: Windows fixture capability | Fix junction/file-link admission; caller card W2 |
| CodexRootExecContractTests | Entrypoint_heal_chowns_the_tmp_directory_once | 1 | Failed | L1: native Linux fixture | Entry gate + native Linux row |
| CodexRootExecContractTests | Entrypoint_heal_never_names_a_path_outside_tmp | 1 | Failed | L1: native Linux fixture | Entry gate + native Linux row |
| CodexRunnerImageContractTests | C913_Image_wrapper_isolates_mounts_and_qualifies_receipts | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| CodexRunnerImageContractTests | C913_Net9_probe_rejects_warm_caches_and_missing_packs | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| CodexRunnerImageContractTests | Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| GrokRunnerImageContractTests | Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers | 1 | Passed | S2: stale, repaired | Keep CARD-1209 repair; no duplicate |
| GrokSha256PinTests | Runner_grok_sha_accepts_matching_file | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_devnull_second_line | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_embedded_newline | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_empty_download | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_empty_pin | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_long_pin | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_multiline_pin | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_short_pin | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_tampered_file | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_trailing_newline | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| GrokSha256PinTests | Runner_grok_sha_rejects_uppercase_pin | 1 | Failed | L1: native Linux /bin/sh | Entry gate + native Linux row |
| JqRunnerImageContractTests | C1054_Jq_row_accepts_canonical_file_and_alias | 1 | Failed | L1: native Linux modes/identity | Entry gate + native Linux row |
| JqRunnerImageContractTests | C1054_Jq_row_records_found_and_resolved_paths | 1 | Failed | L1: native Linux modes/identity | Entry gate + native Linux row |
| JqRunnerImageContractTests | C1054_Jq_row_rejects_canonical_leaf_symlink | 1 | Failed | L1: native Linux modes/identity | Entry gate + native Linux row |
| JqRunnerImageContractTests | C1054_Jq_row_rejects_home_shadow | 1 | Failed | L1: native Linux modes/identity | Entry gate + native Linux row |
| JqRunnerImageContractTests | Version_row_accepts_only_exact_successful_pin_without_stderr | 12 | Failed | L1: native Linux modes/identity | Entry gate + native Linux row |
| C1008PlatformContractTests | C1050_Windows_remote_outcomes_are_exact | 1 | Passed | B1: Windows audit build profile | UseAppHost=true; no product card |
| C1008PlatformContractTests | C1050_Windows_rolling_outcomes_are_exact | 1 | Passed | B1: Windows audit build profile | UseAppHost=true; no product card |
| HostJqPrerequisiteScriptTests | C1025_Check_has_no_install_effects | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Check_qualifies_deployment_shell | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Check_rejects_canonical_leaf_symlink | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Explicit_tool_phases_never_deploy | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Final_recycle_guard_remains | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Preflight_is_fresh_for_each_entry | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Preflight_precedes_every_phase | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_admits_host_prerequisites | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_cleans_only_owned_staging | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_publishes_complete_no_clobber | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_refuses_unsafe_destination | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_requalifies_published_jq | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_requires_canonical_source | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_requires_missing_jq | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_serializes_and_rechecks | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Provision_verifies_download_before_use | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Receipt_persistence_is_required | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Receipt_rejects_canonical_lookup_with_unapproved_target | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Receipt_requires_complete_current_proof | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Transport_is_bounded_and_reaped | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1025_Transport_preserves_secret_custody | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Changed_leaf_cannot_emit_success_proof | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Check_rejects_canonical_hardlink | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Digest_is_bound_to_open_descriptor | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Final_link_checks_are_independent | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Final_permission_checks_are_independent | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Identity_comparisons_include_device | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Installed_pin_is_checked_before_execution | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Metadata_syntax_refuses | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Observation_failures_are_invalid | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Open_descriptor_must_match_canonical_leaf | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Pre_execution_link_checks_are_independent | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Proof_is_buffered_until_final_admission | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Provision_removes_only_owned_stage_link | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| HostJqPrerequisiteScriptTests | C1058_Staged_unlink_failure_refuses | 1 | Failed | L1: native Linux /proc/GNU tools | Entry gate + native Linux row |
| RemoteScriptContractTests | C1191_Lock_probe_is_created_before_uid_handoff | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Apphost_mask_is_an_empty_exact_volume_mount | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Cleanup_releases_only_registered_current_run_resources | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Fixture_seed_and_rerun_keep_payloads_in_volume | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Foreign_volume_is_refused_after_create | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Harness_cleanup_refuses_ancestor_swap | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Harness_cleanup_stays_inside_its_private_directory | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Helper_argv_keeps_discarded_env_file_visible | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Lock_probe_distinguishes_shared_and_private_scratch | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Npm_seed_install_and_refill_use_work_volume | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Partial_workspace_acquisition_is_cleaned | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Race_barriers_observe_volume_state | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Race_programs_and_feed_share_the_work_volume | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Real_cache_cases_keep_the_host_payload_backend | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Summary_validator_runs_under_the_production_interpreter | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Toxic_probe_stays_in_work_volume | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1192_Workspace_registration_is_absent_only_and_survives_prepare | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C849_Prune_refuses_stale_or_busy_authority | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C849_Saved_donor_archive_is_checked_and_imported_without_a_container | 1 | Failed | S1: stale extracted harness | Refresh closure/publication assertion; caller card S1 |
| RemoteScriptContractTests | C849_Seed_publishes_complete_payloads_before_its_marker | 1 | Failed | S1: stale extracted harness | Refresh closure/publication assertion; caller card S1 |
| RemoteScriptContractTests | C849_Seed_refuses_invalid_donors_and_partial_payloads | 1 | Failed | S1: stale extracted harness | Refresh closure/publication assertion; caller card S1 |
| RemoteScriptContractTests | C913_Fixture_uses_owned_payloads_and_proven_absent_temp | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |
| RemoteScriptContractTests | C913_Seed_accepts_complete_ordinary_packages_without_framework_packs | 1 | Failed | W1: Windows fixture CRLF | Pin nested shell fixtures LF; caller card W1 |

### Additional class failures

CP-8 exposed four additional methods with the same L1 cause: C1058_Version_probe_uses_open_descriptor, C1058_True_probe_uses_open_descriptor, C1058_False_probe_uses_open_descriptor, C1058_Receipt_formatter_uses_open_descriptor, all in HostJqPrerequisiteScriptTests. Each produced one failed result at the native Linux assertion. They were absent from the 89-method inherited roster because that broad run never completed, not because they are newly introduced by this report. No source mutation occurred.

## What the sampled waits mean

The lane's alleged permanent hang was **not reproduced** by the prescribed narrow runs. The 3-minute full RemoteScriptContractTests row timed out without TRX. That class mixes many argument-expanded shell/process cases with text checks. The assembly-local ProcessSpawnLimit has Limit=1; the dump contains 31 SemaphoreSlim+TaskNode occurrences with TUnit ExecuteTestInternalAsync/ExecuteScheduledTestAsync chains queued behind the admitted child-spawning case. These are scheduler waits, not 31 independent production deadlocks. The dump does not retain every queued method's identity; it would be inaccurate to invent a full queued-method list.

Windows process-tree samples identified a test-owned host with child wsl.exe/WSL relay processes. Heap/CLR stack evidence at `.antiphon/c1210-remote-class.dmp`, `c1210-remote-async.txt` and `c1210-remote-stacks.txt` identifies `C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers -> LinuxShell -> Process.WaitForExit(Int32)` at line 7970. LinuxShell has a 60,000ms child bound; the observed WSL children changed across samples, showing progress rather than one immutable never-exiting child. CP-13 selected just that method, reproduced the same wait stack in `c1210-marker-method.dmp` / `c1210-marker-stacks.txt`, and completed **all six cases green** in about 50s. WSL process observations included Bash waiting on child/pipe work and an active awk extractor; they do not prove deadlock.

CP-11 selected just `C983_Required_jq_driver_rejects_successful_false_probe`. It ran all nine vectors with KeepTemp false/true, **18 child invocations inside one test**, and passed in about 34s. C983Fixture.Run has a five-minute per-child wait bound. The driver script separately has an unbounded WaitForExit followed by pipe drains, and the fixture drains streams without a separate EOF deadline; those are static robustness gaps, not demonstrated causes of either historical timeout. Its private resolver accepts first-existing pwsh candidates, bypassing CARD-1171's shared resolver fix. On this unchanged PATH it selected a real installed Store-package executable, and the exact test passed. No alias or PATH edit was made.

The historical CARD-0971 dump's C983 wait was on Linux. It is consistent with normal child work and a serial queue; these Windows receipts cannot determine why that historical Linux whole-Unit selection reached its deadline. Likewise, a 3-minute class observation limit is not a measured full-class runtime. We can explain the sampled semaphore queue and demonstrate that both sampled methods complete; **we cannot identify a permanently stuck Windows child or assert the entire Unit timeout is resolved**. A final Plan must retain this uncertainty, partition shell behavior into owned narrow/native Linux rows, and measure completion after the fixture repairs. Do not lengthen assertions or add retries to suppress it.

## Proposed caller cards and first slices

No cards were filed by this Debug task. The caller should search the board for duplicates, then group by root cause:

1. **L1:** native Linux admission before fixture side effects for HostJq, Jq behavior, Grok SHA and Codex namespace-heal methods. First CARD-1210 slice. Retain portable Windows methods and add a Windows exact skipped-roster audit; prove Linux cases still execute.
2. **W1:** recursive LF fidelity for executed/extracted shell fixtures on Windows. Cover c913/c1192 and the summary interpreter control. Fix checkout policy, not the assertions.
3. **W2:** portable outbound file/directory link fixtures on Windows without symlink privilege. Keep rejection/sealing behavior covered with junctions and explicit capability-qualified file-link control.
4. **S1:** stale C849 payload dependency closures and publication-order assertion. Requires native Linux regression rows as well as Windows/WSL verification.

B1 is a plan/build setting, not a separate code defect. S2 is already covered by CARD-1209. Use CARD-1114 for recurring Windows audit placement; no recurring job was added. There is no proven Windows production regression to file. Optional child EOF/deadline hardening needs a separate commissioned test-harness slice and method-scoped failure controls, not an unsupported claim that it fixes this timeout.

### Proposed closed checkpoint rows

This is the proposed implementation selection for Plan to formalize, not runs performed by Debug. It is closed: no whole Unit or assembly row. Each platform builds once into its own isolated producer-owned output; every reused row has the same slice group and source provenance. Windows explicitly UseAppHost=true; Linux uses the repository's established UseAppHost=false/default staging. Rows describing new audit methods are proposed names to author in the next stage. Linux outcome expectations are obligations, not claims of a run here.

| CP | Platform / After | Build | Exact filter | Minimum executed | Expected outcome |
|---|---|---|---|---:|---|
| P-W1 | Windows / S1 | tests/Antiphon.Tests -> bin-c1210-win-s1/ | `/*/*/WindowsUnitPlatformAdmissionTests/Native_linux_entries_skip_before_fixture_work` | 1 | new real child-TRX audit: exact Linux-only methods skipped with result-local reason before side effects, portable rows pass |
| P-W2 | Windows / S1 | reuse P-W1 | `/*/*/C1008PlatformContractTests/*` | 6 | all outer audits green; native child apphost exists |
| P-L1 | Linux / S1 | tests/Antiphon.Tests -> bin-c1210-linux-s1/ | `/*/*/HostJqPrerequisiteScriptTests/*` | 39 | 39 pass, no platform skips; jq/GNU prerequisites recorded |
| P-L2 | Linux / S1 | reuse P-L1 | `/*/*/JqRunnerImageContractTests/*` | 18 | 18 pass, no platform skips |
| P-L3 | Linux / S1 | reuse P-L1 | `/*/*/GrokSha256PinTests/*` | 11 | 11 pass, no platform skips |
| P-L4 | Linux / S1 | reuse P-L1 | `/*/*/CodexRootExecContractTests/*` | 4 | 4 pass; unshare/mount fixture prerequisites qualified |
| P-W3 | Windows / S2 | tests/Antiphon.Tests -> bin-c1210-win-s2/ | `/*/*/CodexRunnerImageContractTests/*` | 13 | 13 pass with WSL prerequisite present and LF fixtures |
| P-W4 | Windows / S2 | reuse P-W3 | `/*/*/RemoteScriptContractTests/C1192*` | 31 | all 31 C1192 methods pass, no skip replacement |
| P-W5 | Windows / S2 | reuse P-W3 | `/*/*/RemoteScriptContractTests/C1191_Lock_probe_is_created_before_uid_handoff` | 1 | 1 pass |
| P-W6 | Windows / S2 | reuse P-W3 | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | 1 | 1 pass |
| P-W7 | Windows / S2 | reuse P-W3 | `/*/*/RemoteScriptContractTests/C913*` | 6 | all selected C913 cases pass |
| P-W8 | Windows / S2 | reuse P-W3 | `/*/*/RemoteScriptContractTests/C849_Prune_refuses_stale_or_busy_authority` | 1 | 1 pass |
| P-L5 | Linux / S2 | tests/Antiphon.Tests -> bin-c1210-linux-s2/ | `/*/*/CodexRunnerImageContractTests/*` | 13 | all selected cases remain green |
| P-L6 | Linux / S2 | reuse P-L5 | `/*/*/RemoteScriptContractTests/C1192*` | 31 | same C1192 method roster remains green |
| P-L7 | Linux / S2 | reuse P-L5 | `/*/*/RemoteScriptContractTests/C913*` | 6 | all selected C913 cases remain green |
| P-L8 | Linux / S2 | reuse P-L5 | `/*/*/RemoteScriptContractTests/C1191_Lock_probe_is_created_before_uid_handoff` | 1 | 1 pass |
| P-L9 | Linux / S2 | reuse P-L5 | `/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content` | 1 | 1 pass |
| P-L10 | Linux / S2 | reuse P-L5 | `/*/*/RemoteScriptContractTests/C849_Prune_refuses_stale_or_busy_authority` | 1 | 1 pass |
| P-W9 | Windows / S3 | tests/Antiphon.Tests -> bin-c1210-win-s3/ | `/*/*/ChannelOutboundStorageTests/*` | 30 | 30 portable pass; two file-link controls execute on capable host or explicitly skip with exact capability reason; never vacuous pass |
| P-L11 | Linux / S3 | tests/Antiphon.Tests -> bin-c1210-linux-s3/ | `/*/*/ChannelOutboundStorageTests/*` | 32 | 32 pass with real links |
| P-W10 | Windows / S4 | tests/Antiphon.Tests -> bin-c1210-win-s4/ | `/*/*/RemoteScriptContractTests/C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | 1 | 1 pass after closure repair |
| P-W11 | Windows / S4 | reuse P-W10 | `/*/*/RemoteScriptContractTests/C849_Seed_refuses_invalid_donors_and_partial_payloads` | 1 | 1 pass after closure repair |
| P-W12 | Windows / S4 | reuse P-W10 | `/*/*/RemoteScriptContractTests/C849_Seed_publishes_complete_payloads_before_its_marker` | 1 | 1 pass with current publication contract |
| P-L12 | Linux / S4 | tests/Antiphon.Tests -> bin-c1210-linux-s4/ | `/*/*/RemoteScriptContractTests/C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | 1 | 1 pass; no Windows-only workaround |
| P-L13 | Linux / S4 | reuse P-L12 | `/*/*/RemoteScriptContractTests/C849_Seed_refuses_invalid_donors_and_partial_payloads` | 1 | 1 pass |
| P-L14 | Linux / S4 | reuse P-L12 | `/*/*/RemoteScriptContractTests/C849_Seed_publishes_complete_payloads_before_its_marker` | 1 | 1 pass |
| P-W13 | Windows / S4 | reuse P-W10 | `/*/*/RemoteScriptContractTests/C983_Required_jq_driver_rejects_successful_false_probe` | 1 | 1 pass; child vector roster intact |
| P-W14 | Windows / S4 | reuse P-W10 | `/*/*/RemoteScriptContractTests/C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers` | 6 | 6 pass; record duration and child ownership |
| P-L15 | Linux / S4 | reuse P-L12 | `/*/*/RemoteScriptContractTests/C983_Required_jq_driver_rejects_successful_false_probe` | 1 | 1 pass; resolves historical Linux sample obligation |
| P-L16 | Linux / S4 | reuse P-L12 | `/*/*/RemoteScriptContractTests/C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers` | 6 | 6 pass |

Plan must verify expanded roster/count floors from source/listing before commissioning and use the importer-required CP/After/Build/Group/Filter/Covers/Expect/Min/EstimatedMinutes table. Do not treat this proposal as a completed Linux validation. S3's permitted capability skips require their own roster evidence; the strict zero-skip clean-source validator must not be claimed green for that conditional row. If another platform failure appears after LF repair, confirm it at unchanged base by exact method before naming a new cause.

## Rerun and custody

Diagnostic manifests are retained as ignored evidence: `.antiphon/c1210-triage.yaml` and `.antiphon/c1210-followup.yaml`. Tool bootstrap was the one unlisted isolated build; it used build-slot.ps1, bin-c1210-tool/, UseAppHost=false, zero errors and one existing CS8602 warning. Diagnostic dotnet-dump installation also took a granted slot. Two task-owned heap dumps completed and were analyzed. No source was edited during any build/test/dump run; every foreground launcher/session was awaited and each checkpoint executor reached done before report edits. No loaded repetitions or positive-control mutations ran.

To reproduce after rebuilding the deleted outputs, use the gated isolated tool bootstrap followed by:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1210-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1210-tool/ --property:UseAppHost=false --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1210-tool/ --property:UseAppHost=false -- run .antiphon/c1210-triage.yaml --row-timeout 3m --total-timeout 40m --serial --keep-outputs --expected-source-sha <full-current-clean-SHA> --max-wait 50s
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1210-tool/ --property:UseAppHost=false -- run .antiphon/c1210-followup.yaml --serial --keep-outputs --expected-source-sha <full-current-clean-SHA> --max-wait 50s
```

Use `wait <run-id> --max-wait 50s` until done after exit 75. The checkpoint tool itself owns the build-slot gate. Evidence TRX/JSON/log/dump files remain gitignored. Only this report is committed. Build-output cleanup and final Git publication are recorded by the completion amendment below; no pre-existing or caller evidence was deleted. No operational restart, host policy change, deployment, external message or board mutation occurred. Caller standing authority was sufficient; no further approval was requested.

## Unedited CHECKPOINT provenance

### 20261013-030351-6bb2
```text
run: 20261013-030351-6bb2   manifest: C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\manifest.resolved.yaml
commit: a7dc8596b082b524bd76074a05223d164d4bf255  branch: feat/card-task-8db33c5e  worktree: C:\Antiphon\worktrees\card-task-8db33c5e  host: Microsoft Windows 10.0.19045 cores=8
source: a7dc8596b082b524bd76074a05223d164d4bf255 state=clean buildSource=verified
CHECKPOINT CP-1 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=ok filter=/*/*/C1008PlatformContractTests/* executed=6 passed=4 failed=2 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/ChannelOutboundContractTests/* executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-2\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/ChannelOutboundStorageTests/* executed=32 passed=30 failed=2 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-3\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/CodexRootExecContractTests/* executed=4 passed=2 failed=2 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-4\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/CodexRunnerImageContractTests/* executed=13 passed=10 failed=3 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-5\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/GrokRunnerImageContractTests/* executed=26 passed=26 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-6\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/GrokSha256PinTests/* executed=11 passed=0 failed=11 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-7\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/HostJqPrerequisiteScriptTests/* executed=39 passed=0 failed=39 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-8\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/JqRunnerImageContractTests/* executed=18 passed=2 failed=16 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-9\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/RemoteScriptContractTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=3m slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=a7dc8596b082b524bd76074a05223d164d4bf255 build=reused filter=/*/*/RemoteScriptContractTests/C983_Required_jq_driver_rejects_successful_false_probe executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-030351-6bb2\rows\CP-11\run.trx slot=granted waited=0s dirty=0 source=a7dc8596b082b524bd76074a05223d164d4bf255 sourceState=clean buildSource=verified
```
### 20261013-031452-5770
```text
run: 20261013-031452-5770   manifest: C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-031452-5770\manifest.resolved.yaml
commit: 0057e1997b526925a01c88dbad3fc3530a97aed8  branch: feat/card-task-8db33c5e  worktree: C:\Antiphon\worktrees\card-task-8db33c5e  host: Microsoft Windows 10.0.19045 cores=8
source: 0057e1997b526925a01c88dbad3fc3530a97aed8 state=clean buildSource=verified
CHECKPOINT CP-12 commit=0057e1997b526925a01c88dbad3fc3530a97aed8 build=ok filter=/*/*/RemoteScriptContractTests/((C1191_Lock_probe_is_created_before_uid_handoff)|(C1192_Apphost_mask_is_an_empty_exact_volume_mount)|(C1192_Cleanup_releases_only_registered_current_run_resources)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume)|(C1192_Foreign_volume_is_refused_after_create)|(C1192_Harness_cleanup_refuses_ancestor_swap)|(C1192_Harness_cleanup_stays_inside_its_private_directory)|(C1192_Helper_argv_keeps_discarded_env_file_visible)|(C1192_Lock_probe_distinguishes_shared_and_private_scratch)|(C1192_Npm_seed_install_and_refill_use_work_volume)|(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed)|(C1192_Partial_workspace_acquisition_is_cleaned)|(C1192_Race_barriers_observe_volume_state)|(C1192_Race_programs_and_feed_share_the_work_volume)|(C1192_Real_cache_cases_keep_the_host_payload_backend)|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest)|(C1192_Summary_validator_runs_under_the_production_interpreter)|(C1192_Toxic_probe_stays_in_work_volume)|(C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy)|(C1192_Workspace_registration_is_absent_only_and_survives_prepare)|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content)|(C849_Prune_refuses_stale_or_busy_authority)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container)|(C849_Seed_publishes_complete_payloads_before_its_marker)|(C849_Seed_refuses_invalid_donors_and_partial_payloads)|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp)|(C913_Seed_accepts_complete_ordinary_packages_without_framework_packs))&(*) executed=27 passed=0 failed=27 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-031452-5770\rows\CP-12\run.trx slot=granted waited=0s dirty=0 source=0057e1997b526925a01c88dbad3fc3530a97aed8 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=0057e1997b526925a01c88dbad3fc3530a97aed8 build=reused filter=/*/*/RemoteScriptContractTests/C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers executed=6 passed=6 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-031452-5770\rows\CP-13\run.trx slot=granted waited=0s dirty=0 source=0057e1997b526925a01c88dbad3fc3530a97aed8 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=0057e1997b526925a01c88dbad3fc3530a97aed8 build=reused filter=/*/*/C1008PlatformContractTests/* executed=6 passed=6 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-8db33c5e\.antiphon\checkpoints\20261013-031452-5770\rows\CP-14\run.trx slot=granted waited=0s dirty=0 source=0057e1997b526925a01c88dbad3fc3530a97aed8 sourceState=clean buildSource=verified
```

## Completion amendment

The checkpoint cleaner removed the 28 bin-c1210-triage directories. Follow-up build ID `diag` differed from its output name, so `clean --run` did not match bin-c1210-followup. A cleanup-only manifest corrected the ID mapping; its dry run was compared exactly with the 28 producer paths from build.log plus the one bootstrap-tool output. All 29 were then removed through the checkpoint cleaner and their absence verified. Two raw PowerShell recursive deletion commands had been automatically rejected with `blocked by policy`, without a more specific stated reason; the verified checkpoint cleanup completed successfully. The tool copy under ignored `.antiphon/c1210-cleanup-tool` and the diagnostic evidence remain. No ordinary bin/obj, original CARD-1171 evidence or caller output was deleted.

All checkpoint executors reached phase=done and all foreground commands were joined. Only this Markdown report is in the Git diff. The report, method table, raw receipts and proposed slices are complete; the broad Unit timeout remains an explicitly unresolved qualification obligation. No cards were created.
--- next stage ---
next: plan
handoff: First slice S1: native Linux entry admission for HostJq/Jq/Grok SHA/Codex heal methods, with exact Windows skip-roster audit and native Linux green rows. Formalize S2 LF fixture fidelity, S3 Windows link capability and S4 stale payload harness slices using the proposed closed rows. Preserve the unresolved historical whole-lane timeout; no permanent Windows child hang was proven.
artifact: docs/superpowers/reports/2026-10-13-card-1210-windows-unit-triage.md


