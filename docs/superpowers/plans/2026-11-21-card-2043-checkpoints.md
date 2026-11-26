# CARD-2043 retire-temp verification

The retirement audit must refuse when content or publication cannot be proved. A registered nested scratch worktree stays in the normal audit roster. A clean checkout can pass once its HEAD and all recovery tips are reachable from the host proof. A scratchpad path and task completion confer no exemption.

## Verification design

- V-1 checks that a status failure remains Unknown and carries a bounded, redacted executable diagnostic.
- V-2 checks that repository-configured fsmonitor and clean filters cannot run in the offline helper.
- R-1 covers the clean published checkout, a dirty file, an unpublished detached HEAD, and an unpublished reflog entry after returning to the published HEAD.
- R-2 checks the existing content and preview contracts, including the read-only mount probe inside the fixture helper namespace.
- R-3 checks the remotely impacted methods and standing policy guards.
- V-3 checks shell syntax, evidence policy over the task range, and a conflict-free merge-tree against current master.

The Linux bash helper is the production path. The fake Docker adapter mounts isolated fixture paths and generated origin proof. These estimates are provisional until one red and one green sample are recorded. The final rows build each entry assembly once and select the affected classes and contract methods. The nightly and rc pipelines own the Unit lane.

The pending method-scoped controls are in the [unbound candidate](../mutations/2026-11-21-card-2043-retirement-audit-candidate.md).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c2043-red/` | audit-red | `/*/*/VolumeRetirementContentTests/(Nested_scratch_status_failure_reports_safe_stderr*)\|(Nested_scratch_audit_does_not_execute_repository_commands*)\|(Nested_scratch_requires_clean_content_and_published_recovery_tips*)` | V-1, V-2, R-1 | four intended failures and four passing controls | 8 | 8 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c2043-final/` | content | `/*/*/VolumeRetirementContentTests/(Nested_scratch_status_failure_reports_safe_stderr*)\|(Nested_scratch_audit_does_not_execute_repository_commands*)\|(Nested_scratch_requires_clean_content_and_published_recovery_tips*)\|(Exchange_inventory_diagnosis_skips_git_error_lines*)` | V-1, V-2, R-1, R-2 | all nine selected cases green | 9 | 9 | true |
| CP-3 | S2 | CP-2 | remote-impact | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-3 | all five impacted methods green | 5 | 1 | true |
| CP-4 | S2 | CP-2 | standing-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all nine classes green | 78 | 1 | true |
| CP-5 | S2 | CP-2 | rolling-preview | `/*/*/RollingVolumeRecycleScriptTests/C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt` | R-2 | read-only preview, dirty and unpublished guards | 1 | 4 | true |
| CP-6 | S2 | CP-2 | native-roster | `/*/*/WindowsUnitPlatformAdmissionInvariantTests/C1210_Roster_closure_matches_source_gates` | R-3 | roster closure is green | 1 | 2 | true |
| CP-7 | S2 | CP-2 | app-spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | R-3 | application spawn limiter contract is green | 1 | 2 | true |
| CP-8 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c2043-pty/` | pty-spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | R-3 | both Pty limiter methods green | 2 | 4 | true |
| CP-9 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c2043-runner/` | runner-spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | R-3 | both runner limiter methods green | 2 | 6 | true |
