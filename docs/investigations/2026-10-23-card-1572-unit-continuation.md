# CARD-1572 bounded Unit census continuation

The full Unit census could not be completed within the commissioned 90-minute budget. The three previously unrun groups (141 classes) were executed in bounded pieces, and their canceled host outcomes were recovered. One inherited fixture cause was repaired and narrowly verified. Independent C1514 portability and legacy-roster timeout failures remain, alongside the explicit remote-script pending inventory; no full-lane green or rc qualification is claimed.

## Identity and evidence

Task fb7c4a41; branch feat/card-task-fb7c4a41; baseline e4928494298b304507fdf2e520445c98e7219211; tested census source d39433fc2ed59412e122e27a1ff5d4964c329191; repaired source 30b41c64780f5204102d3133c457fcc1aa287c43. Host 0c18bb7f591e, Debian Linux, SDK 10.0.402, runtime 9.0.21, TUnit 1.44.0/MTP 2.2.2. Exact selectors, source and DLL hashes, timestamps, host/load and argv are retained per run under /work/worktrees/task-fb7c4a41/.antiphon/1572-cont. Previous report: /work/worktrees/task-6422905c/.antiphon/1572/../../docs/investigations/2026-10-23-card-1572-unit-lane-debug.md (canonical previous checkout report is /work/worktrees/task-6422905c/docs/investigations/2026-10-23-card-1572-unit-lane-debug.md). The inherited six repairs are unchanged. No whole lane, Review, tag or release script ran.

One shared baseline build: slot queue 60s, compile 370.55s, zero errors. Repair rebuild: 205.62s, zero errors. Rc scratch compile: 195.12s, zero errors. Compiler/analyzer warnings remain in the logs. Build/test drivers all used host build slots. Test selections were sequential; tracked source remained frozen during each. Policy safe-environment clearing was applied. The last collection window reduced remote pack caps to 300s and text checks to 60s, within a 600s outer budget; the two final rolling methods used 120s caps. Two supplemental remote packs then used 180s caps, and the last pack used 120s to reserve reporting time within the commissioned budget. Earlier recovery ended at its group-boundary reservation with two methods still unrun, then those exact methods were resumed in the final window. Provisional hard collection caps were bounded samples under this Debug task, not measured p95; no test timeout was raised.

## Findings and repair

- **Fixed, inherited fixture drift.** Fifteen shape-conflict parameters failed independently before unit-08's deadline: negative qualification mismatch. CARD-1370 commit 3a7ed451668051b3627b8802ed9005e7bf3db327. The validator now returns report_failed for non-green runs before source eligibility. Repair 30b41c64780f5204102d3133c457fcc1aa287c43 pins that refusal and independently checks source remains ineligible; user-byte/index preservation, failure verdict/reason and missing-receipt assertions remain. No production behavior changed. Red witness was committed separately in f262948b8. Fresh shape-green2: 17 passed, zero failed/skipped, 258.804s. Initial shape-green selected zero (exit 8, 3.297s); it is invalid evidence, retained separately.
- **Unresolved, fixture/platform integration.** TestPhaseSummaryTests.C1514T_PS51AndRerun failed at 30b41c64780f5204102d3133c457fcc1aa287c43, outside a deadline, in deferred-02. C1514NativeProbe invokes the nightly Windows owner on Linux; NativeProcessHooks is absent, and the owner explicitly refuses non-Windows even if loaded. The Linux plan CP-6 expects this test portable. Relevant history: 644a7c44e introduced the probe; 15e8e5317859374c7c9ebae651ada24b20d32655 imported the real nightly entry point; f5723b59d9038427a3990f0d929b980f824b18ca adjusted summary reading. No bisect is claimed. Correct repair needs a real Linux-owned launch path preserving nine actual probe results, complete timing artifacts and custody. Do not fake custody or skip assertions. The other twelve deferred helper cases passed. Board duplicate search failed with connection timeout at GET /api/boards?includeArchived=true; no absence or new-card write is claimed. Caller should triage with CARD-1514/CARD-1572.
- **Deadline contamination, recovered.** Earlier C1364 post-removal and cleanup results were interrupted: fresh six method runs all passed. unit-08 C994_Absent_cleanup_is_idempotent exited 143 at the collection deadline; all eighteen host recovery cases later passed (six each in 136.256/275.622/181.247s). No product regression is inferred from those interrupted results.
- **Unresolved, fixture performance.** RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain failed its unchanged 600s TUnit timeout before the 620s collector deadline; this was a native TUnit timeout, not a collector kill. Complete TRX: one failed, zero passed/skipped; group wall 606.702s, case duration about 601.060s. It serially runs four jq harness modes (31 assertions each), and owned process observations showed active progress into the second mode at 350s. This is measured expensive fixture work, not a demonstrated production logic regression. Split the four modes into separately bounded cost rows/methods while preserving their assertions and the CARD-1040 frozen qualification contract; do not raise the timeout or silently change that plan. No fix was attempted without enough time for source/rc compiles and all required greens.
- **Collection/performance limits.** Group unit-08 stopped at 620.302s: 537 native passes, fifteen independent shape reds, one cancellation-adjacent failure and seventeen missing terminal events; no complete TRX. Later shape/host repairs close those identities without relabeling the original run. A complete TRX is not manufactured for the stopped group. Remote and rolling limits below remain explicit; timeout/cancellation is not assertion-regression proof.

## Completed versus partial execution

Unit-09: all 45 classes, 565 passed / 0 failed / 5 platform skips, 538.379s. Unit-10: all 64 classes, 589/0/9, 201.601s; 28 passing Integration cases in mixed MatrixDefaultComplexityCreateTests were accidentally included by inherited class-only selection. The exact intended Unit subset is 561/0/9, all 570 intended Unit identities. Keep those 28 supplemental results outside the Unit census; future filters should include the Unit predicate. unit-08 had 32 classes and its remaining identities were recovered individually as above.

The combined UID inventory joins the saved 6,075 Unit identities to chronological native diagnostics from both tasks. It is an attributed census across different source SHAs, not one clean same-source receipt. Current latest states: {"intended":6075,"terminal":5755,"passed":5704,"skipped":43,"red":8,"missing":320}. Two latest red states are the independent fixture failures above; six are prior canceled RemoteScriptContractTests identities awaiting a clean rerun. Remote pending: 320 missing terminal records plus six canceled identities, 326 total. Fresh completed remote pieces passed all 144 cases with zero failures/skips. Saved native diagnostics and run manifests remain the authority; native event spans are not TRX execution durations. Skips retain platform reasons.

| Run | Source | Expected | Wall seconds | Exit | TRX P/F/S |
|---|---|---:|---:|---:|---|
| unit-08 | d39433fc2ed5 | 570 | 620.302 | 124 | no complete TRX |
| unit-09 | d39433fc2ed5 | 570 | 538.379 | 0 | 565/0/5 |
| unit-10 | d39433fc2ed5 | 570 | 201.601 | 0 | 589/0/9 |
| shape-green | 30b41c64780f | 17 | 3.297 | 8 | 0/0/0; zero selected, invalid |
| shape-green2 | 30b41c64780f | 17 | 258.804 | 0 | 17/0/0 |
| bound-1 | 30b41c64780f | 1 | 22.092 | 0 | 1/0/0 |
| bound-2 | 30b41c64780f | 1 | 23.994 | 0 | 1/0/0 |
| bound-3 | 30b41c64780f | 1 | 34.638 | 0 | 1/0/0 |
| bound-4 | 30b41c64780f | 1 | 152.708 | 0 | 1/0/0 |
| bound-5 | 30b41c64780f | 1 | 173.092 | 0 | 1/0/0 |
| bound-6 | 30b41c64780f | 1 | 33.6 | 0 | 1/0/0 |
| host-recover1 | 30b41c64780f | 6 | 136.256 | 0 | 6/0/0 |
| host-recover2 | 30b41c64780f | 6 | 275.622 | 0 | 6/0/0 |
| host-recover3 | 30b41c64780f | 6 | 181.247 | 0 | 6/0/0 |
| deferred-02 | 30b41c64780f | 13 | 62.625 | 2 | 12/1/0 |
| rolling-1 | 30b41c64780f | 1 | 49.926 | 0 | 1/0/0 |
| rolling-2 | 30b41c64780f | 1 | 52.409 | 0 | 1/0/0 |
| rolling-3 | 30b41c64780f | 1 | 49.403 | 0 | 1/0/0 |
| rolling-4 | 30b41c64780f | 1 | 9.25 | 0 | 1/0/0 |
| rolling-5 | 30b41c64780f | 1 | 6.731 | 0 | 1/0/0 |
| rolling-6 | 30b41c64780f | 1 | 19.035 | 0 | 1/0/0 |
| rolling-7 | 30b41c64780f | 1 | 606.702 | 2 | 0/1/0 |
| rolling-8 | 30b41c64780f | 1 | 6.478 | 0 | 1/0/0 |
| rolling-9 | 30b41c64780f | 1 | 31.367 | 0 | 1/0/0 |
| remote-00 | 30b41c64780f | 38 | 8.9 | 0 | 38/0/0 |
| remote-02 | 30b41c64780f | 16 | 264.118 | 0 | 16/0/0 |
| remote-11 | 30b41c64780f | 26 | 117.373 | 0 | 26/0/0 |
| remote-15 | 30b41c64780f | 18 | 26.755 | 0 | 18/0/0 |
| remote-04 | 30b41c64780f | 46 | 85.277 | 0 | 46/0/0 |

No test host, Docker/database, readiness, setup/clone/migration, IO/HTTP/CLI, or teardown phase is credited as zero when it was not instrumented. Those subphases remain unknown/unattributed. Per-test TRX body durations and outer launch wall are separate; class wall spans can contain scheduler gaps and must not be used as standalone serial cost estimates. Build admission is excluded from group wall.

## Pending exact identities

- Antiphon.Tests.Scripts.RemoteScriptContractTests: 326 pending/latest-red identities.
  - C1361_Host_census_tolerates_open_status_changes_between_list_and_detail
  - C1361_Host_census_tolerates_open_status_changes_between_passes
  - C1066_Fixture_tree_fault_reaches_validator_under_nounset
  - C1066_Fixture_npm_install_uses_argument_log_under_nounset
  - C1066_Fixture_warm_probe_uses_argument_log_under_nounset
  - C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit
  - C1191_Lock_probe_is_created_before_uid_handoff
  - C1191_Fixture_exit_removes_owned_workspace_without_host_sudo
  - C1192_Fixture_has_no_host_payload_tree_or_payload_bind
  - C1192_Finalization_precedes_success_and_preserves_primary_failure
  - C1192_Evidence_export_preserves_fixture_roster_without_payloads
  - C1208_Legacy_network_replacement_is_untouched
  - C1208_Unacquired_roster_is_untouched
  - C1208_Acquired_objects_are_removed
  - C1208_Recorded_container_id_does_not_delete_replacement
  - C1208_Legacy_owned_markers_are_migrated
  - C1208_Unknown_identity_retains_residue
  - C1208_Wrong_cidfile_preserves_foreign_container
  - C1208_Recorded_id_with_changed_labels_is_untouched
  - C1208_Recorded_container_inspect_failure_refuses_cleanup
  - C1208_Wrong_helper_id_preserves_foreign_container
  - C1208_Wrong_network_id_is_untouched
  - C1208_Recorded_network_inspect_failure_refuses_cleanup
  - C1192_Cleanup_releases_only_registered_current_run_resources
  - C1192_Partial_workspace_acquisition_is_cleaned
  - C1192_Toxic_probe_stays_in_work_volume
  - C1192_Summary_validator_runs_under_the_production_interpreter
  - C1192_Foreign_volume_is_refused_after_create
  - C1192_Harness_cleanup_stays_inside_its_private_directory
  - C1192_Harness_cleanup_refuses_ancestor_swap
  - C1192_Helper_argv_keeps_discarded_env_file_visible
  - C1192_Race_programs_and_feed_share_the_work_volume
  - C1192_Race_barriers_observe_volume_state
  - C1192_Lock_probe_distinguishes_shared_and_private_scratch
  - C1192_Npm_seed_install_and_refill_use_work_volume
  - C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed
  - C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed
  - C1192_Real_cache_cases_keep_the_host_payload_backend
  - C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy
  - C1192_Fixture_seed_and_rerun_keep_payloads_in_volume
  - C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest
  - C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal
  - C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files
  - C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution
  - C1194_Fixture_budget_scale_refuses_empty_observation
  - C1192_Apphost_mask_is_an_empty_exact_volume_mount
  - C1195_Fixture_import_chmod_restores_volume_root
  - C1195_Seed_import_chmod_restores_volume_root
  - C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content
  - C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work
  - C913_Seed_accepts_complete_ordinary_packages_without_framework_packs
  - C913_Full_marker_binds_verified_recovery_before_publication
  - C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct
  - C913_Prune_validates_recovery_without_refilling_image_packs
  - C913_Fixture_uses_owned_payloads_and_proven_absent_temp
  - C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims
  - C1087_Host_census_filters_and_names_cause
  - C1008_Recycle_exact_default_volumes
  - C1008_Recycle_refuses_references_and_unknown_census
  - C1008_Recycle_audits_work_as_1654
  - C1008_Recycle_refuses_unpublished_and_dirty_work
  - C1008_Recycle_refuses_uninspectable_git
  - C1008_Recycle_reaps_dirty_scratch
  - C1008_Recycle_audits_promisor_checkout
  - C1105_Git_audit_recovery_tips
  - C1105_Git_audit_seed_content
  - C1105_Git_audit_index_content
  - C1105_Git_audit_object_completeness
  - C1105_Git_audit_overall_timeout
  - C1316_Recycle_audit_cap_clears_the_standing_volume
  - C1105_Git_audit_volume_scale
  - C1105_Git_audit_resume_preserves_proof
  - C1105_Git_audit_hidden_locations
  - C1335_Mirror_owner_stamp_in_linked_admin
  - C1008_Retire_temp_rechecks_absence_and_retirement
  - C1008_Recycle_dry_run_never_mutates
  - C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes
  - C1105_Redeploy_accepts_previous_generation_and_requires_new_bind
  - C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout
  - C1105_V218_dry_run_reaches_the_generation_line
  - C1105_Relative_dry_run_refuses_before_removal
  - C1105_Generation_identity_refuses_mismatch
  - C1105_Main_state_init_without_session_runner_refuses
  - C1105_Compose_cmp_refuses_before_removal
  - C1105_Resume_identity_refusals
  - C1105_Resume_state_init_image_follows_the_journal
  - C1105_Resume_foreign_leftover_is_not_an_image_mismatch
  - C1105_Resume_digest_pins_previous_sha12
  - C1105_Resume_post_removal_census_tracks_open_work
  - C849_front_door_passes_every_full_case_name_to_the_invoker
  - Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner
  - Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner
  - Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode
  - Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents
  - Host_lane_ownership_reset_never_enters_the_codex_home
  - C849_Cache_cases_use_only_the_validated_host_lane
  - C849_Cache_prepare_is_idempotent_and_preserves_payloads
  - C849_Cache_prepare_refuses_foreign_or_unsafe_roots
  - C849_Prune_refuses_stale_or_busy_authority
  - C849_Closure_preflight_names_a_missing_dependency
  - C849_Seed_publishes_complete_payloads_before_its_marker
  - C849_Seed_refuses_invalid_donors_and_partial_payloads
  - C849_Saved_donor_archive_is_checked_and_imported_without_a_container
  - C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters
  - C849_Saved_donor_rejects_declared_size_bomb_before_writing
  - C849_Retired_guard_refuses_docker_ps_errors
  - C0995_Deploy_temp_checks_host_absence_before_checkout
  - C0995_Cold_ready_seed_refuses_a_temp_container
  - C849_Cold_marker_reuse_writes_gate_receipts
  - C849_Deploy_temp_names_saved_import_when_seed_is_missing
  - C849_Deploy_prepares_and_verifies_before_acceptance
  - C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo
  - C849_Prune_preview_is_read_only_and_bounded
  - C997_Cache_budget_gate_uses_controlled_disk_readings
  - C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance
  - C997_Saved_donor_probe_failure_refuses_before_copy
  - C849_Prune_and_rollback_retain_roots_and_recovery
  - C849_Cache_receipts_exclude_credentials_and_payloads
  - C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots
  - C912_Cold_seed_refuses_when_created_volume_disappears_before_init
  - C912_Cold_volumes_seed_accepts_busy_main_without_packages
  - C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets
  - C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries
  - C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit
  - C912_Cold_marker_has_distinct_validation_and_full_context_refusal
  - C1008_Recycle_resume_requires_matching_receipt
  - C1008_Recycle_receipt_records_disk_and_partial_failure
  - C1008_Retire_temp_reclaims_below_cache_disk_gate
- Antiphon.Tests.TestHelpers.TestPhaseSummaryTests: 1 pending/latest-red identities.
  - C1514T_PS51AndRerun
- Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests: 1 pending/latest-red identities.
  - C1008_Legacy_rolling_and_jq_rosters_remain

The machine-readable list, including parameter UIDs and latest source/run, is /work/worktrees/task-fb7c4a41/.antiphon/1572-cont/combined-coverage.json. Discovery-only, canceled, missing and deadline-limited results remain pending.

## True per-case shell timing and proposed cost rows

All rows are n=1 provisional samples, not measured p95. C1364 rows are fresh at 30b41c64780f5204102d3133c457fcc1aa287c43; HostJq rows are unchanged historical complete TRX at ffb1031fab448cc09929f8142b41f4fc46c923c9, /work/worktrees/task-6422905c/.antiphon/1572/unit-05/unit-05.trx. The previous brief described its group as interrupted, but unit-05's completed 39-case TRX proves this class finished; it was not rerun solely to pay its timing cost again. These proposals change neither tests/test-execution-policy.json nor any timeout or checkpoint manifest. CARD-1404 owns measured packing; CARD-1514 owns fixture/phase instrumentation.

Suggested per-case serial cost is ceil(TRX seconds); provisional execution cap is approximately twice the observation plus 10 seconds of launch allowance, subject to measured p95 before adoption. Internal vector setup/CLI costs are unknown; preserve fresh mutable fixture state, all vectors and boundary assertions. Share only immutable parsing/compiled helper setup.

| Class / case | TRX seconds | Proposed cost seconds | Provisional collection seconds |
|---|---:|---:|---:|
| C1364BoundLandWaitProofTests.C1364_Host_cleanup_reports_waited | 28.063 | 29 | 67 |
| C1364BoundLandWaitProofTests.C1364_Host_recycle_admission_reports_waited | 147.247 | 148 | 305 |
| C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited | 167.900 | 168 | 346 |
| C1364BoundLandWaitProofTests.C1364_Wrapper_cleanup_reports_waited | 18.946 | 19 | 48 |
| C1364BoundLandWaitProofTests.C1364_Wrapper_redeploy_reports_waited | 16.520 | 17 | 44 |
| C1364BoundLandWaitProofTests.C1364_Wrapper_retire_volumes_reports_waited | 29.588 | 30 | 70 |
| HostJqPrerequisiteScriptTests.C1025_Check_has_no_install_effects | 5.014 | 6 | 21 |
| HostJqPrerequisiteScriptTests.C1025_Check_qualifies_deployment_shell | 5.392 | 6 | 21 |
| HostJqPrerequisiteScriptTests.C1025_Check_rejects_canonical_leaf_symlink | 3.013 | 4 | 17 |
| HostJqPrerequisiteScriptTests.C1025_Explicit_tool_phases_never_deploy | 13.337 | 14 | 37 |
| HostJqPrerequisiteScriptTests.C1025_Final_recycle_guard_remains | 6.645 | 7 | 24 |
| HostJqPrerequisiteScriptTests.C1025_Preflight_is_fresh_for_each_entry | 90.005 | 91 | 191 |
| HostJqPrerequisiteScriptTests.C1025_Preflight_precedes_every_phase | 117.057 | 118 | 245 |
| HostJqPrerequisiteScriptTests.C1025_Provision_admits_host_prerequisites | 0.489 | 1 | 11 |
| HostJqPrerequisiteScriptTests.C1025_Provision_cleans_only_owned_staging | 8.887 | 9 | 28 |
| HostJqPrerequisiteScriptTests.C1025_Provision_publishes_complete_no_clobber | 3.280 | 4 | 17 |
| HostJqPrerequisiteScriptTests.C1025_Provision_refuses_unsafe_destination | 0.787 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1025_Provision_requalifies_published_jq | 10.417 | 11 | 31 |
| HostJqPrerequisiteScriptTests.C1025_Provision_requires_canonical_source | 11.966 | 12 | 34 |
| HostJqPrerequisiteScriptTests.C1025_Provision_requires_missing_jq | 2.406 | 3 | 15 |
| HostJqPrerequisiteScriptTests.C1025_Provision_serializes_and_rechecks | 1.007 | 2 | 13 |
| HostJqPrerequisiteScriptTests.C1025_Provision_verifies_download_before_use | 2.084 | 3 | 15 |
| HostJqPrerequisiteScriptTests.C1025_Receipt_persistence_is_required | 11.601 | 12 | 34 |
| HostJqPrerequisiteScriptTests.C1025_Receipt_rejects_canonical_lookup_with_unapproved_target | 1.898 | 2 | 14 |
| HostJqPrerequisiteScriptTests.C1025_Receipt_requires_complete_current_proof | 101.701 | 102 | 214 |
| HostJqPrerequisiteScriptTests.C1025_Transport_is_bounded_and_reaped | 6.567 | 7 | 24 |
| HostJqPrerequisiteScriptTests.C1025_Transport_preserves_secret_custody | 5.512 | 6 | 22 |
| HostJqPrerequisiteScriptTests.C1058_Changed_leaf_cannot_emit_success_proof | 8.819 | 9 | 28 |
| HostJqPrerequisiteScriptTests.C1058_Check_rejects_canonical_hardlink | 5.418 | 6 | 21 |
| HostJqPrerequisiteScriptTests.C1058_Digest_is_bound_to_open_descriptor | 0.507 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_False_probe_uses_open_descriptor | 0.931 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_Final_link_checks_are_independent | 0.982 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_Final_permission_checks_are_independent | 0.756 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_Identity_comparisons_include_device | 0.834 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_Installed_pin_is_checked_before_execution | 1.349 | 2 | 13 |
| HostJqPrerequisiteScriptTests.C1058_Metadata_syntax_refuses | 10.602 | 11 | 32 |
| HostJqPrerequisiteScriptTests.C1058_Observation_failures_are_invalid | 5.744 | 6 | 22 |
| HostJqPrerequisiteScriptTests.C1058_Open_descriptor_must_match_canonical_leaf | 2.707 | 3 | 16 |
| HostJqPrerequisiteScriptTests.C1058_Pre_execution_link_checks_are_independent | 1.209 | 2 | 13 |
| HostJqPrerequisiteScriptTests.C1058_Proof_is_buffered_until_final_admission | 0.437 | 1 | 11 |
| HostJqPrerequisiteScriptTests.C1058_Provision_removes_only_owned_stage_link | 1.188 | 2 | 13 |
| HostJqPrerequisiteScriptTests.C1058_Receipt_formatter_uses_open_descriptor | 0.912 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_Staged_unlink_failure_refuses | 0.849 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_True_probe_uses_open_descriptor | 0.909 | 1 | 12 |
| HostJqPrerequisiteScriptTests.C1058_Version_probe_uses_open_descriptor | 0.889 | 1 | 12 |

C1364 measured body sum is 408.264s. Keep the 147.247s admission and 167.900s post-removal matrices separate from the four faster cases. HostJq body sum is 454.105s; separate its three largest cases (117.057/101.701/90.005s) from the other 36 (145.342s together). These are bounded pieces well below 30 minutes; no blanket timeout increase is proposed. RetiredTempContainerHostTests also needs method pieces: its eighteen recovery cases took 593.125s of outer launch wall; the live-proof drift method contains sixteen sequential vectors. RetiredTempContainerScriptTests cost 193.959 body seconds and CheckpointTrackedBaselineTests 260.598 in unit-09.

## Per-class continuation inventory

The table covers all 141 previously unrun classes. Latest outcomes may combine original partial native events and fresh repair TRX; it is not a complete same-source class rerun. Full previous 456-class table is in the prior report.

| Class | Group | Intended Unit cases | Latest P/S/other/missing | Fresh TRX body sum seconds |
|---|---|---:|---|---:|
| Antiphon.Tests.AgentTui.RunnerProbePowerShellFixtureTests | unit-08 | 15 | 15/0/0/0 | unknown (native only) |
| Antiphon.Tests.Agents.AgentRegistryTests | unit-08 | 17 | 17/0/0/0 | unknown (native only) |
| Antiphon.Tests.Agents.GrokLinuxBlockingPromptTests | unit-08 | 11 | 11/0/0/0 | unknown (native only) |
| Antiphon.Tests.Agents.GrokSignInPromptDetectorTests | unit-08 | 17 | 17/0/0/0 | unknown (native only) |
| Antiphon.Tests.ApiKeys.ApiKeyPlaceholderTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.AgentTaskDecisionQuestionTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.AgentTaskDispatcherPredicateTests | unit-08 | 19 | 19/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.ApiErrorClassifierTests | unit-08 | 16 | 16/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.ClaudeEffortByRoleSettingsTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.CommitRecoveryObligationsTests | unit-08 | 17 | 17/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.CompactionContinuationPolicyTests | unit-08 | 19 | 19/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.ComplexityRoutingComposeTests | unit-08 | 16 | 16/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.DelegationScopeLeaseTests | unit-08 | 16 | 16/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.DelegationWorkspaceBoundaryTests | unit-08 | 15 | 15/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.DiagnosticsRedactorTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.OrchestratorInvestigationDetectorTests | unit-08 | 17 | 17/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.ProviderQuotaRefusalTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.RolePolicyReadServiceTests | unit-08 | 21 | 21/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.RolePolicyToolBundleTests | unit-08 | 19 | 19/0/0/0 | unknown (native only) |
| Antiphon.Tests.Application.WorkspaceReservationLivenessTests | unit-08 | 20 | 20/0/0/0 | unknown (native only) |
| Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests | unit-08 | 20 | 20/0/0/0 | unknown (native only) |
| Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests | unit-08 | 17 | 17/0/0/0 | 252.932 |
| Antiphon.Tests.Checkpoints.CheckpointTimingHarnessTests | unit-08 | 21 | 21/0/0/0 | unknown (native only) |
| Antiphon.Tests.Checkpoints.CheckpointToolCopyCleanupTests | unit-08 | 16 | 16/0/0/0 | unknown (native only) |
| Antiphon.Tests.Checkpoints.PlanCoverageHandleTests | unit-08 | 16 | 16/0/0/0 | unknown (native only) |
| Antiphon.Tests.Checkpoints.RunSchedulerTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests | unit-08 | 16 | 16/0/0/0 | unknown (native only) |
| Antiphon.Tests.Infrastructure.HostCleanupExecutionTests | unit-08 | 20 | 20/0/0/0 | unknown (native only) |
| Antiphon.Tests.Infrastructure.JqRunnerImageContractTests | unit-08 | 18 | 18/0/0/0 | unknown (native only) |
| Antiphon.Tests.Infrastructure.LogRetentionTests | unit-08 | 21 | 21/0/0/0 | unknown (native only) |
| Antiphon.Tests.ProcessSpawnLimitTests | unit-08 | 21 | 21/0/0/0 | unknown (native only) |
| Antiphon.Tests.Scripts.RetiredTempContainerHostTests | unit-08 | 21 | 21/0/0/0 | 576.398 |
| Antiphon.Tests.AgentTui.DollarEnvArgTests | unit-09 | 11 | 11/0/0/0 | 0.182 |
| Antiphon.Tests.Agents.FakeLlmApiSelfTests | unit-09 | 12 | 12/0/0/0 | 5.925 |
| Antiphon.Tests.Agents.GrokCredentialStoreTests | unit-09 | 12 | 12/0/0/0 | 0.215 |
| Antiphon.Tests.Agents.GrokStartupReadinessTests | unit-09 | 5 | 5/0/0/0 | 0.789 |
| Antiphon.Tests.Agents.RunnerCodexAdapterReadyTests | unit-09 | 12 | 12/0/0/0 | 3.915 |
| Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests | unit-09 | 12 | 12/0/0/0 | 11.086 |
| Antiphon.Tests.Agents.TestOwnedPtyHostIdentityTests | unit-09 | 12 | 12/0/0/0 | 0.093 |
| Antiphon.Tests.Application.BootStallPolicyTests | unit-09 | 14 | 14/0/0/0 | 0.378 |
| Antiphon.Tests.Application.CardRankingTests | unit-09 | 13 | 13/0/0/0 | 0.043 |
| Antiphon.Tests.Application.CardTaskFileRendererTests | unit-09 | 12 | 12/0/0/0 | 0.534 |
| Antiphon.Tests.Application.ChannelContractsTests | unit-09 | 14 | 14/0/0/0 | 0.055 |
| Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests | unit-09 | 11 | 10/1/0/0 | 0.142 |
| Antiphon.Tests.Application.ComplexityEffectiveComposeTests | unit-09 | 13 | 13/0/0/0 | 1.034 |
| Antiphon.Tests.Application.DelegateBindRefusalRecoveryTests | unit-09 | 11 | 11/0/0/0 | 0.173 |
| Antiphon.Tests.Application.DelegationKindPricingTests | unit-09 | 12 | 12/0/0/0 | 0.053 |
| Antiphon.Tests.Application.DelegationLeaseSettingsTests | unit-09 | 14 | 14/0/0/0 | 0.062 |
| Antiphon.Tests.Application.GrokDeliveryShapeTests | unit-09 | 14 | 14/0/0/0 | 0.060 |
| Antiphon.Tests.Application.HerdrStatusDisagreementTests | unit-09 | 14 | 14/0/0/0 | 0.028 |
| Antiphon.Tests.Application.InstructionFileStampTests | unit-09 | 15 | 15/0/0/0 | 0.319 |
| Antiphon.Tests.Application.LandFailureDiagnosticTests | unit-09 | 12 | 12/0/0/0 | 0.381 |
| Antiphon.Tests.Application.PtyDeliveryCeilingsTests | unit-09 | 14 | 10/4/0/0 | 0.144 |
| Antiphon.Tests.Application.RemoteControlMenuScreenTests | unit-09 | 12 | 12/0/0/0 | 0.057 |
| Antiphon.Tests.Application.SeatOccupancyProjectionTests | unit-09 | 15 | 15/0/0/0 | 0.069 |
| Antiphon.Tests.Application.SpecialistAttemptEvidenceTests | unit-09 | 13 | 13/0/0/0 | 0.348 |
| Antiphon.Tests.Application.StandingPipelinePolicyDocumentationTests | unit-09 | 12 | 12/0/0/0 | 0.398 |
| Antiphon.Tests.Application.SubscriptionQuotaGateTests | unit-09 | 13 | 13/0/0/0 | 0.068 |
| Antiphon.Tests.Application.TrackerSyncSummaryFormatterTests | unit-09 | 13 | 13/0/0/0 | 0.326 |
| Antiphon.Tests.Application.WorkflowEngineParsingTests | unit-09 | 13 | 13/0/0/0 | 1.049 |
| Antiphon.Tests.CachingChatClientTests | unit-09 | 13 | 13/0/0/0 | 0.157 |
| Antiphon.Tests.Checkpoints.BuildSlotClientTests | unit-09 | 13 | 13/0/0/0 | 0.463 |
| Antiphon.Tests.Checkpoints.CheckpointRunOwnershipTests | unit-09 | 12 | 12/0/0/0 | 2.002 |
| Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests | unit-09 | 14 | 14/0/0/0 | 10.823 |
| Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests | unit-09 | 13 | 13/0/0/0 | 260.598 |
| Antiphon.Tests.Checkpoints.PlanCoverageCensusTests | unit-09 | 12 | 12/0/0/0 | 11.286 |
| Antiphon.Tests.Checkpoints.RowRunnerTests | unit-09 | 13 | 13/0/0/0 | 5.500 |
| Antiphon.Tests.Checkpoints.WaitCommandTests | unit-09 | 12 | 12/0/0/0 | 6.638 |
| Antiphon.Tests.Domain.StateMachine.CardStateMachineTests | unit-09 | 12 | 12/0/0/0 | 0.010 |
| Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests | unit-09 | 13 | 13/0/0/0 | 3.259 |
| Antiphon.Tests.Infrastructure.DockerStackDocumentationTests | unit-09 | 15 | 15/0/0/0 | 0.553 |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointCustodyTests | unit-09 | 13 | 13/0/0/0 | 0.681 |
| Antiphon.Tests.Infrastructure.HostCleanupOwnershipTests | unit-09 | 12 | 12/0/0/0 | 0.405 |
| Antiphon.Tests.Scripts.RetiredTempContainerScriptTests | unit-09 | 13 | 13/0/0/0 | 193.959 |
| Antiphon.Tests.Scripts.RollingProductionMountTests | unit-09 | 12 | 12/0/0/0 | 61.910 |
| Antiphon.Tests.TestHelpers.BuildOutputNestingTests | unit-09 | 14 | 14/0/0/0 | 0.050 |
| Antiphon.Tests.TestHelpers.TestTimingRecorderTests | unit-09 | 14 | 14/0/0/0 | 0.290 |
| Antiphon.Tests.Agents.AgentProtocolAdapterFactoryTests | unit-10 | 6 | 6/0/0/0 | 0.064 |
| Antiphon.Tests.Agents.CodexWorkingIndicatorTests | unit-10 | 10 | 10/0/0/0 | 0.064 |
| Antiphon.Tests.Agents.GrokFreshWorktreeQualificationTests | unit-10 | 8 | 8/0/0/0 | 1.106 |
| Antiphon.Tests.Agents.RunnerCodexAdapterSubmitConfirmTests | unit-10 | 8 | 8/0/0/0 | 1.142 |
| Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests | unit-10 | 9 | 9/0/0/0 | 19.362 |
| Antiphon.Tests.Agents.RunnerTerminalSessionGenerationTests | unit-10 | 8 | 8/0/0/0 | 7.424 |
| Antiphon.Tests.Application.AgentPresetsTests | unit-10 | 7 | 7/0/0/0 | 0.072 |
| Antiphon.Tests.Application.AgentTaskLandVerificationEvidenceTests | unit-10 | 9 | 9/0/0/0 | 0.436 |
| Antiphon.Tests.Application.ApiErrorRetryScheduleTests | unit-10 | 7 | 7/0/0/0 | 0.017 |
| Antiphon.Tests.Application.AreaMapLoaderTests | unit-10 | 9 | 9/0/0/0 | 0.338 |
| Antiphon.Tests.Application.AwayDigestFormatterTests | unit-10 | 8 | 8/0/0/0 | 0.076 |
| Antiphon.Tests.Application.BlockedQuestionTests | unit-10 | 7 | 7/0/0/0 | 0.038 |
| Antiphon.Tests.Application.BoardServiceNeedsHumanReviewTests | unit-10 | 7 | 7/0/0/0 | 0.150 |
| Antiphon.Tests.Application.CapacityRecoveryPolicyTests | unit-10 | 8 | 8/0/0/0 | 0.044 |
| Antiphon.Tests.Application.CardAliasNormalizationTests | unit-10 | 7 | 7/0/0/0 | 0.092 |
| Antiphon.Tests.Application.ChannelMachineTurnMatchTests | unit-10 | 10 | 10/0/0/0 | 0.098 |
| Antiphon.Tests.Application.ChannelPromptCorrelationUnitTests | unit-10 | 8 | 8/0/0/0 | 0.220 |
| Antiphon.Tests.Application.CheckpointManifestDocumentationTests | unit-10 | 8 | 8/0/0/0 | 0.397 |
| Antiphon.Tests.Application.CodexLaunchArgsTests | unit-10 | 8 | 8/0/0/0 | 0.062 |
| Antiphon.Tests.Application.DelegateBranchNameTests | unit-10 | 9 | 9/0/0/0 | 4.231 |
| Antiphon.Tests.Application.DelegationQuestionDetectionTests | unit-10 | 8 | 8/0/0/0 | 0.006 |
| Antiphon.Tests.Application.DirectoryBrowseServiceTests | unit-10 | 9 | 4/5/0/0 | 0.556 |
| Antiphon.Tests.Application.DispatchConcurrencyPolicyTests | unit-10 | 8 | 8/0/0/0 | 1.308 |
| Antiphon.Tests.Application.ExpectationPipelineTests | unit-10 | 9 | 9/0/0/0 | 1.438 |
| Antiphon.Tests.Application.GitIndexLockTests | unit-10 | 11 | 11/0/0/0 | 0.420 |
| Antiphon.Tests.Application.GrokComposerScreenTests | unit-10 | 8 | 8/0/0/0 | 0.070 |
| Antiphon.Tests.Application.GrokLaunchArgsTests | unit-10 | 9 | 9/0/0/0 | 0.019 |
| Antiphon.Tests.Application.LandVerificationBuildSlotTests | unit-10 | 10 | 10/0/0/0 | 0.827 |
| Antiphon.Tests.Application.MatrixDefaultComplexityCreateTests | unit-10 | 8 | 8/0/0/0 | 170.967 |
| Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests | unit-10 | 11 | 11/0/0/0 | 2.192 |
| Antiphon.Tests.Application.MechanicalPcContractTests | unit-10 | 11 | 11/0/0/0 | 0.626 |
| Antiphon.Tests.Application.OrchestratorBundleOwnershipTests | unit-10 | 10 | 10/0/0/0 | 0.440 |
| Antiphon.Tests.Application.OrchestratorInstructionsRendererTests | unit-10 | 10 | 10/0/0/0 | 1.347 |
| Antiphon.Tests.Application.PhoneHomeRunnerRetirementIdentityTests | unit-10 | 9 | 9/0/0/0 | 8.760 |
| Antiphon.Tests.Application.PhoneHomeRunnerSettingsValidatorTests | unit-10 | 8 | 8/0/0/0 | 0.134 |
| Antiphon.Tests.Application.PhoneHomeTaskRoutingTests | unit-10 | 9 | 9/0/0/0 | 0.109 |
| Antiphon.Tests.Application.PublishedRoutingDocumentationTests | unit-10 | 9 | 9/0/0/0 | 0.313 |
| Antiphon.Tests.Application.RemoteWorktreeMirrorTests | unit-10 | 9 | 9/0/0/0 | 2.588 |
| Antiphon.Tests.Application.RunnerBranchContractDocumentationTests | unit-10 | 8 | 8/0/0/0 | 1.763 |
| Antiphon.Tests.Application.RunnerCapabilitiesFailureTests | unit-10 | 8 | 8/0/0/0 | 0.036 |
| Antiphon.Tests.Application.ScopeDriftTests | unit-10 | 11 | 11/0/0/0 | 1.552 |
| Antiphon.Tests.Application.ScopeResolverAreaTests | unit-10 | 10 | 10/0/0/0 | 0.565 |
| Antiphon.Tests.Application.SlashCommandCatalogServiceTests | unit-10 | 11 | 11/0/0/0 | 2.764 |
| Antiphon.Tests.Application.TaskReportHousekeepingTests | unit-10 | 11 | 11/0/0/0 | 0.151 |
| Antiphon.Tests.Application.TypedBodySpillTests | unit-10 | 9 | 9/0/0/0 | 0.051 |
| Antiphon.Tests.Checkpoints.BaselineComparerTests | unit-10 | 8 | 8/0/0/0 | 4.018 |
| Antiphon.Tests.Checkpoints.CheckpointEofStallTests | unit-10 | 10 | 10/0/0/0 | 2.588 |
| Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests | unit-10 | 11 | 11/0/0/0 | 27.273 |
| Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests | unit-10 | 10 | 10/0/0/0 | 4.427 |
| Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests | unit-10 | 8 | 8/0/0/0 | 141.434 |
| Antiphon.Tests.Checkpoints.CheckpointSlotContractTests | unit-10 | 11 | 11/0/0/0 | 3.600 |
| Antiphon.Tests.Checkpoints.CheckpointTempScopeTests | unit-10 | 11 | 11/0/0/0 | 0.625 |
| Antiphon.Tests.Checkpoints.PlanCoverageCommandTests | unit-10 | 10 | 10/0/0/0 | 48.561 |
| Antiphon.Tests.Checkpoints.PlanCoverageParserTests | unit-10 | 8 | 8/0/0/0 | 5.956 |
| Antiphon.Tests.Checkpoints.PlanCoveragePcTests | unit-10 | 8 | 8/0/0/0 | 17.341 |
| Antiphon.Tests.Checkpoints.TimeoutTests | unit-10 | 8 | 4/4/0/0 | 0.494 |
| Antiphon.Tests.Infrastructure.CustodyHelperContractTests | unit-10 | 9 | 9/0/0/0 | 0.293 |
| Antiphon.Tests.Infrastructure.GrokSha256PinTests | unit-10 | 11 | 11/0/0/0 | 0.071 |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointReceiptTests | unit-10 | 8 | 8/0/0/0 | 14.225 |
| Antiphon.Tests.Infrastructure.Resilience.HttpReadResilienceTests | unit-10 | 8 | 8/0/0/0 | 2.460 |
| Antiphon.Tests.Infrastructure.Resilience.ResilienceOptionsAndTelemetryTests | unit-10 | 10 | 10/0/0/0 | 1.858 |
| Antiphon.Tests.Infrastructure.Resilience.ResiliencePredicateTests | unit-10 | 8 | 8/0/0/0 | 0.022 |
| Antiphon.Tests.Infrastructure.WorktreeManagerTests | unit-10 | 10 | 10/0/0/0 | 0.067 |
| Antiphon.Tests.TestHelpers.TestSchemaCacheTests | unit-10 | 8 | 8/0/0/0 | 0.348 |

Fresh TRX case details: /work/worktrees/task-fb7c4a41/.antiphon/1572-cont/test-results.json; failures.json; classes.csv. Unit-10 supplemental Integration durations remain in that file and the class sum above; membership is derived from the saved Unit UIDs.

## Rc applicability, hygiene and handoff

Pick 30b41c64780f5204102d3133c457fcc1aa287c43: clean apply and compile against 9e97df3f7b3d594c6872a1327502d6877feaf820, zero errors, assembly hash 934faaae2cbc1243a454cf1c9959e9f8f51c946f02c2a755d410c635bf0661cb. Rc scratch restored=true; 29 generated alternate-output directories removed and detached scratch removed. No rc push/tag/release/activation. Compile is not rc qualification. The two continuation documentation commits and final report are not runtime picks.

Final evidence-policy, merge-tree, remote-tip and output-cleanup facts are appended after qualification. Required next stage is decide while any pending/red evidence remains; the verified one-file fixture repair is independently eligible for the operator-authorized landing path. No ordinary Review was requested or claimed.

## Pushed slices before final evidence correction

- 36c69764c66368bb6d24f03fe4553e60b517d3e0 docs(CARD-1572): retain continuation census and unresolved fixture findings
- 30b41c64780f5204102d3133c457fcc1aa287c43 test(CARD-1572): retain failed-report precedence in shape proofs
- f262948b83be768be919f2b0b1dcc48bf30ac07f test(CARD-1572): retain inherited shape receipt red witness
- d39433fc2ed59412e122e27a1ff5d4964c329191 docs(CARD-1572): record bounded continuation scope

--- next stage ---
next: decide
handoff: Land the independently verified shape-receipt fixture repair if desired under the operator-authorized path; commission the exact remaining remote UID census and proper C1514 Linux-owned probe / legacy-roster cost remediation. Two independent inherited fixture failures remain; no full-lane or rc qualification is claimed.
artifact: /work/worktrees/task-fb7c4a41/docs/investigations/2026-10-23-card-1572-unit-continuation.md
