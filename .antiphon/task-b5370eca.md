# CARD-0950 Code report

Fixed StandingSessionSelectionTests portability and pushed the implementation: Linux improves from 12/39 to 39/39 passed, no skips, no weakened assertions, no product fixes. All three scratch mutations cause the intended assertion failures and pass after exact restoration. Windows execution remains pending for the explicitly separate Windows task.

Implementation commit: b7f399a4ef59403e6c0c34f4ee61deef089a1d16. Start ref: e4706adabfb74c5498da506dcce25e1e098ad1f8. A later report-only commit persists this artifact; the final message supplies its published SHA and final-SHA repetition receipts.

Files changed: tests/Antiphon.Tests/Application/StandingSessionSelectionTests.cs (private fixture factory plus constructor call sites), tests/Antiphon.Tests/Application/StandingSessionSelectionTests.Composition.cs (same factory and portable profile executable), and this requested report artifact. StandingRecoveryFixture, shared BuildHarness, StandingSessionOwnershipTests and PreContinuityDbContext are unchanged. No source collision with CARD-0966; no production changes.
## Design note (written before source edits)

Baseline at e4706adabfb74c5498da506dcce25e1e098ad1f8 reproduced exactly: 39 executed, 12 passed, 27 failed, 0 skipped. One isolated build and exact `/*/*/(StandingSessionSelectionTests*)/*` filter through `scripts/run-checkpoint.ps1` (the brief explicitly requires direct driver use).

Signature 1: both the shared harness registry definition and this class's profile revisions use `Path.Combine(Environment.SystemDirectory, "cmd.exe")`. On Linux this is the unresolved bare name `cmd.exe`; configuration preflight's `AgentExecutableResolver.EnsureSpawnable` refuses it before standing selection.

Signature 2: the same preflight refusal carries generic `conflict`, masking the specific selection/continuity codes and the later NotFound/ServiceUnavailable exceptions. No product defect is established by these baseline failures. The portable-fixture run will determine whether any remain.

Chosen approach: a factory private to StandingSessionSelectionTests wraps StandingRecoveryFixture and overrides only its fake registry executable. Use the existing Windows cmd.exe path on Windows and /bin/sh on Unix. The profile seed uses the same executable. The adapters remain fake; no shell is launched. No PATH or process-global environment mutation, no skips, no assertion changes, no shared fixture or production changes. Files: StandingSessionSelectionTests.cs and StandingSessionSelectionTests.Composition.cs only.

Baseline classification (every failing argument row listed):

| Method | Failing arguments | Baseline signature / provisional cause |
|---|---|---|
| Owned_history_uses_current_composition_and_native_resume_identity | ClaudeCode/false, ClaudeCode/true, Grok/false, Grok/true | cmd.exe resolution only, profile executable |
| Equivalent_canonical_cwd_is_accepted_without_reusing_historical_arguments | single | cmd.exe resolution only, registry executable |
| Legacy_historical_owner_can_resume_after_pointer_moved | false, true | cmd.exe resolution only, registry executable |
| Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility | false/false, false/true | cmd.exe resolution only |
| Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility | true/false, true/true | generic conflict masks standing_continuity_held |
| Invalid_target_matrix_preserves_pointer_generation_hold_and_queue | Created, Starting, Running, Stopping, kind, cwd, pool, worker, runner-live, card, worktree | generic conflict masks standing_resume_* |
| Invalid_target_matrix_preserves_pointer_generation_hold_and_queue | runner-unavailable | Conflict masks ServiceUnavailable |
| Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history | false, true | generic conflict masks standing_resume_unsupported |
| Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects | single (internal foreign/conflicting/unproven loop stops at foreign) | generic conflict masks standing_resume_not_owned |
| Invalid_or_busy_targets_refuse_before_reservation | single | Conflict masks NotFound |

Test list: all 39 selection cases; all neighbouring Standing* classes and InstructionBundleTests*; three independent method-scoped scratch selection-code mutations (incompatible, not-owned, unsupported), exact restoration and green for each. Unit lane only if production/shared test code changes, per explicit brief. Windows exact row: `/*/*/(StandingSessionSelectionTests*)/*`, expected 39/39 with no skips, at final pushed SHA; a separate Windows task owns execution.

## Checkpoint receipts

The direct checkpoint driver is required by this brief because the owner-unverified tool requires a task token (CARD-0853). Each row below has its own isolated build and exact literal filter, fresh TRX, and build slot. Temporary mutation rows are intentionally diagnostic dirty-source runs; all ordinary and restored rows bind clean exact source. No mutation was committed/pushed.

CP-1 had one rejected preflight attempt (wrong ExpectedSourceSha argument, exit 2, zero builds/tests, source_mismatch); corrected and rerun once. The refusal source.json remains under .antiphon/c950-checkpoints/CP-1-*/. The successful receipt below is preserved verbatim; reruns=1 is supplementary metadata.

CP-2-BASE is an additional diagnostic build/run, justified by CP-2's 68 neighbouring failures. It uses an owned detached checkout at the start ref and the identical filter. Individual case-name comparison confirms 68 inherited, zero introduced, exactly 27 selection failures resolved. The completed detached checkout was removed after saving evidence outside it and inventorying its owned files.

```text
CHECKPOINT CP-BASE commit=e4706adabfb74c5498da506dcce25e1e098ad1f8 build=ok filter=/*/*/(StandingSessionSelectionTests*)/* executed=39 passed=12 failed=27 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/CP-BASE-20261002-153918-2265/run.trx slot=granted waited=0s dirty=0 source=e4706adabfb74c5498da506dcce25e1e098ad1f8 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/CP-1-20261002-154519-6f77/run.trx slot=granted waited=0s dirty=0 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/Antiphon.Tests.Application/(Standing*)|(InstructionBundleTests*)/* executed=221 passed=153 failed=68 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/CP-2-20261002-155051-c6df/run.trx slot=granted waited=0s dirty=0 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 sourceState=clean buildSource=verified
CHECKPOINT CP-2-BASE commit=e4706adabfb74c5498da506dcce25e1e098ad1f8 build=ok filter=/*/Antiphon.Tests.Application/(Standing*)|(InstructionBundleTests*)/* executed=221 passed=126 failed=95 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/CP-2-BASE-20261002-155639-a5a2/run.trx slot=granted waited=135s dirty=0 source=e4706adabfb74c5498da506dcce25e1e098ad1f8 sourceState=clean buildSource=verified
CHECKPOINT SPOT-1-RED commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/Invalid_target_matrix_preserves_pointer_generation_hold_and_queue* executed=16 passed=14 failed=2 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/SPOT-1-RED-20261002-155857-c276/run.trx slot=granted waited=90s dirty=1 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16+dirty:504761dd568896b3e7e6fd7dd7c7957e050258d4a367f1685ac5ca179a52c3bc sourceState=dirty buildSource=verified
CHECKPOINT SPOT-1-GREEN commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/Invalid_target_matrix_preserves_pointer_generation_hold_and_queue* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/SPOT-1-GREEN-20261002-160425-f9d5/run.trx slot=granted waited=16s dirty=0 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 sourceState=clean buildSource=verified
CHECKPOINT SPOT-2-RED commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/SPOT-2-RED-20261002-160935-8167/run.trx slot=granted waited=60s dirty=1 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16+dirty:7e4f1c91e7ee2c78dc5b55823441b06c0c574dd43279be0006ba0c5794065085 sourceState=dirty buildSource=verified
CHECKPOINT SPOT-2-GREEN commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/SPOT-2-GREEN-20261002-161350-26e2/run.trx slot=granted waited=0s dirty=0 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 sourceState=clean buildSource=verified
CHECKPOINT SPOT-3-RED commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/SPOT-3-RED-20261002-161734-3e2e/run.trx slot=granted waited=0s dirty=1 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16+dirty:ded10bcdc1ca5c21ccb1cb974e50537b5e5c31fcfa2679f6db98cd82544fe1af sourceState=dirty buildSource=verified
CHECKPOINT SPOT-3-GREEN commit=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 build=ok filter=/*/*/(StandingSessionSelectionTests*)/Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/SPOT-3-GREEN-20261002-162145-abf5/run.trx slot=granted waited=0s dirty=0 source=b7f399a4ef59403e6c0c34f4ee61deef089a1d16 sourceState=clean buildSource=verified
```

## Mutation spot-checks and restoration

Each control changes just one production error-code literal in server/Application/Services/StandingSessionOwnership.cs to "conflict", keeping the refusal reachable. Each mutation runs separately, method-scoped, before restoring exact bytes, refreshing source timestamp, rebuilding a separate green output, and requiring a strict clean source receipt.

| Control | Method prefix (exact filter method segment) | Red executed/passed/failed | Restored executed/passed/failed |
|---|---|---|---|
| incompatible code | Invalid_target_matrix_preserves_pointer_generation_hold_and_queue* | 16/14/2 (kind, cwd assertions) | 16/16/0 |
| not-owned code | Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects* | 1/0/1 (foreign-owner assertion) | 1/1/0 |
| unsupported code | Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history* | 2/0/2 (false, true assertions) | 2/2/0 |

No skips. No fixture/build/zero-test failure is counted as a red. Original and every restored StandingSessionOwnership.cs SHA-256: 0841d9e25c8e112158c6b699f9d42d8e187818265b46218de2e31e929eac5cac. git diff --exit-code succeeds after every restoration. Scratch patches/logs remain in .antiphon/c950-pc{1,2,3}.patch and c950-spot*-*.log; receipts below carry the exact dirty-source fingerprints. Source validator accepted the 39-case CP-1 source.json at the implementation SHA.

These commissioned scratch checks do not discharge any formal PC: method-scoped SourceLanding Mutation remains pending per the verification profile.

## Neighbouring failures

InstructionBundleTests passes 61/61; all 39 selection tests also pass in the broad row. Remaining 68 failures are all inherited. CARD-0966 owns four StandingSessionOwnershipTests failures. Filed and updated CARD-0981 for the other 64 failures: StandingContinuityAttentionTests (5), StandingContinuityRecoveryTests (11), StandingRestartAccountingTests (18), StandingSessionQueueSwitchTests (8), StandingSessionRecoveryHttpTests (1), StandingSessionSwitchConcurrencyTests (21). Every class's base/fixed counts and individual inherited case names follow. CARD-0981 records both exact TRX paths and source identities.

card.ps1 new/edit saved CARD-0981, then exited 1 during Windows-path display (known CARD-0713 Join-Path/C: issue). Subsequent card.ps1 get verified creation and the updated revision; no duplicate card was created.
Baseline TRX: /work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/CP-2-BASE-20261002-155639-a5a2/run.trx
Fixed TRX: /work/worktrees/task-b5370eca/.antiphon/c950-checkpoints/CP-2-20261002-155051-c6df/run.trx
Baseline: executed=221 failed=95
Fixed: executed=221 failed=68
Inherited=68; introduced=0; resolved=27

| Class | Baseline passed/failed | Fixed passed/failed |
|---|---|---|
| Antiphon.Tests.Application.InstructionBundleTests | 61/0 | 61/0 |
| Antiphon.Tests.Application.StandingContinuityAttentionTests | 1/5 | 1/5 |
| Antiphon.Tests.Application.StandingContinuityRecoveryTests | 0/11 | 0/11 |
| Antiphon.Tests.Application.StandingPipelinePolicyDocumentationTests | 10/0 | 10/0 |
| Antiphon.Tests.Application.StandingRestartAccountingTests | 3/18 | 3/18 |
| Antiphon.Tests.Application.StandingSessionOwnershipTests | 1/4 | 1/4 |
| Antiphon.Tests.Application.StandingSessionQueueSwitchTests | 0/8 | 0/8 |
| Antiphon.Tests.Application.StandingSessionRecoveryHttpTests | 0/1 | 0/1 |
| Antiphon.Tests.Application.StandingSessionSelectionTests | 12/27 | 39/0 |
| Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests | 3/21 | 3/21 |
| Antiphon.Tests.Application.StandingSpecialistHealthPolicyTests | 5/0 | 5/0 |
| Antiphon.Tests.Application.StandingSpecialistRoutingHttpTests | 14/0 | 14/0 |
| Antiphon.Tests.Application.StandingSpecialistRoutingMigrationTests | 4/0 | 4/0 |
| Antiphon.Tests.Application.StandingSpecialistSeatTests | 12/0 | 12/0 |

Resolved cases:
Antiphon.Tests.Application.StandingSessionSelectionTests.Owned_history_uses_current_composition_and_native_resume_identity(ClaudeCode, False)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(worker)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(Created)
Antiphon.Tests.Application.StandingSessionSelectionTests.Owned_history_uses_current_composition_and_native_resume_identity(Grok, True)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(Starting)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(Running)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(Stopping)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(kind)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(cwd)
Antiphon.Tests.Application.StandingSessionSelectionTests.Owned_history_uses_current_composition_and_native_resume_identity(Grok, False)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(pool)
Antiphon.Tests.Application.StandingSessionSelectionTests.Owned_history_uses_current_composition_and_native_resume_identity(ClaudeCode, True)
Antiphon.Tests.Application.StandingSessionSelectionTests.Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history(True)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(card)
Antiphon.Tests.Application.StandingSessionSelectionTests.Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history(False)
Antiphon.Tests.Application.StandingSessionSelectionTests.Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility(False, False)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(runner-live)
Antiphon.Tests.Application.StandingSessionSelectionTests.Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects
Antiphon.Tests.Application.StandingSessionSelectionTests.Legacy_historical_owner_can_resume_after_pointer_moved(True)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_or_busy_targets_refuse_before_reservation
Antiphon.Tests.Application.StandingSessionSelectionTests.Legacy_historical_owner_can_resume_after_pointer_moved(False)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(worktree)
Antiphon.Tests.Application.StandingSessionSelectionTests.Equivalent_canonical_cwd_is_accepted_without_reusing_historical_arguments
Antiphon.Tests.Application.StandingSessionSelectionTests.Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility(True, True)
Antiphon.Tests.Application.StandingSessionSelectionTests.Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility(True, False)
Antiphon.Tests.Application.StandingSessionSelectionTests.Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(runner-unavailable)
Antiphon.Tests.Application.StandingSessionSelectionTests.Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility(False, True)

Inherited failures:
Antiphon.Tests.Application.StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(RepeatedResumeFailure)
Antiphon.Tests.Application.StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(OwnershipUnproven)
Antiphon.Tests.Application.StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(TargetMissing)
Antiphon.Tests.Application.StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(NativeSessionMissing)
Antiphon.Tests.Application.StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(TargetIncompatible)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(retry)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(first)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(missing)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(legacy-pointer-lost)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(incompatible)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(malformed)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(selection)
Antiphon.Tests.Application.StandingContinuityRecoveryTests.An_unsuccessful_explicit_retry_restores_the_same_continuity_hold
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Infrastructure_failure_with_stale_missing_text_does_not_hold_continuity
Antiphon.Tests.Application.StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(fresh)
Antiphon.Tests.Application.StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(2147483647)
Antiphon.Tests.Application.StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(1)
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_healthy_uptime_resets_the_resume_failure_counter
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_infrastructure_outcomes_grow_the_ladder_and_never_hold
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling
Antiphon.Tests.Application.StandingRestartAccountingTests.Async_infrastructure_failure_is_consumed_once_across_recreation
Antiphon.Tests.Application.StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(2)
Antiphon.Tests.Application.StandingRestartAccountingTests.C561_a_zero_threshold_restores_the_never_give_up_ladder
Antiphon.Tests.Application.StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(0)
Antiphon.Tests.Application.StandingRestartAccountingTests.Real_failures_cap_escalate_and_reset_only_after_healthy_completion
Antiphon.Tests.Application.StandingRestartAccountingTests.Early_running_infrastructure_evidence_survives_provider_rebuild_and_distinct_failed_generations
Antiphon.Tests.Application.StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge(False)
Antiphon.Tests.Application.StandingRestartAccountingTests.Sync_failure_after_restamp_is_not_charged_again_by_terminal_observation
Antiphon.Tests.Application.StandingRestartAccountingTests.Failed_outcome_save_recovers_as_unknown_without_fresh_authority
Antiphon.Tests.Application.StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge(True)
Antiphon.Tests.Application.StandingSessionOwnershipTests.Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state(500)
Antiphon.Tests.Application.StandingSessionOwnershipTests.Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state(2)
Antiphon.Tests.Application.StandingSessionOwnershipTests.Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state(0)
Antiphon.Tests.Application.StandingSessionOwnershipTests.Deleting_and_recreating_the_same_name_does_not_adopt_historical_ownership
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(False)
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Dispatched)
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Working)
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Target_late_confirmation_and_open_task_guards_remain_intact
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Any_prior_delivery_evidence_refuses_switch_and_fresh
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Only_unattempted_messages_move_atomically_and_keep_order_and_routing
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(True)
Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Blocked)
Antiphon.Tests.Application.StandingSessionRecoveryHttpTests.History_and_start_preserve_wire_contract_and_revalidate_eligibility
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(start-rpc)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(saved-running)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(selection)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(owner)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(execution)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(delivery)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(before-spawn)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(generation)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Stopped, True)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Running, True)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(pointer)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(False)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_supersedes_launch_before_spawn_and_before_typing
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(automatic)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(default)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(card)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Failed, True)
Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(fresh)

Introduced failures:

## Windows handoff and final verification

Windows has not been run from this Linux checkout. Commission the separate Windows task at the exact final SHA in the caller's final message. The complete class filter is /*/*/(StandingSessionSelectionTests*)/*, MinExecuted 39, Expect StandingSessionSelectionTests, expected 39 passed/0 failed/0 skipped. Use scripts/run-checkpoint.ps1 directly and bind -ExpectedSourceSha to that SHA:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name WIN-C950 -Project tests/Antiphon.Tests -OutputPath bin-c950-windows/ -Filter '/*/*/(StandingSessionSelectionTests*)/*' -MinExecuted 39 -Expect StandingSessionSelectionTests -ExpectedSourceSha <FINAL-SHA> -ResultsRoot .antiphon/c950-windows
```

For Windows red/green selection controls, use the same three mutations and exact method filters above, prefixed with /*/*/(StandingSessionSelectionTests*)/. Each red must fail the named code assertion; restore exact bytes and rerun that method green. Windows executable behavior remains the original Path.Combine(Environment.SystemDirectory, "cmd.exe"). No Windows-only row was skipped.

The Unit lane is intentionally omitted under the explicit brief: no production or shared test code changed in the final diff; only the class's own factory and profile seed changed. Named bundle Unit tests ran as part of CP-2. Scratch mutations are fully restored, never shipped changes.

Final SHA repetitions CP-3 (same full 39-case class filter) and CP-4 (same full Standing*/InstructionBundleTests* filter) are justified solely to bind verification to the published report-only commit. Their result counts, source qualification, and fresh TRX paths are supplied in the final message; their logs/receipts live in .antiphon/c950-final-selection.log, .antiphon/c950-final-neighbours.log and .antiphon/c950-checkpoints/CP-{3,4}-*/. Do not edit this committed report during those runs. Ordinary Review follows, then plain land by this task's landing owner after clean Final Review. Windows and formal SourceLanding Mutation remain explicit pending work.