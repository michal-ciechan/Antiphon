# CARD-1572 outage recovery: attributed census and fixture repairs

Three fixture causes repaired with five narrow green cases. Latest cross-source Unit inventory: 6024 passed, 43 skipped, 8 non-green terminal cases and 0 missing, of 6075 intended identities. This is an attributed census, not a clean same-source lane or rc qualification.

## Scope, build and provenance

Task fe9ee70d; branch feat/card-task-fe9ee70d; commissioned baseline 30b41c64780f5204102d3133c457fcc1aa287c43. Raw evidence: /work/worktrees/task-fe9ee70d/.antiphon/1572-recovery. The predecessor continued after the outage summary was written: its completed Unit-09/10 results, recovered Unit-08 identities, six C1364 passes and historical 39-case HostJq TRX were validated and retained, without replaying completed expensive selections.

One main shared build at 2b004944635d6b59a7723ec09794179a3c4084c5, assembly SHA-256 b2ac03f4e4d640ab35fc4795e0f7794001808a127decca772b31135df0bc9c70; 153.12s compile, 0s slot queue, 0 errors and 1179 warnings. Host 0c18bb7f591e, platform linux. .NET SDK 10.0.402/runtime 9.0.21, TUnit 1.44.0/MTP 2.2.2. Build log: /work/worktrees/task-fe9ee70d/.antiphon/1572-recovery/build.log. No subsequent main build: all three repairs change the shell fixture loaded at execution time; compiled C# inputs remain unchanged. Per-run source, builtSource, hash, argv, load, timestamps and limits are in each run.json. Policy safe-environment clearing was applied.

The 128 selected remote methods contain 329 cases covering 326 pending identities plus three already-completed siblings. All drivers were foreground children of build-slot.ps1, sequential, with independent collection caps at most 620s. Source stayed frozen during execution except explicitly documented baseline-fixture restoration before the baseline witnesses; restoration was joined before any later edit. No whole-lane run, Review, tag, release script or rc push ran.

TRX gives test-body durations; outer run wall and slot/build waits are separate. Docker/container/readiness, data preparation, database/host/migration, filesystem/HTTP/CLI and teardown subphases not separately instrumented remain unknown/unattributed, never zero. Native event spans are not TRX execution durations. All costs are n=1 provisional observations, not measured p95. CARD-1404 owns measured packing and CARD-1514 phase/fixture work. No test timeout was increased and no policy/checkpoint contract was changed.

## Repairs and unresolved findings

- 4026e85d4f383683ce0f850c1e8a0050f65b5f34: Observe real partial-cleanup release attempts instead of requiring deletion of an unacquired container. All eight cut-scenario resource/residue assertions remain. Narrow partial-green: 1/0/0, 10.629s outer wall.
- 7ce941b313b2ef7f81f491a8de2bea66c77bcd9e: Register fixture-owned planted caches so the real production mount guard reaches the barrier/lock actions. No guard or negative assertion was removed. Narrow cache-green: 3/0/0, 21.721s outer wall.
- 913d9481da2f4d06e75f2fbad1b8c5f182642aed: Rename the local tool permission variable so it cannot shadow the fixture mode through Bash dynamic scoping. All five transfer/refusal assertions remain. Narrow transfer-green: 1/0/0, 5.438s outer wall.

Known remaining causes: three seed/archive cases have a C849PayloadClosure missing c849_cold_reuse_receipts; two C1066 npm parameters have an obsolete extracted-function/dependency model; the C1008 refusal matrix still rejects an explicitly permitted stable off-runner NeedsResolution land (CARD-1353); C1514T_PS51AndRerun invokes a Windows-only custody owner from Linux; the legacy four-mode jq roster exceeds its unchanged 600s test timeout. These three C# fixture causes need repair and a fresh compiled selection, which the one-main-build cap leaves pending (build-scope clarification was unanswered). C1514 requires a real Linux custody path retaining nine actual probe results and all timing artifacts. The frozen CARD-1040 roster requires owner-approved cost/selection remediation, not an assertion or timeout relaxation.

Latest non-green identities:

| Class/method | Latest run | State |
|---|---|---|
| Antiphon.Tests.TestHelpers.TestPhaseSummaryTests.C1514T_PS51AndRerun | deferred-02 | Error |
| Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain | rolling-7 | Error |
| Antiphon.Tests.Scripts.RemoteScriptContractTests.C1066_Fixture_npm_install_uses_argument_log_under_nounset | remote-r01 | Error |
| Antiphon.Tests.Scripts.RemoteScriptContractTests.C1066_Fixture_npm_install_uses_argument_log_under_nounset | remote-r01 | Error |
| Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census | remote-r15 | Error |
| Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker | remote-r26 | Error |
| Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Saved_donor_archive_is_checked_and_imported_without_a_container | remote-r27 | Error |
| Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Seed_refuses_invalid_donors_and_partial_payloads | remote-r27 | Error |

Missing identities: 0; exact UIDs and chronological latest evidence are retained in /work/worktrees/task-fe9ee70d/.antiphon/1572-recovery/combined-coverage.json. Deadline/cancellation cases remain pending until a clean exact selection closes them; do not reinterpret native errors from a killed group as independent product regressions.

## Run outcomes

P/F/S counts are from complete TRX only; a deadline-limited group is never a pass. Native-only evidence is retained separately. Counts include historical reds and baseline/repair reruns, not distinct census identities.

| Run | Source | Intended | Wall s | Exit | TRX P/F/S |
|---|---|---:|---:|---:|---|
| remote-r01 | 2b004944635d6b59a7723ec09794179a3c4084c5 | 10 | 59.224 | 2 | 8/2/0 |
| remote-r02 | d99b941164b67b5d3fe33375d1ef7f145c4d9dcc | 6 | 37.711 | 0 | 6/0/0 |
| remote-r03 | d99b941164b67b5d3fe33375d1ef7f145c4d9dcc | 4 | 62.107 | 0 | 4/0/0 |
| remote-r04 | d99b941164b67b5d3fe33375d1ef7f145c4d9dcc | 4 | 8.684 | 0 | 4/0/0 |
| remote-r05 | d99b941164b67b5d3fe33375d1ef7f145c4d9dcc | 4 | 12.014 | 0 | 4/0/0 |
| remote-r06 | d99b941164b67b5d3fe33375d1ef7f145c4d9dcc | 4 | 8.976 | 0 | 4/0/0 |
| remote-r07 | d99b941164b67b5d3fe33375d1ef7f145c4d9dcc | 4 | 13.44 | 2 | 3/1/0 |
| partial-green | 4026e85d4f383683ce0f850c1e8a0050f65b5f34 | 1 | 10.629 | 0 | 1/0/0 |
| remote-r08 | 4026e85d4f383683ce0f850c1e8a0050f65b5f34 | 4 | 10.369 | 0 | 4/0/0 |
| remote-r09 | 4026e85d4f383683ce0f850c1e8a0050f65b5f34 | 4 | 55.846 | 2 | 2/2/0 |
| cache-base-red | 4026e85d4f383683ce0f850c1e8a0050f65b5f34 | 2 | 50.923 | 2 | 0/2/0 |
| cache-green | 7ce941b313b2ef7f81f491a8de2bea66c77bcd9e | 3 | 21.721 | 0 | 3/0/0 |
| remote-r10 | 7ce941b313b2ef7f81f491a8de2bea66c77bcd9e | 4 | 22.329 | 2 | 3/1/0 |
| transfer-base-red | 7ce941b313b2ef7f81f491a8de2bea66c77bcd9e | 1 | 3.721 | 2 | 0/1/0 |
| transfer-green | 913d9481da2f4d06e75f2fbad1b8c5f182642aed | 1 | 5.438 | 0 | 1/0/0 |
| remote-r11 | 913d9481da2f4d06e75f2fbad1b8c5f182642aed | 4 | 34.088 | 0 | 4/0/0 |
| remote-r12 | 913d9481da2f4d06e75f2fbad1b8c5f182642aed | 4 | 8.571 | 0 | 4/0/0 |
| remote-r13 | 913d9481da2f4d06e75f2fbad1b8c5f182642aed | 4 | 38.328 | 0 | 4/0/0 |
| remote-r14 | 913d9481da2f4d06e75f2fbad1b8c5f182642aed | 4 | 47.518 | 0 | 4/0/0 |
| remote-r15 | 913d9481da2f4d06e75f2fbad1b8c5f182642aed | 4 | 545.014 | 2 | 3/1/0 |
| remote-r16 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 430.729 | 0 | 4/0/0 |
| remote-r17 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 30 | 19.234 | 0 | 30/0/0 |
| remote-r18 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 56.038 | 0 | 4/0/0 |
| remote-r19 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 146 | 620.445 | 124 | no complete TRX |
| receipt-recover | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 273.956 | 0 | 1/0/0 |
| remote-r20-1 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 58.36 | 0 | 1/0/0 |
| remote-r20-2 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 23.48 | 0 | 1/0/0 |
| remote-r20-3 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 10.698 | 0 | 1/0/0 |
| remote-r20-4 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 5.68 | 0 | 1/0/0 |
| remote-r21-1 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 57.911 | 0 | 1/0/0 |
| remote-r21-2 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 8.503 | 0 | 1/0/0 |
| remote-r21-3 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 7.145 | 0 | 1/0/0 |
| remote-r21-4 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 6.425 | 0 | 1/0/0 |
| remote-r22-1 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 33.539 | 0 | 1/0/0 |
| remote-r22-2 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 6.083 | 0 | 1/0/0 |
| remote-r22-3 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 4.907 | 0 | 1/0/0 |
| remote-r22-4 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 66.715 | 0 | 1/0/0 |
| remote-r23-1 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 15.442 | 0 | 1/0/0 |
| remote-r23-2 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 19.215 | 0 | 1/0/0 |
| remote-r23-3 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 101.951 | 0 | 4/0/0 |
| remote-r23-4 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 13 | 408.335 | 0 | 13/0/0 |
| remote-r24-1 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 6.123 | 0 | 1/0/0 |
| remote-r24-2 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 4.531 | 0 | 1/0/0 |
| remote-r24-3 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 3.556 | 0 | 1/0/0 |
| remote-r24-4 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 1 | 3.938 | 0 | 1/0/0 |
| remote-r25 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 5.607 | 0 | 4/0/0 |
| remote-r26 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 14.472 | 2 | 3/1/0 |
| remote-r27 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 12.111 | 2 | 2/2/0 |
| remote-r28 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 4.279 | 0 | 4/0/0 |
| remote-r29 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 35.722 | 0 | 4/0/0 |
| remote-r30 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 14 | 12.639 | 0 | 14/0/0 |
| remote-r31 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 30.813 | 0 | 4/0/0 |
| remote-r32 | 70d13a557481f2d6777fbdc8f2f2d970a033880d | 4 | 38.38 | 0 | 4/0/0 |

The bounded transfer-debug shell copy exited 1 in its lease and is diagnostic evidence only, not a passing test. Group 19's original line is retained unedited: `GROUP complete remote-r19 exit=124 seconds=620.445`. It emitted 145 native passes and one deadline-adjacent constructor failure, without complete TRX. Its 145 completed identities were not repeated just for timing; native-only body durations remain unknown. The sole interrupted disk/partial-failure method was selected separately as receipt-recover.

## Exact group membership

| Group | Expected cases | Pending at start | Methods |
|---|---:|---:|---|
| remote-r01 | 10 | 10 | C1361_Host_census_tolerates_open_status_changes_between_list_and_detail<br>C1361_Host_census_tolerates_open_status_changes_between_passes<br>C1066_Fixture_tree_fault_reaches_validator_under_nounset<br>C1066_Fixture_npm_install_uses_argument_log_under_nounset |
| remote-r02 | 6 | 6 | C1066_Fixture_warm_probe_uses_argument_log_under_nounset<br>C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit<br>C1191_Lock_probe_is_created_before_uid_handoff<br>C1191_Fixture_exit_removes_owned_workspace_without_host_sudo |
| remote-r03 | 4 | 4 | C1192_Fixture_has_no_host_payload_tree_or_payload_bind<br>C1192_Finalization_precedes_success_and_preserves_primary_failure<br>C1192_Evidence_export_preserves_fixture_roster_without_payloads<br>C1208_Legacy_network_replacement_is_untouched |
| remote-r04 | 4 | 4 | C1208_Unacquired_roster_is_untouched<br>C1208_Acquired_objects_are_removed<br>C1208_Recorded_container_id_does_not_delete_replacement<br>C1208_Legacy_owned_markers_are_migrated |
| remote-r05 | 4 | 4 | C1208_Unknown_identity_retains_residue<br>C1208_Wrong_cidfile_preserves_foreign_container<br>C1208_Recorded_id_with_changed_labels_is_untouched<br>C1208_Recorded_container_inspect_failure_refuses_cleanup |
| remote-r06 | 4 | 4 | C1208_Wrong_helper_id_preserves_foreign_container<br>C1208_Wrong_network_id_is_untouched<br>C1208_Recorded_network_inspect_failure_refuses_cleanup<br>C1192_Cleanup_releases_only_registered_current_run_resources |
| remote-r07 | 4 | 4 | C1192_Partial_workspace_acquisition_is_cleaned<br>C1192_Toxic_probe_stays_in_work_volume<br>C1192_Summary_validator_runs_under_the_production_interpreter<br>C1192_Foreign_volume_is_refused_after_create |
| remote-r08 | 4 | 4 | C1192_Harness_cleanup_stays_inside_its_private_directory<br>C1192_Harness_cleanup_refuses_ancestor_swap<br>C1192_Helper_argv_keeps_discarded_env_file_visible<br>C1192_Race_programs_and_feed_share_the_work_volume |
| remote-r09 | 4 | 4 | C1192_Race_barriers_observe_volume_state<br>C1192_Lock_probe_distinguishes_shared_and_private_scratch<br>C1192_Npm_seed_install_and_refill_use_work_volume<br>C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed |
| remote-r10 | 4 | 4 | C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed<br>C1192_Real_cache_cases_keep_the_host_payload_backend<br>C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy<br>C1192_Fixture_seed_and_rerun_keep_payloads_in_volume |
| remote-r11 | 4 | 4 | C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest<br>C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal<br>C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files<br>C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution |
| remote-r12 | 4 | 4 | C1194_Fixture_budget_scale_refuses_empty_observation<br>C1192_Apphost_mask_is_an_empty_exact_volume_mount<br>C1195_Fixture_import_chmod_restores_volume_root<br>C1195_Seed_import_chmod_restores_volume_root |
| remote-r13 | 4 | 4 | C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content<br>C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work<br>C913_Seed_accepts_complete_ordinary_packages_without_framework_packs<br>C913_Full_marker_binds_verified_recovery_before_publication |
| remote-r14 | 4 | 4 | C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct<br>C913_Prune_validates_recovery_without_refilling_image_packs<br>C913_Fixture_uses_owned_payloads_and_proven_absent_temp<br>C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims |
| remote-r15 | 4 | 4 | C1087_Host_census_filters_and_names_cause<br>C1008_Recycle_exact_default_volumes<br>C1008_Recycle_refuses_references_and_unknown_census<br>C1008_Recycle_audits_work_as_1654 |
| remote-r16 | 4 | 4 | C1008_Recycle_refuses_unpublished_and_dirty_work<br>C1008_Recycle_refuses_uninspectable_git<br>C1008_Recycle_reaps_dirty_scratch<br>C1008_Recycle_audits_promisor_checkout |
| remote-r17 | 30 | 30 | C1105_Git_audit_recovery_tips<br>C1105_Git_audit_seed_content<br>C1105_Git_audit_index_content<br>C1105_Git_audit_object_completeness |
| remote-r18 | 4 | 4 | C1105_Git_audit_overall_timeout<br>C1316_Recycle_audit_cap_clears_the_standing_volume<br>C1105_Git_audit_volume_scale<br>C1105_Git_audit_resume_preserves_proof |
| remote-r19 | 146 | 143 | C1105_Git_audit_hidden_locations<br>C1335_Mirror_owner_stamp_in_linked_admin<br>C1008_Recycle_resume_requires_matching_receipt<br>C1008_Recycle_receipt_records_disk_and_partial_failure |
| remote-r20-1 | 1 | 1 | C1008_Retire_temp_rechecks_absence_and_retirement |
| remote-r20-2 | 1 | 1 | C1008_Retire_temp_reclaims_below_cache_disk_gate |
| remote-r20-3 | 1 | 1 | C1008_Recycle_dry_run_never_mutates |
| remote-r20-4 | 1 | 1 | C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes |
| remote-r21-1 | 1 | 1 | C1105_Redeploy_accepts_previous_generation_and_requires_new_bind |
| remote-r21-2 | 1 | 1 | C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout |
| remote-r21-3 | 1 | 1 | C1105_V218_dry_run_reaches_the_generation_line |
| remote-r21-4 | 1 | 1 | C1105_Relative_dry_run_refuses_before_removal |
| remote-r22-1 | 1 | 1 | C1105_Generation_identity_refuses_mismatch |
| remote-r22-2 | 1 | 1 | C1105_Main_state_init_without_session_runner_refuses |
| remote-r22-3 | 1 | 1 | C1105_Compose_cmp_refuses_before_removal |
| remote-r22-4 | 1 | 1 | C1105_Resume_identity_refusals |
| remote-r23-1 | 1 | 1 | C1105_Resume_state_init_image_follows_the_journal |
| remote-r23-2 | 1 | 1 | C1105_Resume_foreign_leftover_is_not_an_image_mismatch |
| remote-r23-3 | 4 | 4 | C1105_Resume_digest_pins_previous_sha12 |
| remote-r23-4 | 13 | 13 | C1105_Resume_post_removal_census_tracks_open_work |
| remote-r24-1 | 1 | 1 | C849_front_door_passes_every_full_case_name_to_the_invoker |
| remote-r24-2 | 1 | 1 | Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner |
| remote-r24-3 | 1 | 1 | Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner |
| remote-r24-4 | 1 | 1 | Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode |
| remote-r25 | 4 | 4 | Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents<br>Host_lane_ownership_reset_never_enters_the_codex_home<br>C849_Cache_cases_use_only_the_validated_host_lane<br>C849_Cache_prepare_is_idempotent_and_preserves_payloads |
| remote-r26 | 4 | 4 | C849_Cache_prepare_refuses_foreign_or_unsafe_roots<br>C849_Prune_refuses_stale_or_busy_authority<br>C849_Closure_preflight_names_a_missing_dependency<br>C849_Seed_publishes_complete_payloads_before_its_marker |
| remote-r27 | 4 | 4 | C849_Seed_refuses_invalid_donors_and_partial_payloads<br>C849_Saved_donor_archive_is_checked_and_imported_without_a_container<br>C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters<br>C849_Saved_donor_rejects_declared_size_bomb_before_writing |
| remote-r28 | 4 | 4 | C849_Retired_guard_refuses_docker_ps_errors<br>C0995_Deploy_temp_checks_host_absence_before_checkout<br>C0995_Cold_ready_seed_refuses_a_temp_container<br>C849_Cold_marker_reuse_writes_gate_receipts |
| remote-r29 | 4 | 4 | C849_Deploy_temp_names_saved_import_when_seed_is_missing<br>C849_Deploy_prepares_and_verifies_before_acceptance<br>C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo<br>C849_Prune_preview_is_read_only_and_bounded |
| remote-r30 | 14 | 14 | C997_Cache_budget_gate_uses_controlled_disk_readings<br>C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance<br>C997_Saved_donor_probe_failure_refuses_before_copy<br>C849_Prune_and_rollback_retain_roots_and_recovery |
| remote-r31 | 4 | 4 | C849_Cache_receipts_exclude_credentials_and_payloads<br>C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots<br>C912_Cold_seed_refuses_when_created_volume_disappears_before_init<br>C912_Cold_volumes_seed_accepts_busy_main_without_packages |
| remote-r32 | 4 | 4 | C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets<br>C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries<br>C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit<br>C912_Cold_marker_has_distinct_validation_and_full_context_refusal |

After group 19 hit its group cap, the remaining host/resume matrices were separated into exact methods; the last cache/seed/cold model packs used provisional 300-second collection caps to reserve rc/report time. Literal filters, environment policy and deadlines are retained in groups-final.json and the per-run manifests. The method OR form is `/*/*/RemoteScriptContractTests/(MethodA*)|(MethodB*)`; do not repeat the whole class to recover a missing method.

## Fresh complete-TRX per-case durations and provisional costs

All historical reruns are retained. Proposed serial cost is ceil(body seconds). A future collection budget should use measured p95; the displayed 2x observation plus 10s is only a provisional collection proposal for successful small cases, not an adopted timeout. Failed or large matrix cases must be repaired/split and remeasured; no longer timeout is proposed.

| Run | Case | Result | Body s | Provisional cost s | Provisional collection s |
|---|---|---|---:|---:|---|
| remote-r09 | C1192_Race_barriers_observe_volume_state | Failed | 32.805 | 33 | repair/split and remeasure |
| remote-r09 | C1192_Npm_seed_install_and_refill_use_work_volume | Passed | 2.625 | 3 | 16 |
| remote-r09 | C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed | Passed | 2.087 | 3 | 15 |
| remote-r09 | C1192_Lock_probe_distinguishes_shared_and_private_scratch | Failed | 13.962 | 14 | repair/split and remeasure |
| remote-r22-2 | C1105_Main_state_init_without_session_runner_refuses | Passed | 2.096 | 3 | 15 |
| remote-r14 | C913_Prune_validates_recovery_without_refilling_image_packs | Passed | 29.740 | 30 | 70 |
| remote-r14 | C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct | Passed | 3.505 | 4 | 18 |
| remote-r14 | C913_Fixture_uses_owned_payloads_and_proven_absent_temp | Passed | 6.224 | 7 | 23 |
| remote-r14 | C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims | Passed | 4.257 | 5 | 19 |
| cache-base-red | C1192_Lock_probe_distinguishes_shared_and_private_scratch | Failed | 13.496 | 14 | repair/split and remeasure |
| cache-base-red | C1192_Race_barriers_observe_volume_state | Failed | 32.989 | 33 | repair/split and remeasure |
| remote-r13 | C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work | Passed | 0.339 | 1 | 11 |
| remote-r13 | C913_Full_marker_binds_verified_recovery_before_publication | Passed | 29.857 | 30 | 70 |
| remote-r13 | C913_Seed_accepts_complete_ordinary_packages_without_framework_packs | Passed | 1.473 | 2 | 13 |
| remote-r13 | C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content | Passed | 1.713 | 2 | 14 |
| remote-r10 | C1192_Real_cache_cases_keep_the_host_payload_backend | Passed | 1.028 | 2 | 13 |
| remote-r10 | C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy | Failed | 0.692 | 1 | repair/split and remeasure |
| remote-r10 | C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed | Passed | 1.633 | 2 | 14 |
| remote-r10 | C1192_Fixture_seed_and_rerun_keep_payloads_in_volume | Passed | 15.198 | 16 | 41 |
| remote-r08 | C1192_Helper_argv_keeps_discarded_env_file_visible | Passed | 0.768 | 1 | 12 |
| remote-r08 | C1192_Harness_cleanup_stays_inside_its_private_directory | Passed | 2.629 | 3 | 16 |
| remote-r08 | C1192_Race_programs_and_feed_share_the_work_volume | Passed | 3.095 | 4 | 17 |
| remote-r08 | C1192_Harness_cleanup_refuses_ancestor_swap | Passed | 0.077 | 1 | 11 |
| remote-r24-2 | Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner | Passed | 0.121 | 1 | 11 |
| remote-r25 | C849_Cache_cases_use_only_the_validated_host_lane | Passed | 1.162 | 2 | 13 |
| remote-r25 | C849_Cache_prepare_is_idempotent_and_preserves_payloads | Passed | 0.228 | 1 | 11 |
| remote-r25 | Host_lane_ownership_reset_never_enters_the_codex_home | Passed | 0.086 | 1 | 11 |
| remote-r25 | Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents | Passed | 0.088 | 1 | 11 |
| transfer-base-red | C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy | Failed | 0.422 | 1 | repair/split and remeasure |
| remote-r22-4 | C1105_Resume_identity_refusals | Passed | 61.878 | 62 | 134 |
| remote-r26 | C849_Cache_prepare_refuses_foreign_or_unsafe_roots | Passed | 0.185 | 1 | 11 |
| remote-r26 | C849_Prune_refuses_stale_or_busy_authority | Passed | 8.462 | 9 | 27 |
| remote-r26 | C849_Seed_publishes_complete_payloads_before_its_marker | Failed | 0.474 | 1 | repair/split and remeasure |
| remote-r26 | C849_Closure_preflight_names_a_missing_dependency | Passed | 1.487 | 2 | 13 |
| remote-r32 | C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit | Passed | 11.911 | 12 | 34 |
| remote-r32 | C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets | Passed | 0.998 | 1 | 12 |
| remote-r32 | C912_Cold_marker_has_distinct_validation_and_full_context_refusal | Passed | 5.098 | 6 | 21 |
| remote-r32 | C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries | Passed | 16.082 | 17 | 43 |
| remote-r27 | C849_Saved_donor_archive_is_checked_and_imported_without_a_container | Failed | 0.283 | 1 | repair/split and remeasure |
| remote-r27 | C849_Seed_refuses_invalid_donors_and_partial_payloads | Failed | 0.286 | 1 | repair/split and remeasure |
| remote-r27 | C849_Saved_donor_rejects_declared_size_bomb_before_writing | Passed | 0.996 | 1 | 12 |
| remote-r27 | C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters | Passed | 6.882 | 7 | 24 |
| remote-r01 | C1066_Fixture_tree_fault_reaches_validator_under_nounset(metadata, CacheDonorPackagesEmpty) | Passed | 0.094 | 1 | 11 |
| remote-r01 | C1066_Fixture_tree_fault_reaches_validator_under_nounset(special, CacheDonorUnsafeEntry) | Passed | 0.057 | 1 | 11 |
| remote-r01 | C1066_Fixture_tree_fault_reaches_validator_under_nounset(hardlink, CacheDonorUnsafeEntry) | Passed | 0.057 | 1 | 11 |
| remote-r01 | C1066_Fixture_npm_install_uses_argument_log_under_nounset(True) | Failed | 0.195 | 1 | repair/split and remeasure |
| remote-r01 | C1066_Fixture_tree_fault_reaches_validator_under_nounset(host, CacheDonorPackagesEmpty) | Passed | 0.100 | 1 | 11 |
| remote-r01 | C1361_Host_census_tolerates_open_status_changes_between_list_and_detail | Passed | 19.641 | 20 | 50 |
| remote-r01 | C1361_Host_census_tolerates_open_status_changes_between_passes | Passed | 35.263 | 36 | 81 |
| remote-r01 | C1066_Fixture_npm_install_uses_argument_log_under_nounset(False) | Failed | 0.051 | 1 | repair/split and remeasure |
| remote-r01 | C1066_Fixture_tree_fault_reaches_validator_under_nounset(symlink, CacheDonorUnsafeEntry) | Passed | 0.078 | 1 | 11 |
| remote-r01 | C1066_Fixture_tree_fault_reaches_validator_under_nounset(reference, CacheDonorPackagesEmpty) | Passed | 0.070 | 1 | 11 |
| remote-r07 | C1192_Toxic_probe_stays_in_work_volume | Passed | 1.042 | 2 | 13 |
| remote-r07 | C1192_Summary_validator_runs_under_the_production_interpreter | Passed | 0.043 | 1 | 11 |
| remote-r07 | C1192_Partial_workspace_acquisition_is_cleaned | Failed | 7.018 | 8 | repair/split and remeasure |
| remote-r07 | C1192_Foreign_volume_is_refused_after_create | Passed | 0.907 | 1 | 12 |
| remote-r04 | C1208_Recorded_container_id_does_not_delete_replacement | Passed | 0.549 | 1 | 12 |
| remote-r04 | C1208_Acquired_objects_are_removed | Passed | 2.145 | 3 | 15 |
| remote-r04 | C1208_Legacy_owned_markers_are_migrated | Passed | 0.973 | 1 | 12 |
| remote-r04 | C1208_Unacquired_roster_is_untouched | Passed | 0.732 | 1 | 12 |
| remote-r21-2 | C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout | Passed | 3.503 | 4 | 18 |
| remote-r23-2 | C1105_Resume_foreign_leftover_is_not_an_image_mismatch | Passed | 15.321 | 16 | 41 |
| partial-green | C1192_Partial_workspace_acquisition_is_cleaned | Passed | 6.666 | 7 | 24 |
| remote-r22-3 | C1105_Compose_cmp_refuses_before_removal | Passed | 0.991 | 1 | 12 |
| remote-r23-1 | C1105_Resume_state_init_image_follows_the_journal | Passed | 11.180 | 12 | 33 |
| remote-r20-3 | C1008_Recycle_dry_run_never_mutates | Passed | 6.254 | 7 | 23 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(terminal, 0, ) | Passed | 34.663 | 35 | 80 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(matching, 0, ) | Passed | 35.225 | 36 | 81 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(land, 2, RecycleLandInFlight 44444444-4444-4444-4444-444444444444) | Passed | 28.389 | 29 | 67 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(missing-phase, 2, RecycleTaskCensusUnknown cause=Unstable) | Passed | 27.393 | 28 | 65 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(land-other, 0, ) | Passed | 35.535 | 36 | 82 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(rebound, 2, RecycleTaskCensusUnknown cause=Unstable) | Passed | 27.427 | 28 | 65 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(preflight, 2, RecycleTaskCensusUnknown cause=Unstable) | Passed | 27.117 | 28 | 65 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(new-server2, 2, RecycleBoundTasks 44444444-4444-4444-4444-444444444444 status=Blocked runner=server2) | Passed | 27.110 | 28 | 65 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(verified, 0, ) | Passed | 34.546 | 35 | 80 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(unstable, 2, RecycleTaskCensusUnknown cause=Unstable) | Passed | 27.692 | 28 | 66 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(new-temp, 0, ) | Passed | 35.547 | 36 | 82 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(bad-tasks, 2, RecycleTaskCensusUnknown cause=Unstable) | Passed | 27.331 | 28 | 65 |
| remote-r23-4 | C1105_Resume_post_removal_census_tracks_open_work(new-desktop, 0, ) | Passed | 35.905 | 36 | 82 |
| remote-r29 | C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo | Passed | 0.196 | 1 | 11 |
| remote-r29 | C849_Prune_preview_is_read_only_and_bounded | Passed | 0.023 | 1 | 11 |
| remote-r29 | C849_Deploy_prepares_and_verifies_before_acceptance | Passed | 30.651 | 31 | 72 |
| remote-r29 | C849_Deploy_temp_names_saved_import_when_seed_is_missing | Passed | 0.053 | 1 | 11 |
| remote-r21-1 | C1105_Redeploy_accepts_previous_generation_and_requires_new_bind | Passed | 53.324 | 54 | 117 |
| remote-r21-3 | C1105_V218_dry_run_reaches_the_generation_line | Passed | 2.465 | 3 | 15 |
| remote-r28 | C0995_Deploy_temp_checks_host_absence_before_checkout | Passed | 0.098 | 1 | 11 |
| remote-r28 | C0995_Cold_ready_seed_refuses_a_temp_container | Passed | 0.073 | 1 | 11 |
| remote-r28 | C849_Retired_guard_refuses_docker_ps_errors | Passed | 0.009 | 1 | 11 |
| remote-r28 | C849_Cold_marker_reuse_writes_gate_receipts | Passed | 0.146 | 1 | 11 |
| cache-green | C1192_Race_barriers_observe_volume_state | Passed | 13.098 | 14 | 37 |
| cache-green | C1192_Lock_probe_distinguishes_shared_and_private_scratch | Passed | 2.602 | 3 | 16 |
| cache-green | C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed | Passed | 2.055 | 3 | 15 |
| remote-r23-3 | C1105_Resume_digest_pins_previous_sha12(missing-previous, 2) | Passed | 19.370 | 20 | 49 |
| remote-r23-3 | C1105_Resume_digest_pins_previous_sha12(matching, 0) | Passed | 39.753 | 40 | 90 |
| remote-r23-3 | C1105_Resume_digest_pins_previous_sha12(edited-digest, 2) | Passed | 19.574 | 20 | 50 |
| remote-r23-3 | C1105_Resume_digest_pins_previous_sha12(short-previous, 2) | Passed | 19.430 | 20 | 49 |
| receipt-recover | C1008_Recycle_receipt_records_disk_and_partial_failure | Passed | 270.075 | 271 | 551 |
| remote-r22-1 | C1105_Generation_identity_refuses_mismatch | Passed | 28.818 | 29 | 68 |
| remote-r18 | C1105_Git_audit_volume_scale | Passed | 50.087 | 51 | 111 |
| remote-r18 | C1316_Recycle_audit_cap_clears_the_standing_volume | Passed | 0.035 | 1 | 11 |
| remote-r18 | C1105_Git_audit_overall_timeout | Passed | 0.353 | 1 | 11 |
| remote-r18 | C1105_Git_audit_resume_preserves_proof | Passed | 1.042 | 2 | 13 |
| remote-r16 | C1008_Recycle_reaps_dirty_scratch | Passed | 23.351 | 24 | 57 |
| remote-r16 | C1008_Recycle_audits_promisor_checkout | Passed | 71.433 | 72 | 153 |
| remote-r16 | C1008_Recycle_refuses_unpublished_and_dirty_work | Passed | 116.648 | 117 | 244 |
| remote-r16 | C1008_Recycle_refuses_uninspectable_git | Passed | 214.925 | 215 | 440 |
| remote-r11 | C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files | Passed | 0.548 | 1 | 12 |
| remote-r11 | C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal | Passed | 17.159 | 18 | 45 |
| remote-r11 | C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution | Passed | 0.043 | 1 | 11 |
| remote-r11 | C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest | Passed | 12.543 | 13 | 36 |
| remote-r05 | C1208_Recorded_id_with_changed_labels_is_untouched | Passed | 2.900 | 3 | 16 |
| remote-r05 | C1208_Wrong_cidfile_preserves_foreign_container | Passed | 0.962 | 1 | 12 |
| remote-r05 | C1208_Recorded_container_inspect_failure_refuses_cleanup | Passed | 1.562 | 2 | 14 |
| remote-r05 | C1208_Unknown_identity_retains_residue | Passed | 2.483 | 3 | 15 |
| remote-r24-3 | Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner | Passed | 0.110 | 1 | 11 |
| remote-r20-1 | C1008_Retire_temp_rechecks_absence_and_retirement | Passed | 54.549 | 55 | 120 |
| remote-r06 | C1208_Wrong_network_id_is_untouched | Passed | 0.559 | 1 | 12 |
| remote-r06 | C1192_Cleanup_releases_only_registered_current_run_resources | Passed | 2.561 | 3 | 16 |
| remote-r06 | C1208_Wrong_helper_id_preserves_foreign_container | Passed | 0.903 | 1 | 12 |
| remote-r06 | C1208_Recorded_network_inspect_failure_refuses_cleanup | Passed | 0.910 | 1 | 12 |
| remote-r20-2 | C1008_Retire_temp_reclaims_below_cache_disk_gate | Passed | 19.586 | 20 | 50 |
| remote-r02 | C1066_Fixture_warm_probe_uses_argument_log_under_nounset(scratch) | Passed | 0.044 | 1 | 11 |
| remote-r02 | C1191_Lock_probe_is_created_before_uid_handoff | Passed | 1.315 | 2 | 13 |
| remote-r02 | C1191_Fixture_exit_removes_owned_workspace_without_host_sudo | Passed | 15.104 | 16 | 41 |
| remote-r02 | C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit | Passed | 16.854 | 17 | 44 |
| remote-r02 | C1066_Fixture_warm_probe_uses_argument_log_under_nounset(packages) | Passed | 0.022 | 1 | 11 |
| remote-r02 | C1066_Fixture_warm_probe_uses_argument_log_under_nounset(home) | Passed | 0.026 | 1 | 11 |
| remote-r30 | C997_Cache_budget_gate_uses_controlled_disk_readings(20971521, 0, , 21474837504) | Passed | 0.056 | 1 | 11 |
| remote-r30 | C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance(directory, 21474836497) | Passed | 0.893 | 1 | 12 |
| remote-r30 | C849_Prune_and_rollback_retain_roots_and_recovery | Passed | 0.272 | 1 | 11 |
| remote-r30 | C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance(tar, 21474836499) | Passed | 1.060 | 2 | 13 |
| remote-r30 | C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance(tar, 21474836498) | Passed | 1.028 | 2 | 13 |
| remote-r30 | C997_Cache_budget_gate_uses_controlled_disk_readings(invalid, 2, CacheDiskUnavailable, ) | Passed | 0.029 | 1 | 11 |
| remote-r30 | C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance(tar, 21474836497) | Passed | 0.965 | 1 | 12 |
| remote-r30 | C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance(directory, 21474836498) | Passed | 1.135 | 2 | 13 |
| remote-r30 | C997_Saved_donor_probe_failure_refuses_before_copy(directory) | Passed | 0.976 | 1 | 12 |
| remote-r30 | C997_Cache_budget_gate_uses_controlled_disk_readings(20971520, 0, , 21474836480) | Passed | 0.029 | 1 | 11 |
| remote-r30 | C997_Cache_budget_gate_uses_controlled_disk_readings(missing, 2, CacheDiskUnavailable, ) | Passed | 0.036 | 1 | 11 |
| remote-r30 | C997_Saved_donor_probe_failure_refuses_before_copy(tar) | Passed | 0.952 | 1 | 12 |
| remote-r30 | C997_Cache_budget_gate_uses_controlled_disk_readings(20971519, 2, CacheDiskLow, ) | Passed | 0.027 | 1 | 11 |
| remote-r30 | C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance(directory, 21474836499) | Passed | 1.112 | 2 | 13 |
| remote-r24-4 | Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode | Passed | 0.075 | 1 | 11 |
| remote-r03 | C1208_Legacy_network_replacement_is_untouched | Passed | 1.378 | 2 | 13 |
| remote-r03 | C1192_Fixture_has_no_host_payload_tree_or_payload_bind | Passed | 35.871 | 36 | 82 |
| remote-r03 | C1192_Evidence_export_preserves_fixture_roster_without_payloads | Passed | 3.448 | 4 | 17 |
| remote-r03 | C1192_Finalization_precedes_success_and_preserves_primary_failure | Passed | 17.276 | 18 | 45 |
| remote-r24-1 | C849_front_door_passes_every_full_case_name_to_the_invoker | Passed | 1.511 | 2 | 14 |
| remote-r31 | C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots | Passed | 3.263 | 4 | 17 |
| remote-r31 | C849_Cache_receipts_exclude_credentials_and_payloads | Passed | 0.021 | 1 | 11 |
| remote-r31 | C912_Cold_volumes_seed_accepts_busy_main_without_packages | Passed | 23.077 | 24 | 57 |
| remote-r31 | C912_Cold_seed_refuses_when_created_volume_disappears_before_init | Passed | 0.310 | 1 | 11 |
| remote-r21-4 | C1105_Relative_dry_run_refuses_before_removal | Passed | 2.449 | 3 | 15 |
| remote-r20-4 | C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes | Passed | 1.910 | 2 | 14 |
| remote-r12 | C1195_Seed_import_chmod_restores_volume_root | Passed | 0.087 | 1 | 11 |
| remote-r12 | C1195_Fixture_import_chmod_restores_volume_root | Passed | 0.034 | 1 | 11 |
| remote-r12 | C1192_Apphost_mask_is_an_empty_exact_volume_mount | Passed | 4.317 | 5 | 19 |
| remote-r12 | C1194_Fixture_budget_scale_refuses_empty_observation | Passed | 0.046 | 1 | 11 |
| remote-r15 | C1008_Recycle_audits_work_as_1654 | Passed | 20.752 | 21 | 52 |
| remote-r15 | C1008_Recycle_refuses_references_and_unknown_census | Failed | 321.964 | 322 | repair/split and remeasure |
| remote-r15 | C1008_Recycle_exact_default_volumes | Passed | 138.101 | 139 | 287 |
| remote-r15 | C1087_Host_census_filters_and_names_cause | Passed | 59.835 | 60 | 130 |
| remote-r17 | C1105_Git_audit_seed_content(seed-tracked) | Passed | 0.238 | 1 | 11 |
| remote-r17 | C1105_Git_audit_recovery_tips(worktree-ref) | Passed | 0.600 | 1 | 12 |
| remote-r17 | C1105_Git_audit_recovery_tips(secondary) | Passed | 0.630 | 1 | 12 |
| remote-r17 | C1105_Git_audit_object_completeness(missing-tree) | Passed | 0.532 | 1 | 12 |
| remote-r17 | C1105_Git_audit_seed_content(fetch-only) | Passed | 0.552 | 1 | 12 |
| remote-r17 | C1105_Git_audit_seed_content(seed-stash) | Passed | 0.437 | 1 | 11 |
| remote-r17 | C1105_Git_audit_recovery_tips(bare-head) | Passed | 0.593 | 1 | 12 |
| remote-r17 | C1105_Git_audit_seed_content(seed) | Passed | 0.539 | 1 | 12 |
| remote-r17 | C1105_Git_audit_seed_content(long-lived) | Passed | 0.671 | 1 | 12 |
| remote-r17 | C1105_Git_audit_index_content(sparse) | Passed | 0.409 | 1 | 11 |
| remote-r17 | C1105_Git_audit_index_content(skip) | Passed | 0.415 | 1 | 11 |
| remote-r17 | C1105_Git_audit_index_content(assume) | Passed | 0.409 | 1 | 11 |
| remote-r17 | C1105_Git_audit_seed_content(seed-empty-ignored) | Passed | 0.420 | 1 | 11 |
| remote-r17 | C1105_Git_audit_seed_content(empty-used) | Passed | 0.558 | 1 | 12 |
| remote-r17 | C1105_Git_audit_recovery_tips(stash) | Passed | 0.593 | 1 | 12 |
| remote-r17 | C1105_Git_audit_seed_content(modified) | Passed | 0.405 | 1 | 11 |
| remote-r17 | C1105_Git_audit_seed_content(seed-ignored) | Passed | 0.391 | 1 | 11 |
| remote-r17 | C1105_Git_audit_recovery_tips(linked-private) | Passed | 0.702 | 1 | 12 |
| remote-r17 | C1105_Git_audit_index_content(racy) | Passed | 0.422 | 1 | 11 |
| remote-r17 | C1105_Git_audit_object_completeness(missing-commit) | Passed | 0.517 | 1 | 12 |
| remote-r17 | C1105_Git_audit_recovery_tips(linked-head) | Passed | 0.701 | 1 | 12 |
| remote-r17 | C1105_Git_audit_index_content(intent) | Passed | 0.400 | 1 | 11 |
| remote-r17 | C1105_Git_audit_seed_content(seed-untracked) | Passed | 0.381 | 1 | 11 |
| remote-r17 | C1105_Git_audit_seed_content(staged) | Passed | 0.404 | 1 | 11 |
| remote-r17 | C1105_Git_audit_recovery_tips(recovery) | Passed | 0.616 | 1 | 12 |
| remote-r17 | C1105_Git_audit_recovery_tips(reflog-symlink) | Passed | 0.526 | 1 | 12 |
| remote-r17 | C1105_Git_audit_recovery_tips(reflog-old) | Passed | 0.596 | 1 | 12 |
| remote-r17 | C1105_Git_audit_recovery_tips(reflog) | Passed | 0.595 | 1 | 12 |
| remote-r17 | C1105_Git_audit_object_completeness(missing-head-tree) | Passed | 0.388 | 1 | 11 |
| remote-r17 | C1105_Git_audit_object_completeness(missing-ancestor-graph) | Passed | 0.524 | 1 | 12 |
| transfer-green | C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy | Passed | 1.640 | 2 | 14 |

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


## Inherited nine rolling-method body durations

Source 30b41c64780f5204102d3133c457fcc1aa287c43; historical complete TRX in the predecessor evidence root. These are not reruns from this task. All costs are provisional n=1; the failed legacy composite needs owner-approved splitting/measurement, without a longer timeout.

| Run | Case | Result | TRX body s | Proposed cost s |
|---|---|---|---:|---:|
| rolling-1 | C1087_Read_failures_name_their_cause | Passed | 43.703 | 44 |
| rolling-5 | C1008_Option_manifest_is_strict | Passed | 1.356 | 2 |
| rolling-8 | C1008_Documentation_and_transport_pins_match | Passed | 1.265 | 2 |
| rolling-9 | C1008_Refusal_receipts_do_not_leak_secrets | Passed | 26.239 | 27 |
| rolling-6 | C1008_Wrapper_option_manifest_is_strict | Passed | 13.160 | 14 |
| rolling-3 | C1087_Census_filters_open_work_and_pending_lands | Passed | 43.891 | 44 |
| rolling-4 | C1087_Check_census_phase_is_read_only | Passed | 3.844 | 4 |
| rolling-7 | C1008_Legacy_rolling_and_jq_rosters_remain | Failed | 601.061 | split and remeasure |
| rolling-2 | C1087_Project_resolves_by_repository_identity | Passed | 46.999 | 47 |


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



## Historical decisions and red witnesses

The following retained notes preserve original outcomes, attributions and limits; later tables above supersede provisional census counts.

# CARD-1572 outage recovery census

Task fe9ee70d starts at 30b41c64780f5204102d3133c457fcc1aa287c43 on feat/card-task-fe9ee70d. The predecessor subsequently completed its report: /work/worktrees/task-fb7c4a41/.antiphon/task-fb7c4a41.md. Its three previously unrun groups and interrupted C1364/rolling selections were executed; HostJq has complete historical TRX. Repeating these expensive completed selections would not close missing evidence.

The predecessor's combined 6,075-UID Unit inventory records 5,704 passes, 43 skips, eight latest reds and 320 missing. The remaining remote-script inventory comprises 326 UIDs across 128 methods; six red identities came from cancellation. Two independent inherited fixture failures remain: C1514T_PS51AndRerun invokes a Windows-only nightly owner on Linux, and C1008_Legacy_rolling_and_jq_rosters_remain exhausted its existing 600-second timeout. Its frozen CARD-1040 qualification contract cannot be silently split or relaxed.

This continuation will validate the inherited artifacts and run only missing remote methods in small sequential groups under one host build-slot lease and one shared isolated build. Each group has a hard collection deadline no greater than 620 seconds, not a new test timeout. Source remains frozen during execution. Generated manifests, exact argv, source/DLL hashes, host/load, TRX and native diagnostics live in /work/worktrees/task-fe9ee70d/.antiphon/1572-recovery. Per-case TRX durations are distinct from outer wall; unknown internal phases remain unknown. The 90-minute task budget bounds collection, with time reserved for attribution/reporting. No full-lane or rc qualification is implied; no Review, tags or release scripts are commissioned.

## Fresh inherited red witness

At unchanged test/product source 2b0049446 (documentation-only descendant of 30b41c647), remote-r01 completed in 59.224s: ten cases, eight passed, two failed, zero skipped. Both C1066_Fixture_npm_install_uses_argument_log_under_nounset parameters fail independently of the 620s collector cap. The extracted workspace-label function calls missing c849_fixture_resource_state; the test's old function roster also omits newer cache-identity/registration and launch dependencies. This is fixture extraction/model drift, not evidence of a product npm failure. Production's fail-closed volume-state integration is associated with 40bb77c9ca355e2039b02d6864cc18c92e1ec9a4 (history search, not a first-bad bisect). The exact red TRX, diagnostics, hashes and method filters are retained under .antiphon/1572-recovery/remote-r01. No assertion or timeout has been changed. Necessary compiled repair verification conflicts with the brief's one-shared-build ceiling; clarification is pending while independent census collection continues.

remote-r07 independently witnessed C1192_Partial_workspace_acquisition_is_cleaned failing (7.017s; three sibling passes). Its `cleanup` fact searches for removal of unacquired restore-p1; CARD-1208's identity-safe release deliberately avoids that deletion. All eight acquisition cut scenarios report cleanup=0 even where owned resources were removed correctly. This is fixture observation drift: measure the real consumer-release attempt and preserve the existing resource/residue assertions. No product deletion policy should be changed to satisfy this stale fixture observation.

The barrier and shared/private lock methods both failed again with baseline fixture bytes restored from 30b41c64780f5204102d3133c457fcc1aa287c43 (cache-base-red: zero passed, two failed, no skips). No C#, project, target, or production remote-script source differs from that baseline; the existing shared binary is its documentation-only descendant. Restoration to 4026e85d4 was joined before further edits. Their plant_cache setup creates valid labels but omits acquisition registration, so the production cache-mount guard rejects before the race/lock action. Registration became required in 27dc531ab4846f1eec090952db29227d1a8ed6c7. This is inherited fixture setup drift; keep all readiness, lock, negative-control and volume assertions.

remote-r10 exposed another inherited fixture failure: C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy fails during setup with CacheHelperImageMissing, before its transfer assertions. transfer-base-red reproduced the one failure with baseline fixture bytes (one failed, no passes/skips). Inspection finds mode_transfer's uninitialized local `mode` dynamically shadows the harness selector read by its Docker model under nounset. Use a distinct tool-permission variable; preserve all five transfer and refusal assertions. A bounded leased diagnostic copy exposing scenario.err did not add a diagnosis because the image probe redirects its own error; that diagnostic is retained and is not a passing witness.

remote-r15 completed, not deadline-limited: 545.014s wall, three passes and one independent assertion failure. True TRX body seconds: C1008_Recycle_audits_work_as_1654 20.752; C1008_Recycle_refuses_references_and_unknown_census 321.964 (failed); C1008_Recycle_exact_default_volumes 138.101; C1087_Host_census_filters_and_names_cause 59.835. The refusal test's `active-land` vector uses runnerId=other with stable NeedsResolution proof but still expects zero removals. CARD-1353 commit b0d2a198b2cea824553845d2649c46a66c4134c2 and docs/docker-stack.md explicitly admit and record this off-runner pending land. This is inherited C# fixture expectation drift, not a product deletion regression. This test and its production/model dependencies are unchanged from the commissioned baseline; no first-bad bisect or additional 322s baseline execution is claimed. Repair should retain explicit bound-land refusal and require receipt proof for the allowed off-runner case. The one-build cap leaves this C# repair and the C1066 C# dependency repair pending; the build-scope clarification has not received an answer. None of these findings justifies raising a timeout.

## Completed continuation census

The final chronological UID join accounts for every intended identity: 6,024 passed, 43 skipped, eight red, zero missing, of 6,075. This remains a cross-source attributed census, not a green lane. The remaining six RemoteScriptContractTests reds are two C1066 npm parameters, the obsolete off-runner land refusal, and three seed/archive methods whose C849PayloadClosure omits c849_cold_reuse_receipts. The latter was introduced into the seed path by f96737552b251671b9541425764e4b77dde5b737; preflight correctly reports MISSING before the fixture body. This is C# extracted-closure drift, not a seed product failure. All three observed failures precede any proposed repair; C# source remains unchanged under the one-main-build cap. C1514 Linux custody and the unchanged legacy jq 600-second timeout are the other two reds.

Forty-seven bounded main selection groups cover the 329 selected cases (326 pending UIDs plus three previously completed parameter siblings). All complete-TRX group counts match their intended counts. remote-r19 alone hit its 620-second cap: 145 native passes and one deadline-adjacent constructor error, no TRX; this run remains incomplete. Only its interrupted disk/partial-failure method was recovered separately: one pass in 273.956s wall. The thirteen post-removal census parameters passed in 408.335s. No completed broad group was repeated for timing. Complete fresh TRX across census, red witnesses and narrow greens totals 179 passes and thirteen historical failures; that is a run count, not unique coverage. The three shell repairs have five narrow green cases with zero failures/skips. Later closure attribution and exact per-case durations are retained in the full generated report and raw manifests.

## Pushed slices before the final report

f0b37a57bbdc0dfc7cb727fa0ec43e14f1dde2a8 docs(CARD-1572): close attributed census and retain eight fixture reds
70d13a557481f2d6777fbdc8f2f2d970a033880d docs(CARD-1572): attribute stale off-runner land refusal and matrix costs
913d9481da2f4d06e75f2fbad1b8c5f182642aed test(CARD-1572): keep transfer permissions separate from fixture mode
8f5f3a6ca27513eda6ac44f989a5fed7dac58eeb docs(CARD-1572): retain baseline transfer setup red witness
7ce941b313b2ef7f81f491a8de2bea66c77bcd9e test(CARD-1572): register acquired caches in race models
730fa85c555b49059897c867b2d539e6f2660c41 docs(CARD-1572): retain baseline race fixture red witnesses
4026e85d4f383683ce0f850c1e8a0050f65b5f34 test(CARD-1572): observe partial cleanup without unowned deletion
18cdd91a33fe23160d60eb5176badada8f634868 docs(CARD-1572): witness stale partial-cleanup observation
d99b941164b67b5d3fe33375d1ef7f145c4d9dcc docs(CARD-1572): retain inherited npm fixture red witness
2b004944635d6b59a7723ec09794179a3c4084c5 docs(CARD-1572): pin outage recovery inventory and bounded scope


## Rc applicability and publication checks


Pinned rc scratch apply/compile/restoration:

```json
{
  "base": "123e5836cc8ef5a950da7204f72dd78d54a0b242",
  "scratch": "/work/worktrees/task-fe9ee70d/.antiphon/1572-recovery/rc-checkout",
  "mainSource": "f0b37a57bbdc0dfc7cb727fa0ec43e14f1dde2a8",
  "start": "2026-10-23T23:48:23.237Z",
  "picks": [
    {
      "sha": "30b41c64780f5204102d3133c457fcc1aa287c43",
      "patch": ".antiphon/1572-recovery/pick-30b41c64780f5204102d3133c457fcc1aa287c43.patch",
      "sha256": "a12f48197a9c97e3ea072a4a6db8435cd0c9c9f92212db2cefcea8367a9d7a40",
      "applied": true
    },
    {
      "sha": "4026e85d4f383683ce0f850c1e8a0050f65b5f34",
      "patch": ".antiphon/1572-recovery/pick-4026e85d4f383683ce0f850c1e8a0050f65b5f34.patch",
      "sha256": "99866c461fa17f1a7b0a91ebffd6063fecdad5b9160e72d34b15071b8053df2d",
      "applied": true
    },
    {
      "sha": "7ce941b313b2ef7f81f491a8de2bea66c77bcd9e",
      "patch": ".antiphon/1572-recovery/pick-7ce941b313b2ef7f81f491a8de2bea66c77bcd9e.patch",
      "sha256": "c25419df9a5f6f3ac94f1ed742e5a6e8951565d6278e06ae9cf3684e6c4018fb",
      "applied": true
    },
    {
      "sha": "913d9481da2f4d06e75f2fbad1b8c5f182642aed",
      "patch": ".antiphon/1572-recovery/pick-913d9481da2f4d06e75f2fbad1b8c5f182642aed.patch",
      "sha256": "9ae816ac3e1b3bc9f7ccce9e9dd4207966cb8c54e968361480ace6773101706c",
      "applied": true
    }
  ],
  "compile": {
    "status": "passed",
    "outputPath": "bin-c1572rcrecovery/",
    "exitCode": 0,
    "errors": 0,
    "warnings": 1146,
    "slotWaitSeconds": 0,
    "assemblySha256": "b7a0c19ae87ecc1a788a45d573f954464401a0e18e8aa665e5adaefd91f4c96e",
    "preparedAt": "2026-10-23T23:48:28.073Z",
    "receiptCapturedAt": "2026-10-23T23:53:14.362Z",
    "reportedMSBuildElapsedSeconds": 206.73,
    "slotHeldSeconds": 208,
    "driverWallSeconds": "unknown (not separately clocked)",
    "logFinalWriteUTC": "2026-10-23T23:52:05.261Z"
  },
  "restored": true,
  "diffSha256": "564cae773dcea384f12d74795a4c365f1c9d64f60039d100c769db54fd8d6da0",
  "outputs": [
    "scripts/fixtures/nightly/owned-child/bin-c1572rcrecovery",
    "server/bin-c1572rcrecovery",
    "src/Antiphon.Agents.Pty/bin-c1572rcrecovery",
    "src/Antiphon.FakeClaude/bin-c1572rcrecovery",
    "src/Antiphon.FakeGrok/bin-c1572rcrecovery",
    "src/Antiphon.FakeLlmApi/bin-c1572rcrecovery",
    "src/Antiphon.HostCleanup/bin-c1572rcrecovery",
    "src/Antiphon.Messaging/bin-c1572rcrecovery",
    "src/Antiphon.Messaging.Client/bin-c1572rcrecovery",
    "src/Antiphon.Messaging.Client.Testing/bin-c1572rcrecovery",
    "src/Antiphon.Messaging.Gateway/bin-c1572rcrecovery",
    "src/Antiphon.Messaging.Slack/bin-c1572rcrecovery",
    "src/Antiphon.NightlyWatchdog/bin-c1572rcrecovery",
    "src/Antiphon.PtyHost/bin-c1572rcrecovery",
    "src/Antiphon.PtyHost.Client/bin-c1572rcrecovery",
    "src/Antiphon.PtyHost.Protocol/bin-c1572rcrecovery",
    "src/Antiphon.Resilience/bin-c1572rcrecovery",
    "src/Antiphon.SessionRunner/bin-c1572rcrecovery",
    "src/Antiphon.SessionRunner.Contracts/bin-c1572rcrecovery",
    "tests/Antiphon.ChannelOutbound.Probe/bin-c1572rcrecovery",
    "tests/Antiphon.Checkpoints.FilterHost/bin-c1572rcrecovery",
    "tests/Antiphon.Checkpoints.LifecycleHost/bin-c1572rcrecovery",
    "tests/Antiphon.Checkpoints.RepeatHost/bin-c1572rcrecovery",
    "tests/Antiphon.CustodyTestChild/bin-c1572rcrecovery",
    "tests/Antiphon.DockerStack.Fixture/bin-c1572rcrecovery",
    "tests/Antiphon.ScriptHarnessHost/bin-c1572rcrecovery",
    "tests/Antiphon.TestTiming.Probe/bin-c1572rcrecovery",
    "tests/Antiphon.Tests/bin-c1572rcrecovery",
    "tools/Antiphon.Checkpoints/bin-c1572rcrecovery",
    "tools/Antiphon.LandGraphAudit/bin-c1572rcrecovery"
  ],
  "statusBeforeRestore": " M scripts/fixtures/c1192-workspace-contract.sh\n M tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs\n",
  "statusAfterRestore": "",
  "outputsRemoved": 30,
  "outputsRemaining": 0,
  "worktreeRemoved": true,
  "host": "0c18bb7f591e",
  "platform": "linux"
}
```

Final hygiene:

```json
{
  "rcCompileExit": 0,
  "rcRestored": true,
  "rcWorktreeRemoved": true,
  "rcOutputsRemoved": 30,
  "rcOutputsRemaining": 0,
  "mainOutputsRemoved": 30,
  "mainOutputsRemaining": 0,
  "compiledInputsChanged": [],
  "trackedSourceCleanBeforeReport": true,
  "allForegroundRunsJoined": true,
  "matchingExecutingDrivers": 0,
  "rcScratchRegistered": false,
  "generatedEvidenceIgnored": true,
  "preReportEvidenceCheck": "EVIDENCE range base=30b41c64780f5204102d3133c457fcc1aa287c43 head=f0b37a57bbdc0dfc7cb727fa0ec43e14f1dde2a8\nEVIDENCE result commits=10 entries=0 violations=0 base=30b41c64780f5204102d3133c457fcc1aa287c43 head=f0b37a57bbdc0dfc7cb727fa0ec43e14f1dde2a8"
}
```

Rc compilation proves applicability, not rc qualification or activation. The predecessor's first shape-green selected zero (exit 8, 3.297s), so it is invalid; only shape-green2's 17 passes at 258.804s are credited. No rc branch/tag/release was pushed. The inherited shape repair 30b41c64780f5204102d3133c457fcc1aa287c43 has predecessor narrow evidence (17 passes) and is included in the scratch pick list; it was not re-executed solely to repeat its 259s cost.

--- next stage ---
next: decide
handoff: Decide the remaining C1066 npm, off-runner land and C849 seed-closure C# fixture compilation scope, commission the real C1514 Linux custody repair, and resolve the frozen legacy-roster cost contract. Consider the independently verified fixture picks separately from incomplete lane/rc qualification.
artifact: /work/worktrees/task-fe9ee70d/docs/investigations/2026-10-23-card-1572-outage-recovery.md
