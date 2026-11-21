# CARD-2043 retire-temp verification

The retirement audit must refuse when content or publication cannot be proved. A registered nested scratch worktree stays in the normal audit roster. A clean checkout can pass once its HEAD and all recovery tips are reachable from the host proof. A scratchpad path and task completion confer no exemption.

## Verification design

- V-1 checks that a status failure remains Unknown and carries a bounded, redacted executable diagnostic.
- V-2 checks that repository-configured fsmonitor and clean filters cannot run in the offline helper.
- R-1 covers the clean published checkout, a dirty file, an unpublished detached HEAD, and an unpublished reflog entry after returning to the published HEAD.
- R-2 checks the existing content and preview contracts, including the read-only mount probe inside the fixture helper namespace.
- R-3 checks the remotely impacted methods and standing policy guards.
- V-3 checks shell syntax, evidence policy over the task range, and a conflict-free merge-tree against current master.

All shell behavior changes run under the Linux bash retirement helper. The fake Docker adapter mounts isolated fixture paths and generated origin proof. The verification does not use a live runner or delete real volumes.

Pending SourceLanding controls: PC-1 discard status stderr (V-1), PC-2 re-enable repository fsmonitor (V-2), PC-3 leave external filter commands configured (V-2), PC-4 omit linked checkout content (R-1 dirty), PC-5 omit linked HEAD and reflog publication roots (R-1 unpublished/reflog).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c2043-red/` | audit-red | `/*/*/VolumeRetirementContentTests/(Nested_scratch_status_failure_reports_safe_stderr*)\|(Nested_scratch_audit_does_not_execute_repository_commands*)\|(Nested_scratch_requires_clean_content_and_published_recovery_tips*)` | V-1, V-2, R-1 | four intended failures and four passing controls | 8 | 8 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c2043-final/` | retirement | `/*/*/(VolumeRetirementContentTests*)\|(RollingVolumeRecycleScriptTests*)/*` | V-1, V-2, R-1, R-2 | both classes, all selected cases green | 8 | 12 | true |
| CP-3 | S2 | CP-2 | remote-impact | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-3 | all five impacted methods green | 5 | 1 | true |
| CP-4 | S2 | CP-2 | standing-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all nine classes green | 78 | 1 | true |
