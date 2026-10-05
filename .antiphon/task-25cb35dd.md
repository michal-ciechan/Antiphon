CARD-0820 V-7 Windows qualification passed: CP-13 through CP-22, 114 passed, 0 failed, 0 skipped; exactly two executions per selected case.

Original Code task / landing owner: 25cb35dd-eabd-4c9a-be03-ae5c03d40649. Branch: feat/card-task-25cb35dd. Worktree: C:\Antiphon\worktrees\card-task-25cb35dd.
Task base and actual tested SHA for every group: 1b30bbe15220a93b9cf68ebfa33384112ae35d82. No production or test source changed. This report is the only committed deliverable; its later commit is not the tested SHA.
Plan: docs/superpowers/plans/2026-10-05-card-0820-windows-unit-timing-plan.md at the tested SHA. The explicit brief commissions only CP-13..CP-22 and prohibits whole Unit runs. No integration classes or additional manual acceptance were commissioned. CP-1..CP-12 were not rerun or credited by this task. No commissioned V/R IDs are deferred.

Runner defaults and runner catalogue were read before execution (JSON under C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd). Defaults revision 2 prefers Linux for portable work; the assigned Windows lane was available and dispatchEligible. No runner was pinned. OS: Microsoft Windows 10.0.19045; 8 logical cores; 34,298,142,720 bytes physical RAM (31.94 GiB). Build slots enabled, budget 2, maxcpucount 4.
Qualification used the existing ambient workload with no generated CPU load. Recorded CPU across the four groups ranged from 66.56% to 100%; this does not reproduce the historical M-2.n host/load. No Antiphon.Agents.Pty.Tests or FakeClaude run was scheduled here; neither executable was observed in the process census during the final group.

All groups ran via the prescribed slot-wrapped checkpoint tool with --expected-source-sha 1b30bbe15220a93b9cf68ebfa33384112ae35d82 --row-timeout 5m --total-timeout 10m --serial --repeat 2. Each executor was awaited to exit 0; no exit 75 was left outstanding. Exactly four isolated Antiphon.Tests builds were executed, one per group; six rows reused their own group build. The report builds list includes unused manifest entries, not eight executed builds per group.
All four reports validated through the checkpoint tool with --expected-repeat 2. Fresh native TRX was inspected for every intended class/method and argument case, nonzero counts, and both ordinals. Every row records dirty=0 sourceState=clean buildSource=verified; each ordinal contains exactly half its row total, all Passed. No red rows, retries, failure-driven reruns, budget increases, or assertion changes.

| Group | Run | Rows | Passed | Build seconds | Build slot/wait | Launcher slot/wait | Run wall seconds |
|---|---|---|---:|---:|---|---|---:|
| win1 | 20261005-094804-2b64 | CP-13,CP-14,CP-15,CP-16 | 62 | 114.521 | slot=granted waited=0s | slot=granted waited=0s | 175.159 |
| win2 | 20261005-095214-84f9 | CP-17,CP-18 | 14 | 103.416 | slot=granted waited=0s | slot=granted waited=0s | 134.900 |
| win3 | 20261005-095552-111f | CP-19,CP-20 | 6 | 106.615 | slot=granted waited=76s | slot=granted waited=1s | 219.646 |
| win4 | 20261005-100016-0bfc | CP-21,CP-22 | 32 | 99.902 | slot=granted waited=0s | slot=granted waited=0s | 129.702 |

Per-row load samples cover observed running/green boundaries plus periodic samples between them; runner sampleAt is retained to show the native five-second sampler age. Raw before/after group and row snapshots: winN-load.jsonl. Null/missing load is not treated as zero; there were no failed snapshots.

| CP | CPU % range | Available GiB range | Native case seconds range | Row slot/wait |
|---|---:|---:|---:|---|
| CP-13 | 86.52–99.10 | 6.71–6.80 | 2.696015–4.606484 | slot=granted waited=0s |
| CP-14 | 79.65–99.10 | 6.80–8.28 | 0.059195–0.217523 | slot=granted waited=0s |
| CP-15 | 66.56–87.15 | 8.28–8.47 | 0.000093–0.043522 | slot=granted waited=0s |
| CP-16 | 66.56–78.25 | 8.12–8.62 | 1.512353–3.265134 | slot=granted waited=0s |
| CP-17 | 89.57–96.48 | 6.72–6.92 | 0.000741–0.074182 | slot=granted waited=0s |
| CP-18 | 84.82–89.57 | 6.49–6.92 | 0.186268–0.191898 | slot=granted waited=0s |
| CP-19 | 75.00–91.75 | 6.37–7.25 | 0.146175–0.219213 | slot=granted waited=0s |
| CP-20 | 73.79–91.75 | 7.25–7.45 | 2.432967–2.432969 | slot=granted waited=0s |
| CP-21 | 83.87–89.80 | 6.30–6.92 | 0.093395–0.865655 | slot=granted waited=0s |
| CP-22 | 75.34–89.80 | 6.92–6.95 | 0.120873–0.140511 | slot=granted waited=0s |

V-1 PASS CP-13; V-2 PASS CP-14/CP-15; V-3 PASS CP-17/CP-18; V-4 PASS CP-19; V-5 PASS CP-21; V-6 PASS CP-16; V-7 PASS CP-13..CP-22. R-1 PASS CP-13/CP-15; R-2 PASS CP-14; R-3 PASS CP-17; R-4 PASS CP-20; R-5 PASS CP-21/CP-22; R-6 PASS CP-15.

Native held-file results: both returned Removed after exactly two attempts. Attempt timestamps were 47.5/104.8 ms and 16.7/74.2 ms; final outcomes at 105.9 and 75.1 ms. Native test durations were 0.2192078 and 0.2192127 seconds. Fresh TRX contains the monotonic C820 phase receipts (owner-delay, driver entry/cancellation, owner-unverified, release entry, execution join; renew-delay/http/next-delay/stop, appended diagnostics, drain entry/cancellation, lease disposal). These prove the named received conditions; independent handler-call or root-operation timestamp traces are not emitted by the unchanged source. Handler counts, one-step completion, cancellation, release counts, persisted state/report, root retention/deletion and journals remain the existing outcome assertions in the green methods; no extra telemetry or source instrumentation was added.

Fresh TRX roster (argument-expanded counts include both ordinals):

| CP | Class.method | Native results |
|---|---|---:|
| CP-13 | CheckpointTaskOwnershipTests.ownership_loss_cancels_slot_wait_and_rejects_a_late_grant | 2 |
| CP-13 | CheckpointTaskOwnershipTests.terminal_publication_waits_for_driver_exit_and_lease_disposal | 2 |
| CP-13 | CheckpointTaskOwnershipTests.uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified | 2 |
| CP-13 | CheckpointTaskOwnershipTests.late_settlement_after_owner_unverified_cancels_the_running_row | 2 |
| CP-13 | CheckpointTaskOwnershipTests.aborted_phase_joins_execution_before_root_teardown | 2 |
| CP-14 | CheckpointTempScopeTests.unfinished_registered_work_retains_roots | 2 |
| CP-14 | CheckpointTempScopeTests.completed_faulted_work_reports_failure_and_deletes_roots | 2 |
| CP-14 | CheckpointTempScopeTests.sealed_scope_awaits_registered_work | 2 |
| CP-14 | CheckpointTempScopeTests.teardown_preserves_failure_and_attempts_other_roots | 2 |
| CP-15 | CheckpointTimingHarnessTests.single_step_delay_never_opens_future_requests | 2 |
| CP-15 | CheckpointTimingHarnessTests.single_step_delay_cancellation_does_not_advance_time | 2 |
| CP-15 | CheckpointTimingHarnessTests.phase_wait_surfaces_early_execution_failure | 2 |
| CP-15 | CheckpointTimingHarnessTests.phase_deadline_is_finite_and_shared | 2 |
| CP-15 | CheckpointTimingHarnessTests.phase_deadline_links_test_cancellation | 2 |
| CP-15 | CheckpointTimingHarnessTests.missing_phase_reports_condition_and_joins_work | 32 |
| CP-16 | CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases | 2 |
| CP-17 | BuildSlotClientTests.renewable_grant_is_renewed_until_the_checkpoint_releases_it | 2 |
| CP-17 | BuildSlotClientTests.renewal_delay_is_separate_from_acquisition_delay | 2 |
| CP-17 | BuildSlotClientTests.pid_liveness_grant_with_null_renewal_is_granted_without_renewing | 2 |
| CP-17 | BuildSlotClientTests.null_renewal_interval_is_granted_without_renewal | 2 |
| CP-17 | BuildSlotClientTests.release_on_dispose | 2 |
| CP-17 | BuildSlotClientTests.busy_waits_then_grants | 2 |
| CP-18 | CheckpointSlotContractTests.renew_and_release_diagnostics_keep_status_and_body | 2 |
| CP-19 | EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open | 2 |
| CP-19 | EvidenceFolderTests.non_image_remove_drops_the_tool_copy | 2 |
| CP-20 | CheckpointToolCopyCleanupTests.failed_delete_receipt_is_truthful_and_retryable | 2 |
| CP-21 | CheckpointTempRootSweepTests.live_roots_are_skipped_without_taking_the_root_lock | 2 |
| CP-21 | CheckpointTempRootSweepTests.young_roots_are_skipped_without_taking_the_root_lock | 2 |
| CP-21 | CheckpointTempRootSweepTests.uncertain_roots_are_skipped_without_taking_the_root_lock | 4 |
| CP-21 | CheckpointTempRootSweepTests.root_lock_is_still_required_for_dead_candidates | 2 |
| CP-21 | CheckpointTempRootSweepTests.eligibility_is_rechecked_after_the_root_lock | 12 |
| CP-21 | CheckpointTempRootSweepTests.grace_is_additional_to_dead_ownership | 2 |
| CP-21 | CheckpointTempRootSweepTests.resumed_deletion_rechecks_every_veto | 2 |
| CP-21 | CheckpointTempRootSweepTests.marker_survives_partial_deletion | 2 |
| CP-22 | CheckpointToolCopyCleanupTests.nested_live_executor_vetoes_whole_root | 2 |
| CP-22 | CheckpointToolCopyCleanupTests.unknown_nested_custody_vetoes_whole_root | 2 |

Essential unedited CHECKPOINT lines:

```text
CHECKPOINT CP-13 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=ok filter=/*/*/CheckpointTaskOwnershipTests/(ownership_loss_cancels_slot_wait_and_rejects_a_late_grant*)|(terminal_publication_waits_for_driver_exit_and_lease_disposal*)|(uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified*)|(late_settlement_after_owner_unverified_cancels_the_running_row*)|(aborted_phase_joins_execution_before_root_teardown*) executed=10 passed=10 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win1\20261005-094804-2b64\rows\CP-13\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=reused filter=/*/*/CheckpointTempScopeTests/(unfinished_registered_work_retains_roots*)|(completed_faulted_work_reports_failure_and_deletes_roots*)|(sealed_scope_awaits_registered_work*)|(teardown_preserves_failure_and_attempts_other_roots*) executed=8 passed=8 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win1\20261005-094804-2b64\rows\CP-14\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=reused filter=/*/*/CheckpointTimingHarnessTests/(single_step_delay_never_opens_future_requests*)|(single_step_delay_cancellation_does_not_advance_time*)|(phase_wait_surfaces_early_execution_failure*)|(phase_deadline_is_finite_and_shared*)|(phase_deadline_links_test_cancellation*)|(missing_phase_reports_condition_and_joins_work*) executed=42 passed=42 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win1\20261005-094804-2b64\rows\CP-15\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=reused filter=/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win1\20261005-094804-2b64\rows\CP-16\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=ok filter=/*/*/BuildSlotClientTests/(renewable_grant_is_renewed_until_the_checkpoint_releases_it*)|(renewal_delay_is_separate_from_acquisition_delay*)|(pid_liveness_grant_with_null_renewal_is_granted_without_renewing*)|(null_renewal_interval_is_granted_without_renewal*)|(release_on_dispose*)|(busy_waits_then_grants*) executed=12 passed=12 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win2\20261005-095214-84f9\rows\CP-17\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=reused filter=/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win2\20261005-095214-84f9\rows\CP-18\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=ok filter=/*/*/EvidenceFolderTests/(tool_copy_removal_retries_while_a_file_is_still_held_open*)|(non_image_remove_drops_the_tool_copy*) executed=4 passed=4 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win3\20261005-095552-111f\rows\CP-19\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=reused filter=/*/*/CheckpointToolCopyCleanupTests/failed_delete_receipt_is_truthful_and_retryable executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win3\20261005-095552-111f\rows\CP-20\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=ok filter=/*/*/CheckpointTempRootSweepTests/(live_roots_are_skipped_without_taking_the_root_lock*)|(young_roots_are_skipped_without_taking_the_root_lock*)|(uncertain_roots_are_skipped_without_taking_the_root_lock*)|(root_lock_is_still_required_for_dead_candidates*)|(eligibility_is_rechecked_after_the_root_lock*)|(grace_is_additional_to_dead_ownership*)|(resumed_deletion_rechecks_every_veto*)|(marker_survives_partial_deletion*) executed=28 passed=28 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win4\20261005-100016-0bfc\rows\CP-21\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=1b30bbe15220a93b9cf68ebfa33384112ae35d82 build=reused filter=/*/*/CheckpointToolCopyCleanupTests/(nested_live_executor_vetoes_whole_root*)|(unknown_nested_custody_vetoes_whole_root*) executed=4 passed=4 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd\win4\20261005-100016-0bfc\rows\CP-22\run.trx slot=granted waited=0s repeat=2 repetitions=2/2 hostInvocations=1 dirty=0 source=1b30bbe15220a93b9cf68ebfa33384112ae35d82 sourceState=clean buildSource=verified
```

Evidence root: C:\Antiphon\worktrees\card-task-25cb35dd\.antiphon\c820-25cb35dd. Each group run contains report.json/report.md, manifest.resolved.yaml, executor.log, exact build-source evidence and rows/CP-n/run.trx. Group stdout/stderr logs and load JSONL remain ignored. trx-audit.json records the independent count/roster/load/duration inspection. Each report contains SHA-qualified clean source/build receipts; generated payloads are not committed.
Additional build/test drivers: only the plan-required tool launcher implicit builds using bin-c820-tool/, inside scripts/build-slot.ps1 (four group invocations). The wrapper states that dotnet run implicit builds use the default node count; the test builds use the granted maxcpucount=4. Validation launches were read-only and slot-gated. One initial validator invocation used an incorrect DLL subdirectory and exited before validation; the corrected existing DLL path validated successfully without another build or test execution. No whole Unit, namespace, assembly, or full-class test run was added.

SourceLanding Mutation pending: PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27, PC-28, PC-29, PC-30, PC-31, PC-32, PC-33, PC-34, PC-35, PC-36, PC-37, PC-38, PC-39, PC-40, PC-41, PC-42, PC-43, PC-44, PC-45, PC-46, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58. PC-30 has Unknown and ReusedPid variants; PC-32 has AliveSame, Unknown and ReusedPid variants. Every other PC has one variant: 58 IDs / 61 cycles all pending. No deliberate mutant was run; ordinary green does not discharge any PC. Mutation must run method-scoped red/restore/green and missing-control discovery after caller-confirmed landing.
Restart: none; activation/restart owner is the caller. This verification does not change a running server or runner. The checkpoint tool removed its group outputs and shadow tools; the task-owned launcher output remains at tools/Antiphon.Checkpoints/bin-c820-tool/: automatic approval review rejected both the recursive-enumeration cleanup and the narrower verified literal-path deletion, returning only "blocked by policy". No deletion command executed; caller owns this remaining cleanup. The full task base..report HEAD evidence-diff guard and push are recorded in the final task result.

--- next stage ---
next: review
handoff: Review CARD-0820 S1-S4 at tested SHA 1b30bbe15220a93b9cf68ebfa33384112ae35d82 using CP-13..CP-22 Windows repeat-2 clean receipts (114 passed); all 58 PCs/61 variants remain pending for caller-commissioned SourceLanding Mutation after the original Code task lands.
artifact: docs/superpowers/plans/2026-10-05-card-0820-windows-unit-timing-plan.md
[antiphon-report:25cb35dd done]
