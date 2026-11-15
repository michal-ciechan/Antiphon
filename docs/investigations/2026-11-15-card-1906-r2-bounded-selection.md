# CARD-1906 R-2 bounded selection

Status: INCOMPLETE; 37/68 final R-2 rows have fresh passing TRX. Unique requested cases: 557; executed 426, passed 426, failed 0, skipped 4; pending 127.

Assigned branch: `feat/card-task-c2489e73`. Code base: `1ca3d5a961b78089fe24fdc09a3597c886d4f528`. Actual test/build source: `6ea5fffd11404d8226b8e5ac9857b1a5702f6468`. Final publication changes this manifest only; results are never relabeled to that later publication.

Every receipt source observed: `6ea5fffd11404d8226b8e5ac9857b1a5702f6468`. The assigned fast-forward-only Code-base contract governs; master is used only for pin checks, failure attribution and a read-only merge-tree check.

No product/test source, policy input or derived-index input was changed. No live deployment, retirement, restart, runner drain, Docker volume or rc ref was touched. Mutation checks were operator-waived.

## Prerequisites and rerun commands

Run from the exact source checkout, serially, using native default outputs and the same properties for build/run. A build or slot wait is separate from row execution. All runs below used the host build-slot gate; no unleased bypass was used.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1906-main-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1906-driver-build -- dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1906-runner-build -- dotnet build tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj -p:UseAppHost=false
```

The derived-index driver is a prerequisite of `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`. Its test rewrites a scratch index and verifies changed/unchanged on consecutive calls. This task did not regenerate the repository index or repin inputs.

For every CP row use its Project, Selector and exact Filter below (replace the bracketed placeholders, use a fresh result directory):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1906-CP-N -- dotnet run --project <Project> --no-build -p:UseAppHost=false -- <Selector> "<Filter>" --report-trx --report-trx-filename run.trx --results-directory <fresh-absolute-directory> --no-progress --no-ansi --timeout 9m
```

UID rows intentionally use `--filter-uid` without a simultaneous tree filter. They are separate parameterized cases, not a whole-method selection. Import them as command checkpoints when the checkpoint importer accepts tree filters only. A command checkpoint invokes the inner dotnet command under the existing lease held by the checkpoint tool, rather than nesting another build-slot wrapper; its fresh TRX count/membership audit remains mandatory. Every OR operand has the canonical trailing wildcard. Pipes are escaped only for Markdown tables.

## Class inventory

Main discovery: 23,528 raw markers, 23,528 parsed records, 23,528 unique native UIDs. Multiline display names are retained. Requested main cases: 528; session-runner cases: 29; total 557. Full discovery is inventory only. Actual execution is audited against fresh TRX definitions/native IDs with zero duplicate/unmatched identities required.

Whole-class probes were run once where their cost was unknown. D07 and D17 reached the strict row limit and receive no TRX/pass credit. Historical over-ten-minute D21/D25/D35 were inventoried through bounded method probes rather than repeating their broad aggregate. Their class cost is the sum of bounded probe walls, including repeated discovery; it is not a measured single-class wall.

Heaviest observed methods below show summed TRX case seconds (top three), not row wall or p95. Where covered is below discovered, unrun method costs remain unknown; observed maxima are not a proven class maximum. All timings are single-sample observations under varying host load.

| ID | Project | Class | Discovered / covered | Inventory cost, seconds | Heaviest methods, case seconds |
|---|---|---|---:|---|---|
| D01 | tests/Antiphon.SessionRunner.Tests | C1818GcOwnerRobustnessTests | 10 / 10 | 7.613 class wall | `C1818_budget_kill_reports_a_lock_the_gc_child_wrote` 2.227; `C1818_budget_kill_leaves_a_lock_held_by_an_outside_process` 1.264; `C1818_one_repository_fault_does_not_abort_the_pass` 0.223 |
| D02 | tests/Antiphon.SessionRunner.Tests | CustodyNamespaceBootstrapTests | 8 / 8 | 6.625 class wall | `C1614_Entrypoint_library_guard_continues_unadvertised` 1.209; `C1614_Freeze_failure_kills_nothing` 0.239; `C1614_Namespace_lifecycle_and_boot_failure_are_safe` 0.102 |
| D03 | tests/Antiphon.SessionRunner.Tests | HostStatsProbeParseTests | 11 / 11 | 4.911 class wall | `Server2_compose_indexed_volume_environment_keys_bind_through_AddHostStats` 0.148; `Linux_probe_memory_floor_and_sample_read_the_same_meminfo` 0.133; `Windows_probe_maps_a_failed_GetSystemTimes_to_null` 0.026 |
| D04 | tests/Antiphon.Tests | C0824BranchDeletionRecycleAuditTests | 3 / 3 | 47.363 class wall | `C0824_after_runner_refs_and_origin_delete_audit_passes` 14.376; `C0824_legacy_mirror_blocks_runner_refs_and_origin_kept` 12.271; `C0824_origin_delete_without_runner_refs_audit_refuses` 7.089 |
| D05 | tests/Antiphon.Tests | C1008HostFixtureSetupInsertionTests | 3 / 3 | 12.589 class wall | `One_column_zero_trap_gets_the_setup_once_and_indented_traps_none` 0.034; `No_column_zero_trap_throws_naming_zero` 0.017; `Two_column_zero_traps_throw_naming_two` 0.017 |
| D06 | tests/Antiphon.Tests | C1008PlatformContractTests | 6 / 6 | 10.004 class wall | `C1050_Remote_entries_guard_before_work` 0.872; `C1050_Rolling_entries_guard_before_work` 0.448; `C1050_Audit_rejects_empty_or_incomplete_outcomes` 0.099 |
| D07 | tests/Antiphon.Tests | C1364BoundLandWaitProofTests | 6 / 6 | budget stop 573.936; 739.536 bounded probes | `C1364_Host_recycle_post_removal_reports_waited` 335.442; `C1364_Host_recycle_admission_reports_waited` 246.381; `C1364_Wrapper_retire_volumes_reports_waited` 33.408 |
| D08 | tests/Antiphon.Tests | ClaudeTokenRefreshOptInTests | 6 / 6 | 18.204 class wall | `Verify_parent_threads_the_refresh_request_past_the_dot_source` 3.095; `Manifest_refresh_calls_bw_and_skips_when_the_item_is_unreadable` 1.861; `Default_path_does_not_call_bw_or_warn_logged_out_when_the_file_is_present` 1.704 |
| D09 | tests/Antiphon.Tests | CodexRootExecContractTests | 4 / 4 | 18.719 class wall | `Provider_exec_into_the_runner_names_uid_1654` 10.014; `Entrypoint_heal_chowns_the_tmp_directory_once` 0.159; `Entrypoint_heal_never_names_a_path_outside_tmp` 0.097 |
| D10 | tests/Antiphon.Tests | CodexRunnerImageContractTests | 21 / 21 | 15.155 class wall | `C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` 3.786; `C913_Net9_probe_rejects_warm_caches_and_missing_packs` 0.991; `State_initializer_runs_under_each_compose_state_init_shape` 0.219 |
| D11 | tests/Antiphon.Tests | DindRunnerContractTests | 25 / 25 | 9.285 class wall | `Seed_script_merges_onboarding_and_trust_and_keeps_credentials` 0.156; `Gitconfig_pushes_antiphon_over_ssh_and_other_repositories_over_https` 0.080; `Server2_compose_binds_the_github_token_directory_read_only` 0.022 |
| D12 | tests/Antiphon.Tests | DockerStackDocumentationTests | 19 / 19 | 10.291 class wall | `Docs_name_linux_cgroup_backend` 0.154; `Production_server_on_the_server2_host_is_documented` 0.152; `Lands_proceed_during_rollout_is_documented` 0.150 |
| D13 | tests/Antiphon.Tests | GrokRunnerImageContractTests | 26 / 26 | 11.470 class wall | `Image_version_row_accepts_only_the_successful_qualified_version` 0.382; `Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` 0.050 |
| D14 | tests/Antiphon.Tests | HangfireLeaseDocumentationTests | 2 / 2 | 9.436 class wall | `Durable_hangfire_documents_the_lease_the_package_and_renames` 0.022; `Program_releases_the_worker_lease_when_the_host_stops` 0.010 |
| D15 | tests/Antiphon.Tests | HangfirePostgreSqlStorageTests | 22 / 22 | 33.178 class wall | `Worker_lease_ignores_another_database_holder_and_stops_for_its_own` 1.562; `Recurring_job_survives_a_new_storage_instance_without_duplicating` 1.294; `Disabled_slot_reconcile_stays_absent_on_a_new_storage_instance` 0.932 |
| D16 | tests/Antiphon.Tests | HangfireStorageSelectionTests | 13 / 13 | 11.170 class wall | `Unreachable_tick_retries_until_the_crash_expiry_then_stops_the_host` 0.972; `A_slow_tick_does_not_overlap_the_next_tick` 0.529; `Disabled_census_removes_a_previously_registered_job` 0.251 |
| D17 | tests/Antiphon.Tests | HostJqPrerequisiteScriptTests | 40 / 40 | budget stop 543.971; 563.777 bounded probes | `C1025_Preflight_precedes_every_phase` 139.664; `C1025_Receipt_requires_complete_current_proof` 136.810; `C1025_Preflight_is_fresh_for_each_entry` 94.702 |
| D18 | tests/Antiphon.Tests | JqRunnerImageContractTests | 18 / 18 | 11.185 class wall | `Version_row_accepts_only_exact_successful_pin_without_stderr` 0.280; `C1054_Jq_row_records_found_and_resolved_paths` 0.247; `C1054_Jq_row_rejects_home_shadow` 0.087 |
| D19 | tests/Antiphon.Tests | RcBridgeProbePlatformTests | 7 / 7 | 9.675 class wall | `Docker_stack_records_the_supported_omission` 0.012; `Session_runtime_records_the_linux_omission` 0.006; `Agent_kinds_records_the_linux_omission` 0.006 |
| D20 | tests/Antiphon.Tests | ReleaseImpactEnvelopeTests | 11 / 11 | 60.677 class wall | `OrdinaryModeRemainsAdvisory` 27.041; `AlgorithmSourceDriftIsRefused` 14.831; `AlgorithmDigestBindsCommittedBlobs` 4.152 |
| D21 | tests/Antiphon.Tests | RetiredTempContainerHostTests | 22 / 12 | 599.773 bounded probes | `C1609_Audit_uses_current_image` 141.421; `C1105_Temp_cleanup_uses_previous_generation` 92.918; `C994_Each_removal_rechecks_live_proofs` 82.584 |
| D22 | tests/Antiphon.Tests | RetiredTempContainerScriptTests | 16 / 16 | 218.135 class wall | `C994_Verifier_manifest_is_strict` 73.572; `C994_Retire_recovers_exited_and_absent_temp` 28.360; `C994_Preview_reports_pending_volume_proof` 16.762 |
| D23 | tests/Antiphon.Tests | RollingProductionMountTests | 12 / 12 | 58.319 class wall | `C994_Tmpfs_configuration_survives_exit` 9.271; `C994_Named_volume_identity_and_topology_are_exact` 9.174; `C994_Bind_sources_are_canonical_and_typed` 9.074 |
| D24 | tests/Antiphon.Tests | RollingVolumeRecycleCensusChurnTests | 1 / 1 | 10.950 class wall | `C1099_Fixture_census_skips_a_vanished_foreign_container` 0.164 |
| D25 | tests/Antiphon.Tests | RollingVolumeRecycleScriptTests | 80 / 4 | 845.240 bounded probes | `C1008_Legacy_rolling_and_jq_rosters_remain` 621.685; `C1008_Host_proof_admits_only_an_authenticated_private_origin` 185.453 |
| D26 | tests/Antiphon.Tests | RunnerPushCredentialDocsTests | 1 / 1 | 10.192 class wall | `Credentials_doc_records_the_token_custody_expiry_and_rotation` 0.026 |
| D27 | tests/Antiphon.Tests | SeedSmokeTargetingPackContractTests | 4 / 4 | 16.574 class wall | `Net9_smoke_reference_selects_the_image_targeting_pack` 0.240 |
| D28 | tests/Antiphon.Tests | Server2ProductionServerContractTests | 15 / 15 | 30.533 class wall | `Execute_refusals_stop_before_docker` 17.290; `Execute_drains_large_compose_output_without_echoing_a_secret` 1.313; `WhatIf_prints_the_plan_and_calls_no_docker` 0.962 |
| D29 | tests/Antiphon.Tests | Server2SidecarStandingAgentsTests | 5 / 5 | 9.129 class wall | `Orchestrator_instructions_file_is_on_the_shared_work_volume` 0.026; `Sidecar_claude_config_dir_is_on_runner_state` 0.023; `Server_leaves_executable_checks_to_the_sidecar` 0.016 |
| D30 | tests/Antiphon.Tests | Server2StagingServerContractTests | 10 / 10 | 9.878 class wall | `Staging_schedules_interpreter_diagnose_and_distiller_use_linux_paths` 0.026; `Staging_hangfire_is_on_and_census_legacy_bridge_and_digest_are_off` 0.026; `Staging_residue_is_on_for_this_projects_worktrees_only` 0.026 |
| D31 | tests/Antiphon.Tests | ServerImagePushCredentialTests | 34 / 34 | 9.447 class wall | `Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity` 0.280; `Configured_identity_outranks_inherited_git_identity_and_config_variables` 0.262; `Desktop_without_an_identity_file_keeps_the_inherited_git_environment` 0.261 |
| D32 | tests/Antiphon.Tests | TimeoutAuditTests | 7 / 7 | 10.903 class wall | `Check_rejects_excess_without_exact_justification_measurement_and_card` 0.256; `Comparable_samples_use_nearest_rank_p95_and_exclude_duplicates_failures_and_load_mixing` 0.240; `Windows_row_override_is_inventoried_and_only_compared_to_Windows_history` 0.240 |
| D33 | tests/Antiphon.Tests | VerifyDindRunnerScriptTests | 9 / 9 | 20.576 class wall | `Fast_http_500_does_not_grade_launch_true` 2.038; `Image_is_required` 1.575; `Production_port_is_refused` 1.055 |
| D34 | tests/Antiphon.Tests | VolumeRetirementApplyGateTests | 13 / 13 | 374.971 class wall | `Scripted_apply_refuses_until_the_operation_token_matches` 96.635; `Manual_apply_refuses_until_the_operation_token_matches` 96.118; `Resume_refuses_until_the_operation_token_matches` 81.258 |
| D35 | tests/Antiphon.Tests | VolumeRetirementContentTests | 41 / 7 | 599.847 bounded probes | `Claude_session_keys_are_disposable_state_bound_into_the_inventory` 226.327; `Claude_bash_edit_diff_snapshots_are_disposable_tool_scratch` 118.256; `Private_origin_advanced_head_uses_real_host_proof` 100.571 |
| D36 | tests/Antiphon.Tests | VolumeRetirementReceiptTests | 11 / 4 | budget stop 543.782; 933.403 bounded probes | `Journal_intents_and_partial_resume_are_exact` 428.154; `Manual_attestation_is_exact_and_cannot_waive_findings` 320.093; `Freshness_and_policy_version_are_rechecked` 110.705 |
| D37 | tests/Antiphon.Tests | VolumeRetirementWrapperTests | 6 / 6 | 330.025 class wall | `Retire_temp_uses_current_proof_protocol` 206.605; `Retained_volumes_block_deploy_and_completion` 90.100; `Main_recycle_and_existing_deploy_contracts_are_preserved` 9.225 |
| D38 | tests/Antiphon.Tests | ZombieCensusPlatformTests | 9 / 9 | 8.978 class wall | `Omission_snapshot_throws_and_is_not_empty` 0.051; `Forced_windows_branch_registers_the_wmi_type` 0.035; `Linux_registration_is_not_the_wmi_census` 0.035 |

`VolumeRetirementPublicationTests` is absent from the assigned base and is not in the supplied D01-D38 selection; no execution for that later class is claimed.

## Performance budget and evidence

The overall selection is the caller-commissioned performance Debug for CARD-1906. Every final row must execute in less than 600 seconds; the normal driver limit remains nine minutes. Near the 150-minute task boundary it is tightened to the remaining execution window ending 15:25:30 UTC, with at least two minutes required to admit a row; invocation arguments retain that administrative cap. Future rerun commands use the original nine-minute ceiling. Existing method timeouts/assertions/retry rules were unchanged. Initial forecasts are provisional (not measured p95); Review/Merge reruns provide the bounded next collection samples. Queue admission is reported separately and never credited as execution. Fresh host roots are necessary where startup, topology, publication or refusal is the test subject. Compose seed/setup is shared through the existing fixture; isolated PostgreSQL is initialized once for its run.

TRX retains per-case duration and stdout. Opt-in phase journals retain instrumented fixture/process spans under `.antiphon/c2489e73/timing`; unavailable DB/container/readiness/IO subphases remain unknown rather than zero. `/proc/loadavg` was recorded before every row, including retries, in each `invocation.json`/`receipt.json`. One driver/lease ran at a time. No same-source passing row is owed another repeat solely for this report.

### Checkpoints

The table is the reusable closed selection. Inventory probes are excluded from final sums when a grouped replacement exists. Expected seconds are provisional planning costs; measured execution is slot-held wall including startup/discovery/report/teardown, separate from admission. Counts are TRX executed/passed/failed/notExecuted. Build prerequisites are above.

| CP | Evidence row | Project | Classes/methods | Selector | Exact Filter | Expected s | Measured s / admission s | Executed | Passed | Failed | Skipped |
|---|---|---|---|---|---|---:|---:|---:|---:|---:|---:|
| CP-01 | I04 | tests/Antiphon.Tests | D04 | --treenode-filter | `/*/*/C0824BranchDeletionRecycleAuditTests/*` | 80 | 46.000 / 0.000 | 3 | 3 | 0 | 0 |
| CP-02 | I15 | tests/Antiphon.Tests | D15 | --treenode-filter | `/*/*/HangfirePostgreSqlStorageTests/*` | 60 | 32.000 / 0.000 | 22 | 22 | 0 | 0 |
| CP-03 | I20 | tests/Antiphon.Tests | D20 | --treenode-filter | `/*/*/ReleaseImpactEnvelopeTests/*` | 100 | 60.000 / 0.000 | 11 | 11 | 0 | 0 |
| CP-04 | I22 | tests/Antiphon.Tests | D22 | --treenode-filter | `/*/*/RetiredTempContainerScriptTests/*` | 300 | 217.000 / 0.000 | 16 | 16 | 0 | 0 |
| CP-05 | I23 | tests/Antiphon.Tests | D23 | --treenode-filter | `/*/*/RollingProductionMountTests/*` | 100 | 57.000 / 0.000 | 12 | 12 | 0 | 0 |
| CP-06 | I24 | tests/Antiphon.Tests | D24 | --treenode-filter | `/*/*/RollingVolumeRecycleCensusChurnTests/*` | 30 | 9.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-07 | I34 | tests/Antiphon.Tests | D34 | --treenode-filter | `/*/*/VolumeRetirementApplyGateTests/*` | 510 | 374.000 / 0.000 | 13 | 13 | 0 | 0 |
| CP-08 | D36P01 | tests/Antiphon.Tests | D36 | --treenode-filter | `/*/*/VolumeRetirementReceiptTests/Manual_attestation_is_exact_and_cannot_waive_findings` | 480 | 329.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-09 | D36P02 | tests/Antiphon.Tests | D36 | --treenode-filter | `/*/*/VolumeRetirementReceiptTests/Freshness_and_policy_version_are_rechecked` | 360 | 118.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-10 | D36P03 | tests/Antiphon.Tests | D36 | --treenode-filter | `/*/*/VolumeRetirementReceiptTests/Every_delete_rechecks_full_live_guards` | 360 | 45.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-11 | D36P04 | tests/Antiphon.Tests | D36 | --treenode-filter | `/*/*/VolumeRetirementReceiptTests/Journal_intents_and_partial_resume_are_exact` | 500 | 436.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-12 | D36P05 | tests/Antiphon.Tests | D36 | --treenode-filter | `/*/*/VolumeRetirementReceiptTests/(Preview_receipt_is_required_and_consumed*)\|(Receipt_shape_custody_and_digest_are_strict*)\|(Proof_directory_custody_matches_the_applying_account*)\|(Volume_incarnation_and_operation_are_bound*)\|(Legacy_receipts_are_history_only*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-13 | D36P06 | tests/Antiphon.Tests | D36 | --treenode-filter | `/*/*/VolumeRetirementReceiptTests/(Changed_content_and_remote_proof_refuse*)\|(Lock_order_excludes_supported_writers*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-14 | D07M5 | tests/Antiphon.Tests | D07 | --treenode-filter | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_post_removal_reports_waited` | 400 | 344.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-15 | D07M1 | tests/Antiphon.Tests | D07 | --treenode-filter | `/*/*/C1364BoundLandWaitProofTests/C1364_Wrapper_redeploy_reports_waited` | 360 | 27.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-16 | D07M2 | tests/Antiphon.Tests | D07 | --treenode-filter | `/*/*/C1364BoundLandWaitProofTests/C1364_Wrapper_cleanup_reports_waited` | 360 | 30.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-17 | D07M3 | tests/Antiphon.Tests | D07 | --treenode-filter | `/*/*/C1364BoundLandWaitProofTests/C1364_Wrapper_retire_volumes_reports_waited` | 360 | 41.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-18 | D07M4 | tests/Antiphon.Tests | D07 | --treenode-filter | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_admission_reports_waited` | 360 | 260.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-19 | D07M6 | tests/Antiphon.Tests | D07 | --treenode-filter | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_cleanup_reports_waited` | 360 | 29.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-20 | D17P01 | tests/Antiphon.Tests | D17 | --treenode-filter | `/*/*/HostJqPrerequisiteScriptTests/C1025_Preflight_is_fresh_for_each_entry` | 240 | 104.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-21 | D17P02 | tests/Antiphon.Tests | D17 | --treenode-filter | `/*/*/HostJqPrerequisiteScriptTests/C1025_Preflight_precedes_every_phase` | 240 | 148.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-22 | D17P03 | tests/Antiphon.Tests | D17 | --treenode-filter | `/*/*/HostJqPrerequisiteScriptTests/C1025_Receipt_requires_complete_current_proof` | 240 | 145.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-23 | D17P04 | tests/Antiphon.Tests | D17 | --treenode-filter | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)\|(C1058_Digest_is_bound_to_open_descriptor*)\|(C1058_Installed_pin_is_checked_before_execution*)\|(C1058_Version_probe_uses_open_descriptor*)\|(C1058_True_probe_uses_open_descriptor*)\|(C1058_False_probe_uses_open_descriptor*)\|(C1058_Receipt_formatter_uses_open_descriptor*)\|(C1058_Changed_leaf_cannot_emit_success_proof*)\|(C1058_Pre_execution_link_checks_are_independent*)\|(C1058_Identity_comparisons_include_device*)\|(C1058_Final_link_checks_are_independent*)\|(C1058_Final_permission_checks_are_independent*)\|(C1058_Proof_is_buffered_until_final_admission*)\|(C1058_Metadata_syntax_refuses*)\|(C1058_Observation_failures_are_invalid*)\|(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1058_Staged_unlink_failure_refuses*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_rejects_canonical_leaf_symlink*)\|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_requires_missing_jq*)\|(C1025_Provision_admits_host_prerequisites*)\|(C1025_Provision_refuses_unsafe_destination*)\|(C1025_Provision_serializes_and_rechecks*)\|(C1025_Provision_verifies_download_before_use*)\|(C1025_Provision_publishes_complete_no_clobber*)\|(C1025_Provision_cleans_only_owned_staging*)\|(C1025_Provision_requalifies_published_jq*)\|(C1025_Provision_requires_canonical_source*)\|(C1025_Transport_is_bounded_and_reaped*)\|(C1025_Real_transport_emits_no_pipeline_output*)\|(C1025_Receipt_persistence_is_required*)\|(C1025_Transport_preserves_secret_custody*)\|(C1025_Explicit_tool_phases_never_deploy*)\|(C1025_Final_recycle_guard_remains*)` | 360 | 163.000 / 0.000 | 37 | 37 | 0 | 0 |
| CP-24 | L01 | tests/Antiphon.SessionRunner.Tests | D01, D03, D02 | --treenode-filter | `/*/*/(C1818GcOwnerRobustnessTests*)\|(CustodyNamespaceBootstrapTests*)\|(HostStatsProbeParseTests*)/*` | 40 | 9.000 / 0.000 | 29 | 29 | 0 | 0 |
| CP-25 | L02 | tests/Antiphon.Tests | D11, D10, D09, D16, D12, D18, D14, D19, D05, D06, D13, D08 | --treenode-filter | `/*/*/(C1008HostFixtureSetupInsertionTests*)\|(C1008PlatformContractTests*)\|(ClaudeTokenRefreshOptInTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(DindRunnerContractTests*)\|(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(HangfireLeaseDocumentationTests*)\|(HangfireStorageSelectionTests*)\|(JqRunnerImageContractTests*)\|(RcBridgeProbePlatformTests*)/*` | 360 | 20.000 / 0.000 | 147 | 147 | 0 | 3 |
| CP-26 | L03 | tests/Antiphon.Tests | D31, D30, D28, D29, D26, D38, D27, D32, D33 | --treenode-filter | `/*/*/(RunnerPushCredentialDocsTests*)\|(SeedSmokeTargetingPackContractTests*)\|(Server2ProductionServerContractTests*)\|(Server2SidecarStandingAgentsTests*)\|(Server2StagingServerContractTests*)\|(ServerImagePushCredentialTests*)\|(TimeoutAuditTests*)\|(VerifyDindRunnerScriptTests*)\|(ZombieCensusPlatformTests*)/*` | 150 | 30.000 / 0.000 | 93 | 93 | 0 | 1 |
| CP-27 | D25P01 | tests/Antiphon.Tests | D25 C1008_Legacy_rolling_and_jq_rosters_remain(present) | --filter-uid | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.1.1.C1008_Legacy_rolling_and_jq_rosters_remain(System.String, System.Threading.CancellationToken).1.1.0` | 390 | 225.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-28 | D25P02 | tests/Antiphon.Tests | D25 C1008_Legacy_rolling_and_jq_rosters_remain(absent) | --filter-uid | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.1.1.C1008_Legacy_rolling_and_jq_rosters_remain(System.String, System.Threading.CancellationToken).2.1.0` | 390 | 212.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-29 | D25P03 | tests/Antiphon.Tests | D25 C1008_Legacy_rolling_and_jq_rosters_remain(missing-shell) | --filter-uid | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.1.1.C1008_Legacy_rolling_and_jq_rosters_remain(System.String, System.Threading.CancellationToken).3.1.0` | 390 | 210.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-30 | D25P04 | tests/Antiphon.Tests | D25 C1008_Legacy_rolling_and_jq_rosters_remain(failing-shell) | --filter-uid | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.1.1.C1008_Legacy_rolling_and_jq_rosters_remain(System.String, System.Threading.CancellationToken).4.1.0` | 390 | NOT COMPLETE | - | - | - | - |
| CP-31 | D25P05 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/C1008_Host_proof_admits_only_an_authenticated_private_origin` | 420 | 194.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-32 | D25P06 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Busy_routed_and_land_in_flight_refuse*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-33 | D25P07 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Host_proof_file_omits_credentials*)\|(C1008_Option_manifest_is_strict*)\|(C1008_Present_or_unknown_temp_keeps_null_refusal*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-34 | D25P08 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Preview_helper_removal_is_owned*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Retired_absent_null_is_accepted*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-35 | D25P09 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Same_sha_and_partial_retries_are_safe*)\|(C1008_Transport_scripts_are_ascii*)\|(C1008_Wrapper_option_manifest_is_strict*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-36 | D25P10 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Wrapper_refusal_receipts_do_not_leak_secrets*)\|(C1008_Wrapper_retired_absent_null_is_accepted*)\|(C1087_Census_filters_open_work_and_pending_lands*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-37 | D25P11 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1087_Check_census_phase_is_read_only*)\|(C1087_Project_resolves_by_repository_identity*)\|(C1087_Read_failures_name_their_cause*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-38 | D25P12 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1317_Census_tolerates_a_task_that_settles_without_a_land*)\|(C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1*)\|(C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-39 | D25P13 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance*)\|(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands*)\|(C1353_Wrapper_waits_for_a_bound_land_then_proceeds*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-40 | D25P14 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail*)\|(C1361_Wrapper_census_tolerates_open_status_changes_between_passes*)\|(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-41 | D25P15 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1371_Post_removal_count_drift_does_not_abort*)\|(C1374_Wrapper_census_recovers_transient_reads*)\|(C1594_Antiphon_evidence_gitdir*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-42 | D25P16 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1594_Cross_sha_cleanup_image_binds_the_retired_temp*)\|(C1594_Disagreeing_cross_sha_images_refuse*)\|(C1594_Older_retirement_receipt_is_ignored*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-43 | D25P17 | tests/Antiphon.Tests | D25 | --treenode-filter | `/*/*/RollingVolumeRecycleScriptTests/(C1693_Retire_temp_census_ignores_standing_runner_churn_between_passes*)\|(C1697_Wrapper_standing_census_compares_only_rows_bound_to_server2*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-44 | D21P01 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C1105_Temp_cleanup_uses_previous_generation*)\|(C1609_Audit_uses_current_image*)\|(C1609_Cleanup_does_not_require_historical_image*)` | 360 | 309.000 / 0.000 | 3 | 3 | 0 | 0 |
| CP-45 | D21P02 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C994_Absent_cleanup_is_idempotent*)\|(C994_Census_and_inspect_fail_closed*)\|(C994_Cleanup_receipts_redact_unapproved_fields*)` | 360 | 59.000 / 0.000 | 3 | 3 | 0 | 0 |
| CP-46 | D21P03 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C994_Container_service_and_state_are_restricted*)\|(C994_Each_removal_rechecks_live_proofs*)\|(C994_Exited_owned_containers_are_removed_without_volumes*)` | 360 | 190.000 / 0.000 | 3 | 3 | 0 | 0 |
| CP-47 | D21P04 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C994_Final_absence_is_observed*)\|(C994_Fixture_custody_is_bounded*)\|(C994_Host_context_is_strict*)` | 360 | 37.000 / 0.000 | 3 | 3 | 0 | 0 |
| CP-48 | D21P05 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C994_Host_main_admission_is_required*)\|(C994_Host_preview_never_mutates*)\|(C994_Host_retirement_proof_is_typed*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-49 | D21P06 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C994_Host_task_and_land_census_is_complete*)\|(C994_Older_retirement_receipt_does_not_block_cleanup*)\|(C994_Production_mount_topology_is_proven*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-50 | D21P07 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/(C994_Receipt_write_and_copy_fail_closed*)\|(C994_Removal_failure_and_crash_retry_preserve_progress*)\|(C994_Rollout_lock_covers_final_census*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-51 | D21P08 | tests/Antiphon.Tests | D21 | --treenode-filter | `/*/*/RetiredTempContainerHostTests/C994_Whole_candidate_set_precedes_removal` | 360 | NOT COMPLETE | - | - | - | - |
| CP-52 | D35P01 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof` | 420 | 109.000 / 0.000 | 1 | 1 | 0 | 0 |
| CP-53 | D35P02 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Absent_work_does_not_skip_other_volumes*)\|(All_target_roles_are_classified*)` | 360 | 38.000 / 0.000 | 2 | 2 | 0 | 0 |
| CP-54 | D35P03 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Audit_identity_and_namespace_are_stable*)\|(Claude_bash_edit_diff_snapshots_are_disposable_tool_scratch*)` | 360 | 140.000 / 0.000 | 2 | 2 | 0 | 0 |
| CP-55 | D35P04 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Claude_session_keys_are_disposable_state_bound_into_the_inventory*)\|(Current_tooling_and_missing_history_do_not_block*)` | 360 | 307.000 / 0.000 | 2 | 2 | 0 | 0 |
| CP-56 | D35P05 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Every_ref_recovery_tip_and_tag_object_requires_publication*)\|(Evidence_names_and_filesystem_shapes_fail_closed*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-57 | D35P06 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Exchange_exports_only_this_projects_registered_origins*)\|(Exchange_inventory_diagnosis_skips_git_error_lines*)\|(Exchange_inventory_refusal_names_its_check_and_detail_file*)\|(Exchange_malformed_private_origin_lines_are_never_approved*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-58 | D35P07 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Exchange_proof_failure_names_its_stage*)\|(Exchange_registry_refusals_name_the_registry_cause*)\|(Exchange_registry_with_a_wrong_shape_is_invalid_before_the_exchange*)\|(Fetch_head_requires_proven_objects_not_fresh_names*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-59 | D35P08 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Fixture_setup_runs_once_across_audit_traps*)\|(Gone_tracking_retains_original_commit_protection*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-60 | D35P09 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Hold_lift_clear_after_deploy_is_not_refused_for_its_own_volumes*)\|(Hold_lift_clear_refuses_a_retired_or_other_build_runner*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-61 | D35P10 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(No_content_or_credentials_are_executed_or_exported*)\|(Only_exact_known_seed_fixtures_are_disposable*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-62 | D35P11 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Preview_lists_every_offender_without_effects*)\|(Pristine_inner_docker_layout_is_disposable_and_bound_into_the_inventory*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-63 | D35P12 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Read_failures_and_incomplete_inventory_refuse*)\|(Real_census_read_returns_only_the_census_text*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-64 | D35P13 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof*)\|(Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-65 | D35P14 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(State_credentials_refuse_before_removal*)\|(Temp_recycle_generation_binding_refuses_a_present_owned_container_alone*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-66 | D35P15 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/(Temp_recycle_refuses_a_present_owned_container_before_any_audit*)\|(Temp_recycle_topology_layer_refuses_a_present_owned_container_alone*)` | 360 | NOT COMPLETE | - | - | - | - |
| CP-67 | D35P16 | tests/Antiphon.Tests | D35 | --treenode-filter | `/*/*/VolumeRetirementContentTests/Worktree_content_and_admin_closure_are_complete` | 360 | NOT COMPLETE | - | - | - | - |
| CP-68 | I37 | tests/Antiphon.Tests | D37 | --treenode-filter | `/*/*/VolumeRetirementWrapperTests/*` | 450 | 329.000 / 0.000 | 6 | 6 | 0 | 0 |

Final-row totals: executed **426**, passed **426**, failed **0**, skipped **4**. Pending native identities: 127. Identity audit errors: 0.

Completed row execution sum: 5432.000 seconds; admission sum: 0.000 seconds; longest completed row: D36P04 436.000 seconds.

## Historical outcomes and attribution

- I07: `C1364BoundLandWaitProofTests/*`, exit 1, 573.936 seconds wall, no TRX. Pure row-budget cancellation; no passing credit. Retried its timeout-active post-removal method once alone: 1 passed, 345.178 seconds wall. All six methods then passed in bounded rows.
- I17: `HostJqPrerequisiteScriptTests/*`, exit 3, 543.971 seconds wall, no TRX. Pure row-budget cancellation; no passing credit. Retried its timeout-active fresh-preflight method once alone: 1 passed, 104.878 seconds wall. Its four bounded replacements cover all 40 cases.
- G01: 114 executed, 113 passed, 1 failed, 1 skipped. `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`, `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs:205`: expected `first.Exit == 0`, actual 1 because the native-default driver DLL did not exist.
- Bare master `8ca2ca57fca282be239a6d4326499b8106eba514`, identical exact method filter `/*/*/DerivedImpactIndexTests*/regenerate_script_reports_changed_then_unchanged`: 1 executed, 0 passed, 1 failed, 0 skipped, identical missing-DLL assertion. This is an inherited build prerequisite, not an introduced product failure. Baseline main build was 180 slot-held seconds.
- Built the missing driver through the gate: 0 warnings/errors, 5 slot-held seconds. G01-FIX: 1 executed/passed, 0 failed/skipped, 13.747 seconds wall. No source repair or assertion change. Initial guard red remains historical.

- I36: `VolumeRetirementReceiptTests/*`, exit 3, 543.782 seconds wall, no TRX. The phase journal records four completed passing methods, then journal/resume failing during cancellation. It receives no TRX/pass credit. Six bounded replacements were bound; manual attestation passed alone in 330.128 seconds and journal/resume passed alone in 437.500 seconds, resolving the cancellation outcome without a source repair.

## Required guard union

Exact filter:

```text
/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*
```

Guard union after the single affected-method rerun: 114 executed, 114 passed, 0 failed, 1 existing native Windows skip. Skip: `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`. This is not Windows execution or a zero-skip certificate.

Fresh C1729 stdout: `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

Master pins were independently checked (initial d100f8fa2c679a5c74b6b235087308776a8f5d50 and baseline 8ca2ca57fca282be239a6d4326499b8106eba514): policyHash `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`, PinnedInputDigest `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`, LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`. Branch policyHash remains `16917233483bf9f13534d8d5f86a1bdbdfabe4a4981153127287537c25bb5179`; branch PinnedInputDigest remains `0a6eaab72768e0e3f65c1a0cf15ef47c4471f913397389ca6900b9538b44fe00`. No inputs were repinned.

## Provenance and retained evidence

Main test DLL SHA-256: `f390efbbe72d7b6f849f98cd4a228ab0f8329025b74e29e8352aa02fb68a343f`. Session-runner test DLL SHA-256: `7ab30beb1cd6239adc33404ca5edacead7e6e692fe8d4edbe46d50af9a6ad4c3`. Main build: 187 slot-held seconds, 0 errors. Session-runner build: 26.4 seconds driver wall, 0 errors.

Ignored evidence root: `/work/worktrees/task-c2489e73/.antiphon/c2489e73/`. Each row directory retains `invocation.json`, `receipt.json`, `run.log`, and fresh `run.trx` where emitted. `audit.json` retains exact executed identities, durations and pending identities; discovery JSON/diagnostic logs retain every selected UID. Generated evidence is not committed. Baseline receipt/build log is copied under `baseline/` before the detached comparison checkout is removed.

Native Windows skips in the final R-2 selection:

- `Antiphon.Tests.Infrastructure.RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count`.
- `Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact`.
- `Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact`.
- `Antiphon.Tests.Infrastructure.ZombieCensusPlatformTests.Windows_registration_uses_the_wmi_census`.

Pending final rows: `D36P05`, `D36P06`, `D25P04`, `D25P06`, `D25P07`, `D25P08`, `D25P09`, `D25P10`, `D25P11`, `D25P12`, `D25P13`, `D25P14`, `D25P15`, `D25P16`, `D25P17`, `D21P05`, `D21P06`, `D21P07`, `D21P08`, `D35P05`, `D35P06`, `D35P07`, `D35P08`, `D35P09`, `D35P10`, `D35P11`, `D35P12`, `D35P13`, `D35P14`, `D35P15`, `D35P16`.

Publication checks and exact final SHA are in the task report; this document retains actual tested-source provenance.
