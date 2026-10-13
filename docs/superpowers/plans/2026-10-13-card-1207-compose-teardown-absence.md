# CARD-1207 Compose teardown absence

Original Code task: 2be1191d-082a-4820-ba16-cb58fdf27379. Original base: 3cea97716b3567d9240d3df52b50cc96053c8fe4; source branch feat/card-task-2be1191d. Repair Code/landing owner: 5c3f0366-48a5-4a7f-8e14-75501670e43f, branch feat/card-task-5c3f0366, worktree /work/worktrees/task-5c3f0366, repair base 55890fe1149789bceeb1c16f54e20700aa0f42bb. Restart: none.

Original root cause: finalization accepted Compose down -v by exit status and discarded controller documents without probing private resources.
Decision: before teardown, persist project volume/network/container names from successful project-label censuses, plus private volume names created by the retention producer. Preserve earlier records across producer and finalizer teardown. Recording failure refuses teardown. After down, confirm each exact name absent only by a successful list omitting it or exit-1 exact missing-resource text through the S7 parser. Present objects and unknown outcomes retain named residue/ledger and refuse success. Use c849_fixture_write for every new case-directory metadata publication; no bypass writer.

Files: docs/docker-stack.md; scripts/c590-remote.sh; scripts/fixtures/c1192-workspace-contract.sh; tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs; this plan.

## Verification design

Specific brief overrides generic Final: no Unit/full class. CP-33..78 are all adjacent CARD-1208 rows at existing counts (98 executions, CP-44 exactly 19); R-1..21 and V-1..25 retain CARD-1208 qualification. CP-79 adds CARD-1207 V-1..5, five argument results. All rows required. No unbounded class impact or full assembly run. Execute serially to bound fixture process load.

Each new case runs the actual retention producer, strict fixture wrapper, finalizer, evidence marker and JSON publisher with modeled Docker. Hand-written expectations check verdict and exact names in residue/ledger.

| ID/case | Guarded defect / mutation | Required red on unchanged production |
|---|---|---|
| V-1 residual | down returns zero, retains four main private volumes | accepted:true and missing named residue/ledger fail refusal assertions |
| V-2 daemon | failed post-down list/inspect accepted | unknown identities missing from residue/ledger |
| V-3 absent | omit recording/confirmation | confirmed-absent ledger for originally acquired names missing |
| V-4 mixed | clear all records after one absent | unknown name missing from residue/ledger |
| V-5 recording | down after failed census/publication | teardown occurs for unrecorded project |

All cases bind C1207_Compose_teardown_requires_recorded_positive_absence. Tests-first CP-79 runs before production edits. No assertion/deadline loosening. Loaded timeout: rerun that row alone once and report both. No repeats after green.

### Positive controls (pending Mutation)

PC-1 residual volume/container/network confirmation bypass; PC-2 failed list/inspect treated absent (daemon, empty, exit2, wrong-name); PC-3 omit confirmed-absent recording; PC-4 mixed whole-record clearing; PC-5 census/publication failure teardown bypass. Detecting method for every variant: /*/*/RemoteScriptContractTests/C1207_Compose_teardown_requires_recorded_positive_absence. All pending method-scoped SourceLanding Mutation; Mutation owns deliberate mutants, red/restore/green and missing-control discovery. Inherited CARD-1208 PC-1..19 and CARD-1192 PC-1..174/variants stay pending. Post-land Mutation not run. W-1 live Docker acceptance pending rollout owner.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-33 | Fixed | `tests/Antiphon.Tests -> bin-c1207/` | linux-no-host-payload | `/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind` | R-1 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-34 | Fixed | `CP-33` | linux-finalization | `/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-35 | Fixed | `CP-33` | linux-workspace-disposal | `/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-36 | Fixed | `CP-33` | linux-evidence-roster | `/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads` | R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-37 | Fixed | `CP-33` | linux-escape-unlink | `/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit` | R-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-38 | Fixed | `CP-33` | linux-log-order | `/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal` | R-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-39 | Fixed | `CP-33` | linux-log-paths | `/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files` | R-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-40 | Fixed | `CP-33` | linux-lane | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python` | R-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-41 | Fixed | `CP-33` | linux-receipt-exclusion | `/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads` | R-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-42 | Fixed | `CP-33` | linux-receipt-consumer | `/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | R-10 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-43 | Fixed | `CP-33` | linux-fixture-inventory | `/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | R-11 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-44 | Fixed | `CP-33` | linux-adjacent | `/*/*/RemoteScriptContractTests/(C1189_*\|C1191_*\|C1193_*\|C1194_*\|C1195_*\|C1192_Summary_*\|C1192_Foreign_*\|C1192_Harness_*\|C1192_Helper_*)*` | R-12 | 19 executed, 0 failed/skipped | 19 | 2 |
| CP-45 | Fixed | `CP-33` | linux-cleanup-consumer | `/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence` | R-13 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-46 | Fixed | `CP-33` | linux-cleanup-helper | `/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence` | R-14 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-47 | Fixed | `CP-33` | linux-cleanup-masked | `/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence` | R-15 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-48 | Fixed | `CP-33` | linux-cleanup-work | `/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence` | R-16 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-49 | Fixed | `CP-33` | linux-cleanup-mask | `/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence` | R-17 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-50 | Fixed | `CP-33` | linux-cleanup-cache | `/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence` | R-18 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-51 | Fixed | `CP-33` | linux-cleanup-network | `/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence` | R-19 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-52 | Fixed | `CP-33` | linux-daemon-loss | `/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success` | R-20 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-53 | Fixed | `CP-33` | linux-acquisition-unknown | `/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue` | R-21 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-54 | Fixed | `CP-33` | audit-54 | `/*/*/RemoteScriptContractTests/C1208_Foreign_cache_is_dropped_and_untouched*` | V-1 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-55 | Fixed | `CP-33` | audit-55 | `/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched*` | V-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-56 | Fixed | `CP-33` | audit-56 | `/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched*` | V-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-57 | Fixed | `CP-33` | audit-57 | `/*/*/RemoteScriptContractTests/C1208_Unacquired_roster_is_untouched*` | V-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-58 | Fixed | `CP-33` | audit-58 | `/*/*/RemoteScriptContractTests/C1208_Acquired_objects_are_removed*` | V-5 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-59 | Fixed | `CP-33` | audit-59 | `/*/*/RemoteScriptContractTests/C1208_Recorded_container_id_does_not_delete_replacement*` | V-6 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-60 | Fixed | `CP-33` | audit-60 | `/*/*/RemoteScriptContractTests/C1208_Legacy_owned_markers_are_migrated*` | V-7 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-61 | Fixed | `CP-33` | audit-61 | `/*/*/RemoteScriptContractTests/C1208_Unknown_identity_retains_residue*` | V-8 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-62 | Fixed | `CP-33` | repair-62 | `/*/*/RemoteScriptContractTests/C1208_Wrong_cidfile_preserves_foreign_container*` | V-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-63 | Fixed | `CP-33` | repair-63 | `/*/*/RemoteScriptContractTests/C1208_Recorded_id_with_changed_labels_is_untouched*` | V-10 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-64 | Fixed | `CP-33` | repair-64 | `/*/*/RemoteScriptContractTests/C1208_Recorded_container_inspect_failure_refuses_cleanup*` | V-11 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-65 | Fixed | `CP-33` | repair-65 | `/*/*/RemoteScriptContractTests/C1208_Wrong_helper_id_preserves_foreign_container*` | V-12 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-66 | Fixed | `CP-33` | repair-66 | `/*/*/RemoteScriptContractTests/C1208_Wrong_network_id_is_untouched*` | V-13 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-67 | Fixed | `CP-33` | repair-67 | `/*/*/RemoteScriptContractTests/C1208_Recorded_network_inspect_failure_refuses_cleanup*` | V-14 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-68 | Fixed | `CP-33` | repair-68 | `/*/*/RemoteScriptContractTests/C1208_Owned_packages_registration_survives_production_controls*` | V-15 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-69 | Fixed | `CP-33` | repair-69 | `/*/*/RemoteScriptContractTests/C1208_Dangling_network_record_retains_residue*` | V-16 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-70 | Fixed | `CP-33` | repair-70 | `/*/*/RemoteScriptContractTests/C1208_Invalid_network_record_retains_text*` | V-17 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-71 | Fixed | `CP-33` | repair-71 | `/*/*/RemoteScriptContractTests/C1208_Confirmed_absent_network_record_is_cleared*` | V-18 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-72 | Fixed | `CP-33` | repair-72 | `/*/*/RemoteScriptContractTests/C1208_Recorded_creation_fingerprint_is_reverified*` | V-19 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-73 | Fixed | `CP-33` | repair-fingerprint-acquisition | `/*/*/RemoteScriptContractTests/C1208_Fingerprint_acquisition_refuses_unowned_paths*` | V-20 | 12 executed, 0 failed/skipped | 12 | 1 |
| CP-74 | Fixed | `CP-33` | repair-safe-metadata | `/*/*/RemoteScriptContractTests/C1208_Metadata_writers_refuse_file_type_changes*` | V-21 | 11 executed, 0 failed/skipped; 99 internal writer/type cases | 11 | 2 |
| CP-75 | Fixed | `CP-33` | publication-update | `/*/*/RemoteScriptContractTests/C1208_Update_publication_replaces_link_without_writing_victim*` | V-22 | 11 executed, 0 failed/skipped | 11 | 1 |
| CP-76 | Fixed | `CP-33` | publication-create | `/*/*/RemoteScriptContractTests/C1208_Create_publication_refuses_link_without_writing_victim*` | V-23 | 4 executed, 0 failed/skipped | 4 | 1 |
| CP-77 | Fixed | `CP-33` | model-inspection-cost | `/*/*/RemoteScriptContractTests/C1208_Model_inspection_reads_live_fields_without_external_reader_processes` | V-24 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-78 | Fixed | `CP-33` | deadline-fixture-setup | `/*/*/RemoteScriptContractTests/C1208_Deadline_fixture_keeps_required_logs_and_omits_unrelated_setup` | V-25 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-79 | Fixed | `CP-33` | compose-absence | `/*/*/RemoteScriptContractTests/C1207_Compose_teardown_requires_recorded_positive_absence*` | CARD-1207 V-1..5 | 5 executed, 0 failed/skipped | 5 | 2 |

### Cost

56 estimated row minutes; preceding adjacent selection took about 19 actual minutes. One isolated test build per committed selection, UseAppHost=false, bin-c1207/. Tests-first CP-79 uses inherited CP-33 build definition. Explained extra tool bootstrap is slot-gated, UseAppHost=false, bin-c1207-tool/. Necessary model diagnostics are explained slot-gated extras. Bound when ANTIPHON_TASK_TOKEN exists, otherwise explicitly unbound. Expected source is committed HEAD; receipts retain actual tested SHA.

## Inherited limitations (disclosure only)

S7 Review 2ec97f2d report /tmp/antiphon-review-2ec97f2d/report.md lists S1-S6 cache namespace prechecks (old c590 lines 3396/3744/3810/3920/4295) treating failed raw inspect as permission to proceed to acquisition checks; and F-7 negative/private-volume observations (old lines 3997/4088) treating raw nonzero inspect as absence. These observations remain unchanged here and need follow-up cards. Native Docker W-1 stays pending.

S7 also disclosed older mid-group acknowledged removals (old lines 3471/3786/3836/3983/4127), including retirement of legacy markers without their own absence probe, and raw-exit/default/best-effort patterns in other c590 cases (image acquisition, interrupted cleanup, retirement, seed/cold-seed, retired caches and nested residue). Those are follow-up scope; CARD-1208 improved acquired-object deletion but this slice makes no whole-file cleanup guarantee.

## Tests-first evidence and implementation

CP-79 at abad95198fda10dac8169168e07e2b4c1a4d654a executed 5, passed 0, failed 5, skipped 0 against byte-identical base production. The residual assertion showed accepted:true / exit:0 with verdict=0, marker=0, all four ledger/residue expectations=0 and all four retained-volume witnesses=1. Absence failed its confirmed-absent ledger expectation; daemon, mixed and recording failed refusal. The first earlier attempt at a1f1a2423 failed fixture argument transport before exercising the defect and receives no defect-proof credit.

After correcting the model's exact label-filter parsing, a slot-gated replay of all five fixtures against the exact base script again produced each intended assertion defect. Candidate diagnostics pass all five with hand-written expectations, including actual container/network survival and ledger names in residual. These direct diagnostics are explained unlisted fixture runs, not TRX ordinary evidence or SourceLanding Mutation.

The producer records identities before temp/main down -v and before the negative project's first down. The finalizer merges earlier records, refuses down when recording fails, and still probes already-recorded names after census loss. Successful empty project censuses need no metadata file because there are no identities to persist; confirmation uses that same immediate empty-census result. Every nonempty record is safely published through c849_fixture_write. Native Docker acceptance is not inferred from the filesystem model.

Contract refinement before Final qualification: every unconfirmed Compose name, including a still-present resource, uses unknown-cleanup-outcome. Reset immediate empty-census state before each acquisition attempt. The interrupted full attempt at e4a45b4d6 was stopped and awaited during build, exit 6; no row credit is claimed. Restart the complete closed selection on the corrected committed source.
