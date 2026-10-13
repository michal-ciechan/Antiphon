# CARD-1210 Windows Unit triage

Diagnostic scope committed before execution. No production or test source changes.

Inherited evidence: CARD-1171 base 118bba94f1dae3499b84260b26d062cd75e1a6c4; 100 failed results / 89 methods. Native Windows class-isolated triage pending. ANTIPHON_TASK_TOKEN present; checkpoint tool will bind the run to this task.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | D1 | `tests/Antiphon.Tests -> bin-c1210-triage/` | C1008PlatformContractTests | `/*/*/C1008PlatformContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-2 | D1 | CP-1 | ChannelOutboundContractTests | `/*/*/ChannelOutboundContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-3 | D1 | CP-1 | ChannelOutboundStorageTests | `/*/*/ChannelOutboundStorageTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-4 | D1 | CP-1 | CodexRootExecContractTests | `/*/*/CodexRootExecContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-5 | D1 | CP-1 | CodexRunnerImageContractTests | `/*/*/CodexRunnerImageContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-6 | D1 | CP-1 | GrokRunnerImageContractTests | `/*/*/GrokRunnerImageContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-7 | D1 | CP-1 | GrokSha256PinTests | `/*/*/GrokSha256PinTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-8 | D1 | CP-1 | HostJqPrerequisiteScriptTests | `/*/*/HostJqPrerequisiteScriptTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-9 | D1 | CP-1 | JqRunnerImageContractTests | `/*/*/JqRunnerImageContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-10 | D1 | CP-1 | RemoteScriptContractTests | `/*/*/RemoteScriptContractTests/*` | native triage | nonzero diagnostic results | 1 | 1 | true |
| CP-11 | D1 | CP-1 | hang-probe | `/*/*/RemoteScriptContractTests/C983_Required_jq_driver_rejects_successful_false_probe` | exact hang probe | completes or bounded timeout | 1 | 1 | true |

The diagnostic run overrides row timeout to 3 minutes and total timeout to 40 minutes. These are observation limits, not test assertion changes.

## Inherited method roster

- Antiphon.Tests.Application.ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries
- Antiphon.Tests.Application.ChannelOutboundStorageTests.C1059_Source_reads_reject_file_and_directory_links
- Antiphon.Tests.Application.ChannelOutboundStorageTests.Output_rename_and_link_swap_after_inspection_seals_the_opened_file
- Antiphon.Tests.Infrastructure.CodexRootExecContractTests.Entrypoint_heal_chowns_the_tmp_directory_once
- Antiphon.Tests.Infrastructure.CodexRootExecContractTests.Entrypoint_heal_never_names_a_path_outside_tmp
- Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts
- Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs
- Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing
- Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_accepts_matching_file
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_devnull_second_line
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_embedded_newline
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_empty_download
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_empty_pin
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_long_pin
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_multiline_pin
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_short_pin
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_tampered_file
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_trailing_newline
- Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_uppercase_pin
- Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_accepts_canonical_file_and_alias
- Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_records_found_and_resolved_paths
- Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_rejects_canonical_leaf_symlink
- Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_rejects_home_shadow
- Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr
- Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact
- Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Check_has_no_install_effects
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Check_qualifies_deployment_shell
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Check_rejects_canonical_leaf_symlink
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Explicit_tool_phases_never_deploy
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Final_recycle_guard_remains
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_is_fresh_for_each_entry
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_precedes_every_phase
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_admits_host_prerequisites
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_cleans_only_owned_staging
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_publishes_complete_no_clobber
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_refuses_unsafe_destination
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requalifies_published_jq
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requires_canonical_source
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requires_missing_jq
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_serializes_and_rechecks
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_verifies_download_before_use
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_persistence_is_required
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_rejects_canonical_lookup_with_unapproved_target
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_requires_complete_current_proof
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Transport_is_bounded_and_reaped
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Transport_preserves_secret_custody
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Changed_leaf_cannot_emit_success_proof
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Check_rejects_canonical_hardlink
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Digest_is_bound_to_open_descriptor
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Final_link_checks_are_independent
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Final_permission_checks_are_independent
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Identity_comparisons_include_device
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Installed_pin_is_checked_before_execution
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Metadata_syntax_refuses
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Observation_failures_are_invalid
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Open_descriptor_must_match_canonical_leaf
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Pre_execution_link_checks_are_independent
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Proof_is_buffered_until_final_admission
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Provision_removes_only_owned_stage_link
- Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Staged_unlink_failure_refuses
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1191_Lock_probe_is_created_before_uid_handoff
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Apphost_mask_is_an_empty_exact_volume_mount
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Cleanup_releases_only_registered_current_run_resources
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Fixture_seed_and_rerun_keep_payloads_in_volume
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Foreign_volume_is_refused_after_create
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Harness_cleanup_refuses_ancestor_swap
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Harness_cleanup_stays_inside_its_private_directory
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Helper_argv_keeps_discarded_env_file_visible
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Lock_probe_distinguishes_shared_and_private_scratch
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Npm_seed_install_and_refill_use_work_volume
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Partial_workspace_acquisition_is_cleaned
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Race_barriers_observe_volume_state
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Race_programs_and_feed_share_the_work_volume
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Real_cache_cases_keep_the_host_payload_backend
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Summary_validator_runs_under_the_production_interpreter
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Toxic_probe_stays_in_work_volume
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Workspace_registration_is_absent_only_and_survives_prepare
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Saved_donor_archive_is_checked_and_imported_without_a_container
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Seed_refuses_invalid_donors_and_partial_payloads
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp
- Antiphon.Tests.Scripts.RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs

## First diagnostic result and bounded follow-up

Run 20261013-030351-6bb2 at a7dc8596b082b524bd76074a05223d164d4bf255: CP-1 4/2, CP-2 2/0, CP-3 30/2, CP-4 2/2, CP-5 10/3, CP-6 26/0, CP-7 0/11, CP-8 0/39, CP-9 2/16 (passed/failed); CP-10 timeout at 3 minutes without TRX; CP-11 exact C983 passed 1/0 in 34 seconds. All completed rows have clean source and verified build binding.

Heap dump identifies C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers in LinuxShell/Process.WaitForExit and semaphore wait nodes. C983 is not proven to hang. Extra narrow diagnostics are required because the full remote class could not finish.

Follow-up CP-12 selects exactly the inherited RemoteScriptContractTests methods (26); CP-13 selects exactly C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers (6 argument cases), bounded at 7 minutes. CP-14 requalifies full C1008PlatformContractTests with a real Windows apphost. The first diagnostic manifest intentionally reproduced CARD-1171 UseAppHost=false; its two apphost refusals are not product defects. No assertion/deadline or source implementation changes.
