# RemoteScriptContractTests debug and repair, 2026-10-26

Two reproduced fixture defects repaired. The latest receipts cover 265 distinct Windows-capable cases in RemoteScriptContractTests with 265 passed, zero failed and zero skipped. This combines baseline checks with targeted repair checks at the source tips below; it is not a whole-class run at one SHA or native Windows qualification. The 103 cases in 40 methods requiring native Linux were outside this Windows-failure diagnostic selection.

Task: 0d5de8f1. Branch: feat/card-task-0d5de8f1. Assigned base and fetched origin/rc/2026-10-26: bedea1c03cd788d83b2ff54c40bfb603927bd69c. Final code tip: 809aecd748cd918f896e4ad333514f504af65f9e. Host: d66b7f130eb4, Debian 12, .NET 9.0.22, TUnit 1.44.0, Microsoft Testing Platform 2.2.2. Work stayed on the assigned branch; every slice was pushed without reset, rebase or force.

## Findings and changes

1. **Inherited fixture filesystem race.** C1192_Race_programs_and_feed_share_the_work_volume failed on unchanged base: race-code=2, race-diagnosis=ControlNotSensitive and race-boundary=0. An exact-method rerun passed, demonstrating intermittency. The filesystem Docker model implemented a read-only mount by removing directory write permission from the shared backing tree. A reader probe could therefore deny a concurrent writer's readiness-marker creation. Real Docker read-only mounts restrict their own mount rather than revoking another mount's write access. This model originated in f433cd91802d58175d3a71c08070a23bca0e5742.

   The committed witness interleaves a peer write at the reader command in the existing race scenario, for both production file and existence probes. Before the repair, the race itself completed (race-code=0), but race-reader-permits-peer-write=0 and the existing race-boundary assertion failed. After the repair that exact method passed. model_mount_exec now executes only the exact five-argument sh commands `test -f "$1"` and `test -e "$1"` without changing shared permissions. All other read-only commands retain the permission model, including the reader-cannot-prune negative check. The existing mount, feed, barrier, lock and pruning assertions remain. No production race logic, deadline, retry or assertion was weakened.

2. **Additional current-rc fixture-control leak.** C944_All_cache_loop_variables_are_local reported `c849_fixture_inventory_controls: kind`. The CARD-1589 control helper added by 12b630bdd23ed77a0c40a10cf88b741257f3af4e did not declare its loop variable. This helper was added after the original qualification candidate 105792f7d22f297746cdb338976f52e424b76fa7, so this additional regression is not attributed to that candidate's one Windows failure. The repair adds `local kind` inside the fixture control helper in scripts/c590-remote.sh. Its guard was also run against archived base-commit source and failed with the same assertion before the fix. The guard and its existing deliberate leak counterexample pass after the fix. The local inventory harness passes 23 status/census vectors, two positive control shapes and 16 expected-red PC-31 variants.

The c590-remote.sh change is one local declaration in its fixture-control path; deployed inventory acceptance and rollout behavior are unchanged. The other code change is confined to scripts/fixtures/c1192-workspace-contract.sh. No C# test source changed. These are fixture-only repairs under the brief's no-Review authority. The native Windows failing method identity/TRX was unavailable on Linux, so the exact original failure is not claimed resolved without its native rerun.

The previous relevant rc picks were already present: 867559e9f335e32be5aba8fcb2ce303cb6eb1f1a (native diagnostics), 19fd782f6096c7207d2a7937f7b31c95e9f70e3f (owned race caches), and fd64043e229f55f602ed2629bb98ea57f3ffc13e (transfer permissions versus fixture mode). They were not reapplied. The four existing C980 Windows-path witnesses passed on Linux, including forced Windows strings and conversion failures. No Windows path/CRLF defect was reproduced or inferred.

## Commits

- fdd1a107406672e010e03102ac76f840cc90665f — deterministic peer-writer witness; expected red confirmed before repair.
- caaab44cfc4d900c3c391501200df4d26d46657e — read probes preserve peer-writer permissions; exact witness green.
- 809aecd748cd918f896e4ad333514f504af65f9e — local inventory-control loop variable; guard and controls green.

## Verification and provenance

One isolated build at the assigned base, through scripts/build-slot.ps1: 0 errors, 1,196 warnings; MSBuild wall 250.08 seconds, slot held 251 seconds, slot wait 0. OutputPath was bin-debug-0d5de8f1/ with UseAppHost=false. All subsequent test executions reused this output with --no-build; script fixtures are read from the checkout at execution time, and no compiled C# source changed. Every group recorded the same test DLL SHA-256: 1c3ec797378f08d2a05bbc2d3b4d8f1f5d78ee464b50690b8fcfea6659aad14d.

Every build/test driver used the host build-slot gate, including the local shell controls. No desktop access, native lock, stack restart, deployment, live Docker, release script or tag operation was performed. Source stayed frozen during each run. TRX method definitions were checked against every intended method and its source-declared argument count; all 15 completed groups have exact nonzero rosters, with no missing or extra methods.

The base C944 reproduction used the identical built DLL beneath a local archive of the base commit's Antiphon.sln, c590-remote.sh and test source. RepoRoot walks upward from that binary to the archived solution, so the guard read the base script. Both the original and archived DLL hashes matched the hash above. This avoided another build or a branch switch.

Historical diagnostics remain retained: the initial bare-OR TUnit filter selected zero tests (exit 8), then was corrected to the pinned suffix-star OR syntax before crediting execution. A bash -x trace without a separate trace descriptor disturbed fixture admission (FixtureNamespaceUnknown); another with a separate descriptor produced FixtureRestoreBarrierFailed. Neither trace is credited as the deterministic red witness. The committed witness's ordinary exact-method red is independent of these diagnostics.

TRX retains per-case durations, exact membership and outcomes. Opt-in timing sidecars were enabled for grouped runs and race/barrier checks. Uninstrumented internal filesystem/process phases and outer timing for the standalone guard runs remain unknown; they are not counted as zero. Group wall times below include their gate/driver overhead and are separate from case body durations. No timeout was increased. Fresh native timing and Windows qualification remain outstanding.

Raw ignored evidence: /work/worktrees/task-0d5de8f1/.antiphon/debug-0d5de8f1. This includes TRX, commands, source identities, source-case census, method rosters, timing JSONL, build log, hashes, trace diagnostics and output-cleanup inventory. All 33 owned project bin-debug-0d5de8f1 directories and the archived binary staging directory were removed after testing; no task-owned alternate output remains.

## Native rerun rows

Run after the current desktop qualification supervisor has finished, using a single current isolated build and the normal build-slot gate. This task did not take its lock or run native tests. The same compiled-output recipe below applies on Windows with that host's owned output path.

| Row | Exact treenode filter | Expected cases | Purpose |
|---|---|---:|---|
| N-1 | `/*/*/RemoteScriptContractTests/C1192_Race_programs_and_feed_share_the_work_volume` | 1 | Windows/WSL reproduction of the repaired race and deterministic peer-write witness |
| N-2 | `/*/*/RemoteScriptContractTests/C944_All_cache_loop_variables_are_local` | 1 | New rc inventory-control scope regression |
| N-3 | `/*/*/RemoteScriptContractTests/(C1192_Race_barriers_observe_volume_state*)|(C1192_Lock_probe_distinguishes_shared_and_private_scratch*)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)` | 3 | Barrier, shared/private locks and read-only reader versus writable pruning preservation |

Retain fresh TRX and report any declared prerequisite skips. Reconcile N-1 with the actual failed method in the desktop 8f055f87 split-21 TRX; that identity was not supplied here.

## Landing checks

At code tip 809aecd748cd918f896e4ad333514f504af65f9e, fetched origin/rc/2026-10-26 remained bedea1c03cd788d83b2ff54c40bfb603927bd69c and merge-tree was clean, tree 533c27395a18667286e5bd450e0243dce7ca0e97.

Fetched master was b6db2de3dd0c0bad2ba30823d01882ca6fc9388e. Direct merge-tree against master reported three conflicts: .antiphon/task-af2b78c8.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md and scripts/c590-remote.sh. The first two also conflict when checking the unchanged assigned base. The third appears with the task's one-line edit across separately cherry-picked histories. Both changed script files at master are byte-identical to the assigned base (blobs 86fc0507dc36ee96336f9c85d871802adc4f6ad4 and 19480412120fb236295647f8cc727e8c306a8a28). Replay the task's own commits through the caller's landing rebase for rc and master. A direct master merge is not certified conflict-free. No merge/rebase was performed here.

## Execution receipts

The following generated table preserves the actual outcomes and source tips. Baseline red receipts remain red; later exact repair receipts supply their passing evidence. The 265-case figure is the latest result per case across these receipts, not a claim that every case was rerun at the final code tip.

| Run | Source tip | Passed / failed / skipped | Group wall seconds | Roster |
|---|---|---|---:|---|
| baseline-v2-00 | bedea1c03cd7 | 14 / 0 / 0 | 84.600 | exact |
| baseline-v2-01 | bedea1c03cd7 | 20 / 0 / 0 | 126.435 | exact |
| baseline-v2-02 | bedea1c03cd7 | 16 / 0 / 0 | 251.875 | exact |
| baseline-v2-03 | bedea1c03cd7 | 12 / 0 / 0 | 39.686 | exact |
| baseline-v2-04 | bedea1c03cd7 | 46 / 0 / 0 | 88.592 | exact |
| baseline-v2-05 | bedea1c03cd7 | 11 / 1 / 0 | 45.814 | exact |
| repair-06 | caaab44cfc4d | 12 / 0 / 0 | 46.317 | exact |
| repair-07 | caaab44cfc4d | 26 / 1 / 0 | 20.304 | exact |
| repair2-08 | 809aecd748cd | 22 / 0 / 0 | 98.269 | exact |
| repair2-09 | 809aecd748cd | 12 / 0 / 0 | 6.063 | exact |
| repair2-10 | 809aecd748cd | 12 / 0 / 0 | 5.576 | exact |
| repair2-11 | 809aecd748cd | 12 / 0 / 0 | 38.568 | exact |
| repair2-12 | 809aecd748cd | 22 / 0 / 0 | 14.506 | exact |
| repair2-13 | 809aecd748cd | 21 / 0 / 0 | 83.826 | exact |
| repair2-14 | 809aecd748cd | 5 / 0 / 0 | 6.859 | exact |
| barriers-green | 809aecd748cd | 1 / 0 / 0 | outer unknown; body 00:00:10.7168169 | exact method |
| race-base-exact | bedea1c03cd7 | 1 / 0 / 0 | outer unknown; body 00:00:01.8568116 | exact method |
| race-witness-red | fdd1a1074066 | 0 / 1 / 0 | outer unknown; body 00:00:05.0223165 | exact method |
| race-witness-green | caaab44cfc4d | 1 / 0 / 0 | outer unknown; body 00:00:02.0039067 | exact method |
| c944-base | bedea1c03cd7 | 0 / 1 / 0 | outer unknown; body 00:00:00.3282264 | exact method |
| c944-green | 809aecd748cd | 1 / 0 / 0 | outer unknown; body 00:00:00.4466812 | exact method |

Latest distinct-case outcome: 265 passed, 0 failed, 0 skipped; 0 intended methods missing. The source census selects 265 cases across 175 methods and excludes the 103 explicitly native-Linux cases across 40 methods.

All completed group filters follow. Their per-run JSON retains literal argv. To rerun a row after one owned build:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label remote-contract-rerun -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-remote-contract-rerun/ --property:UseAppHost=false -- --treenode-filter '<row filter>' --report-trx --report-trx-filename run.trx --results-directory <fresh-results-directory>
```

### baseline-v2-00

```text
/*/*/RemoteScriptContractTests/(C913_Seed_accepts_complete_ordinary_packages_without_framework_packs*)|(C913_Full_marker_binds_verified_recovery_before_publication*)|(C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct*)|(C913_Prune_validates_recovery_without_refilling_image_packs*)|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp*)|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims*)|(C980_Repo_root_assignment_preserves_linux_bytes*)|(C980_Repo_root_assignment_converts_windows_paths*)|(C980_Linux_shell_rejects_unconverted_repo_root*)|(C980_Repo_root_conversion_failure_stops_before_body*)|(C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block*)|(C905_Linux_shell_finds_pwsh_when_installed*)|(Every_case_declares_a_lane*)|(C849_Cache_cases_use_only_the_validated_host_lane*)
```

### baseline-v2-01

```text
/*/*/RemoteScriptContractTests/(C1361_Host_census_tolerates_open_status_changes_between_list_and_detail*)|(C1361_Host_census_tolerates_open_status_changes_between_passes*)|(C1066_Fixture_tree_fault_reaches_validator_under_nounset*)|(C1066_Fixture_npm_install_uses_argument_log_under_nounset*)|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset*)|(C1189_Fixture_package_zip_does_not_call_host_pwsh*)|(C1189_Fixture_npm_integrity_does_not_call_host_pwsh*)|(C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit*)|(C1191_Lock_probe_is_created_before_uid_handoff*)|(C1191_Fixture_exit_removes_owned_workspace_without_host_sudo*)|(C1192_Fixture_has_no_host_payload_tree_or_payload_bind*)|(C1192_Finalization_precedes_success_and_preserves_primary_failure*)
```

### baseline-v2-02

```text
/*/*/RemoteScriptContractTests/(C1192_Evidence_export_preserves_fixture_roster_without_payloads*)|(C1192_Cleanup_consumer_requires_positive_absence*)|(C1192_Cleanup_helper_requires_positive_absence*)|(C1192_Cleanup_masked_requires_positive_absence*)|(C1192_Cleanup_work_requires_positive_absence*)|(C1192_Cleanup_mask_requires_positive_absence*)|(C1192_Cleanup_cache_requires_positive_absence*)|(C1192_Cleanup_network_requires_positive_absence*)|(C1192_Daemon_loss_after_compose_refuses_success*)|(C1192_Acquisition_unknown_ownership_retains_registration_and_residue*)|(C1207_Compose_teardown_requires_recorded_positive_absence*)|(C1208_Foreign_cache_is_dropped_and_untouched*)
```

### baseline-v2-03

```text
/*/*/RemoteScriptContractTests/(C1208_Legacy_container_replacement_is_untouched*)|(C1208_Legacy_network_replacement_is_untouched*)|(C1208_Unacquired_roster_is_untouched*)|(C1208_Acquired_objects_are_removed*)|(C1208_Recorded_container_id_does_not_delete_replacement*)|(C1208_Legacy_owned_markers_are_migrated*)|(C1208_Unknown_identity_retains_residue*)|(C1208_Wrong_cidfile_preserves_foreign_container*)|(C1208_Recorded_id_with_changed_labels_is_untouched*)|(C1208_Recorded_container_inspect_failure_refuses_cleanup*)|(C1208_Wrong_helper_id_preserves_foreign_container*)|(C1208_Wrong_network_id_is_untouched*)
```

### baseline-v2-04

```text
/*/*/RemoteScriptContractTests/(C1208_Recorded_network_inspect_failure_refuses_cleanup*)|(C1208_Owned_packages_registration_survives_production_controls*)|(C1208_Dangling_network_record_retains_residue*)|(C1208_Invalid_network_record_retains_text*)|(C1208_Confirmed_absent_network_record_is_cleared*)|(C1208_Recorded_creation_fingerprint_is_reverified*)|(C1208_Fingerprint_acquisition_refuses_unowned_paths*)|(C1208_Metadata_writers_refuse_file_type_changes*)|(C1208_Update_publication_replaces_link_without_writing_victim*)|(C1208_Create_publication_refuses_link_without_writing_victim*)|(C1208_Model_inspection_reads_live_fields_without_external_reader_processes*)|(C1208_Deadline_fixture_keeps_required_logs_and_omits_unrelated_setup*)
```

### baseline-v2-05

```text
/*/*/RemoteScriptContractTests/(C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt*)|(C1192_Workspace_registration_is_absent_only_and_survives_prepare*)|(C1192_Cleanup_releases_only_registered_current_run_resources*)|(C1192_Partial_workspace_acquisition_is_cleaned*)|(C1192_Toxic_probe_stays_in_work_volume*)|(C1192_Summary_validator_runs_under_the_production_interpreter*)|(C1192_Foreign_volume_is_refused_after_create*)|(C1192_Harness_cleanup_stays_inside_its_private_directory*)|(C1192_Harness_cleanup_refuses_ancestor_swap*)|(C1192_Helper_argv_keeps_discarded_env_file_visible*)|(C1192_Race_programs_and_feed_share_the_work_volume*)|(C1192_Race_barriers_observe_volume_state*)
```

### repair-06

```text
/*/*/RemoteScriptContractTests/(C1192_Lock_probe_distinguishes_shared_and_private_scratch*)|(C1192_Npm_seed_install_and_refill_use_work_volume*)|(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed*)|(C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed*)|(C1192_Real_cache_cases_keep_the_host_payload_backend*)|(C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy*)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*)|(C1193_Lock_probe_passes_home_and_nuget_scratch*)|(C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal*)|(C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files*)|(C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution*)
```

### repair-07

```text
/*/*/RemoteScriptContractTests/(C1194_Fixture_budget_scale_refuses_empty_observation*)|(C1192_Apphost_mask_is_an_empty_exact_volume_mount*)|(C1195_Fixture_import_chmod_restores_volume_root*)|(C1195_Seed_import_chmod_restores_volume_root*)|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*)|(C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work*)|(C1316_Recycle_audit_cap_clears_the_standing_volume*)|(C1105_Jq_argv_forms_are_absent*)|(C1105_Jq_file_forms_are_present*)|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(C944_All_cache_loop_variables_are_local*)|(C951_Cache_paths_refuse_symlinked_mountpoints_and_refusing_sudo*)
```

### repair2-08

```text
/*/*/RemoteScriptContractTests/(C976_Missing_helper_image_preserves_final_receipt_diagnosis*)|(C946_Rolling_harness_cleans_only_its_owned_success_root*)|(C957_Scratch_mutation_controls_go_red_and_restore*)|(C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence*)|(C983_Required_jq_refuses_unavailable_probe_before_any_group*)|(C983_Required_jq_driver_rejects_successful_false_probe*)|(C849_front_door_passes_every_full_case_name_to_the_invoker*)|(Nested_lane_never_uses_sudo_or_python*)|(C1067_Nested_lane_sudo_guard_is_sensitive*)|(Testing_payload_expects_linux_backend_never_windows*)|(Custody_containment_case_measures_the_v1_path_as_the_app_uid*)|(Custody_residue_counts_subdirectories_only*)
```

### repair2-09

```text
/*/*/RemoteScriptContractTests/(Custody_containment_is_routed_to_the_remote_and_not_left_pending*)|(Linux_custody_case_requires_the_linux_backend*)|(Scrub_covers_github_token_prefixes*)|(Smoke_deletes_its_branch*)|(Child_project_is_run_scoped_and_distinct*)|(Host_compose_uses_the_deployed_tag_not_this_runs_sha*)|(Retirement_is_anchored_on_the_run_scoped_prefix*)|(Host_daemon_is_never_pruned*)|(Lane_detection_separates_a_bare_host_from_the_runner*)|(Deploy_parent_probes_the_phone_home_secret_as_the_app_uid*)|(Deploy_parent_phone_home_grep_matches_the_runners_registration_failure_template*)|(Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing*)
```

### repair2-10

```text
/*/*/RemoteScriptContractTests/(Deploy_parent_retires_its_own_superseded_images*)|(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store*)|(Temp_retire_requires_the_retired_receipt_and_downs_only_temp_volumes*)|(Deploy_parent_creates_the_git_identity_file_without_overwriting*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents*)|(Host_lane_ownership_reset_never_enters_the_codex_home*)
```

### repair2-11

```text
/*/*/RemoteScriptContractTests/(C849_Cache_prepare_is_idempotent_and_preserves_payloads*)|(C849_Cache_prepare_refuses_foreign_or_unsafe_roots*)|(C849_Prune_refuses_stale_or_busy_authority*)|(C849_Closure_preflight_names_a_missing_dependency*)|(C849_Seed_publishes_complete_payloads_before_its_marker*)|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters*)|(C849_Saved_donor_rejects_declared_size_bomb_before_writing*)|(C849_Retired_guard_refuses_docker_ps_errors*)|(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C0995_Cold_ready_seed_refuses_a_temp_container*)
```

### repair2-12

```text
/*/*/RemoteScriptContractTests/(C849_Cold_marker_reuse_writes_gate_receipts*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_ordering_contract_is_pinned*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)|(C849_Docker_volume_paths_have_no_bare_host_reads*)|(C849_Prune_preview_is_read_only_and_bounded*)|(C997_Cache_budget_gate_uses_controlled_disk_readings*)|(C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance*)|(C997_Saved_donor_probe_failure_refuses_before_copy*)|(C997_Saved_donor_default_probe_remains_real*)|(C849_Prune_and_rollback_retain_roots_and_recovery*)|(C849_Cache_receipts_exclude_credentials_and_payloads*)
```

### repair2-13

```text
/*/*/RemoteScriptContractTests/(C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots*)|(C912_Cold_seed_refuses_when_created_volume_disappears_before_init*)|(C912_Cold_volumes_seed_accepts_busy_main_without_packages*)|(C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets*)|(C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries*)|(C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit*)|(C912_Cold_marker_has_distinct_validation_and_full_context_refusal*)|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads*)|(C912_Cold_exception_never_weakens_maintenance_or_donor_gates*)|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)|(C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune*)|(C973_Prune_and_saved_donor_still_require_full_after_seed_image_prune*)
```

### repair2-14

```text
/*/*/RemoteScriptContractTests/(C1168_State_init_tolerates_a_file_deleted_mid_walk*)|(C1168_State_init_refuses_a_persistent_chown_error*)|(C1168_State_init_owns_a_dangling_symlink*)|(C1331_State_init_chowns_a_populated_tree_in_find_batches*)|(C1168_Rollout_docs_keep_redeploy_while_temp_holds_the_shared_store*)
```
