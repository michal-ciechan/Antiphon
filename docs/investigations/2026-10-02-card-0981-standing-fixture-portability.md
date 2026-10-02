# CARD-0981: portable Standing neighbour fixtures

## Design decision

Use one test-only executable value in `AgentControlServiceIntegrationTests`:
`OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : Environment.ProcessPath!`.
The fake protocol adapters and fake runner capture launches without executing this file.
`AgentExecutableResolver.EnsureSpawnable` checks an absolute file's existence; the running
host provides that file on Linux without assuming an installed shell. Windows keeps its
original system `cmd.exe` preflight, arguments and fake launch behaviour.

Use that value in the shared control harness, its profile seed, the supervision harness
used by restart accounting, and the recovery HTTP factory. Align CARD-0950's private
selection factory and CARD-0966's ownership configuration override with the same value.
The ownership override returns to validating system `cmd.exe` on Windows; its assertions
are unchanged. Keep the private factories and overrides to avoid unrelated test refactoring.
No production code, skips, seed-helper logic or timeouts change. The two latent fixture corrections below preserve the queue and concurrency assertions while matching the current delivery contract.

## Unchanged-source red evidence

Start: `bf58e534242fc2429d5f522fef627a2c17791ca9`, clean.
Run: `.antiphon/c981-checkpoints/CP-RED-20261002-185104-85b9/run.trx`.
Exact filter: `/*/Antiphon.Tests.Application/(Standing*)|(InstructionBundleTests*)/*`.
221 executed: 157 passed, 64 failed, 0 skipped. These are exactly the card's six
neighbour-class failures. Ownership passes 5/5; selection passes 39/39; instruction
bundles pass 61/61. The red driver built successfully under a granted slot and
reported `dirty=0 sourceState=clean buildSource=verified`.

Preflight runs before history selection and runner/reservation boundaries. The missing
`cmd.exe` therefore appears as a direct ConflictException, a generic `conflict` instead
of the asserted refusal code, an HTTP body mismatch, an exception-type mismatch, or a
boundary timeout. The first fixed-source exact filter resolves 62/64 failures with only
executable configuration changes (219 passed, 2 failed, 0 skipped). The two remaining failures are investigated below; the final rerun must prove all listed cases pass.

## Requirement trace

C1: control harness executable preflight. Fix: shared `FixtureExecutable` in BuildHarness.
C2: supervision harness executable preflight. Fix: use the same value in BuildHarness.
C3: recovery HTTP factory preflight. Fix: use the same value in in-memory configuration.
Each row's proof is the same named test case in CP-1's fresh TRX; arguments are preserved.

| Class | Red failures / cases | Cause and fix | Proof |
|---|---:|---|---|
| StandingContinuityAttentionTests | 5 / 6 | C1 | CP-1: all 6 |
| StandingContinuityRecoveryTests | 11 / 11 | C1 | CP-1: all 11 |
| StandingRestartAccountingTests | 18 / 21 | C2 | CP-1: all 21 |
| StandingSessionQueueSwitchTests | 8 / 8 | C1, C4 | CP-1: all 8 |
| StandingSessionRecoveryHttpTests | 1 / 1 | C3 | CP-1: full HTTP flow |
| StandingSessionSwitchConcurrencyTests | 21 / 24 | C1, C5 | CP-1: all 24 |

| Failing case at start | Observed signature | Cause / fix | Proving case |
|---|---|---|---|
| StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(NativeSessionMissing) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(NativeSessionMissing) |
| StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(OwnershipUnproven) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(OwnershipUnproven) |
| StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(RepeatedResumeFailure) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(RepeatedResumeFailure) |
| StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(TargetIncompatible) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(TargetIncompatible) |
| StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(TargetMissing) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(TargetMissing) |
| StandingContinuityRecoveryTests.An_unsuccessful_explicit_retry_restores_the_same_continuity_hold | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.An_unsuccessful_explicit_retry_restores_the_same_continuity_hold |
| StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(first) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(first) |
| StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(incompatible) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(incompatible) |
| StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(legacy-pointer-lost) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(legacy-pointer-lost) |
| StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(malformed) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(malformed) |
| StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(missing) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(missing) |
| StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(fresh) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(fresh) |
| StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(retry) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(retry) |
| StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(selection) | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(selection) |
| StandingContinuityRecoveryTests.Infrastructure_failure_with_stale_missing_text_does_not_hold_continuity | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.Infrastructure_failure_with_stale_missing_text_does_not_hold_continuity |
| StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create | cmd.exe missing in executable preflight | C1 | CP-1: StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create |
| StandingRestartAccountingTests.Async_infrastructure_failure_is_consumed_once_across_recreation | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Async_infrastructure_failure_is_consumed_once_across_recreation |
| StandingRestartAccountingTests.C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again |
| StandingRestartAccountingTests.C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold |
| StandingRestartAccountingTests.C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter |
| StandingRestartAccountingTests.C561_a_zero_threshold_restores_the_never_give_up_ladder | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_a_zero_threshold_restores_the_never_give_up_ladder |
| StandingRestartAccountingTests.C561_healthy_uptime_resets_the_resume_failure_counter | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_healthy_uptime_resets_the_resume_failure_counter |
| StandingRestartAccountingTests.C561_infrastructure_outcomes_grow_the_ladder_and_never_hold | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_infrastructure_outcomes_grow_the_ladder_and_never_hold |
| StandingRestartAccountingTests.C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling |
| StandingRestartAccountingTests.Early_running_infrastructure_evidence_survives_provider_rebuild_and_distinct_failed_generations | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Early_running_infrastructure_evidence_survives_provider_rebuild_and_distinct_failed_generations |
| StandingRestartAccountingTests.Failed_outcome_save_recovers_as_unknown_without_fresh_authority | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Failed_outcome_save_recovers_as_unknown_without_fresh_authority |
| StandingRestartAccountingTests.Real_failures_cap_escalate_and_reset_only_after_healthy_completion | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Real_failures_cap_escalate_and_reset_only_after_healthy_completion |
| StandingRestartAccountingTests.Sync_failure_after_restamp_is_not_charged_again_by_terminal_observation | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Sync_failure_after_restamp_is_not_charged_again_by_terminal_observation |
| StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge(False) | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge(False) |
| StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge(True) | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Timeout_retries_but_requested_cancellation_does_not_charge(True) |
| StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(0) | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(0) |
| StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(1) | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(1) |
| StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(2) | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(2) |
| StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(2147483647) | cmd.exe missing in executable preflight | C2 | CP-1: StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(2147483647) |
| StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(False) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(False) |
| StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(True) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(True) |
| StandingSessionQueueSwitchTests.Any_prior_delivery_evidence_refuses_switch_and_fresh | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingSessionQueueSwitchTests.Any_prior_delivery_evidence_refuses_switch_and_fresh |
| StandingSessionQueueSwitchTests.Only_unattempted_messages_move_atomically_and_keep_order_and_routing | cmd.exe missing in executable preflight | C1, C4 | CP-1: StandingSessionQueueSwitchTests.Only_unattempted_messages_move_atomically_and_keep_order_and_routing |
| StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Blocked) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Blocked) |
| StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Dispatched) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Dispatched) |
| StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Working) | Generic executable conflict masks asserted refusal code | C1 | CP-1: StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Working) |
| StandingSessionQueueSwitchTests.Target_late_confirmation_and_open_task_guards_remain_intact | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionQueueSwitchTests.Target_late_confirmation_and_open_task_guards_remain_intact |
| StandingSessionRecoveryHttpTests.History_and_start_preserve_wire_contract_and_revalidate_eligibility | HTTP generic executable conflict masks target-active code | C3 | CP-1: StandingSessionRecoveryHttpTests.History_and_start_preserve_wire_contract_and_revalidate_eligibility |
| StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(automatic) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(automatic) |
| StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(default) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(default) |
| StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(fresh) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(fresh) |
| StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(selection) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(selection) |
| StandingSessionSwitchConcurrencyTests.Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation |
| StandingSessionSwitchConcurrencyTests.Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision | Executable conflict masks injected reservation IOException | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision |
| StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Failed, True) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Failed, True) |
| StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Running, True) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Running, True) |
| StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Stopped, True) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Stopped, True) |
| StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(False) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(False) |
| StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True) | Executable preflight refusal prevents boundary entry | C1, C5 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True) |
| StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(card) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(card) |
| StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(delivery) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(delivery) |
| StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(execution) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(execution) |
| StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(generation) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(generation) |
| StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(owner) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(owner) |
| StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(pointer) | Executable preflight refusal prevents boundary entry | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(pointer) |
| StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(before-spawn) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(before-spawn) |
| StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(saved-running) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(saved-running) |
| StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(start-rpc) | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(start-rpc) |
| StandingSessionSwitchConcurrencyTests.Stop_supersedes_launch_before_spawn_and_before_typing | cmd.exe missing in executable preflight | C1 | CP-1: StandingSessionSwitchConcurrencyTests.Stop_supersedes_launch_before_spawn_and_before_typing |

## Separately investigated failures exposed by executable resolution

CP-1 at `92b6f1f19dd285469649b674fae7df576cd6f7e4` executes 221: 219 passed,
2 failed, 0 skipped. Its TRX is `.antiphon/c981-checkpoints/CP-1-20261002-190023-2e8f/run.trx`.
These are test-contract mismatches, not Linux executable failures or production defects.

C4: `Only_unattempted_messages_move_atomically_and_keep_order_and_routing` expects raw
`Safe`, but actual delivery is `[antiphon-channel:<queue-row-id:N>] Safe`.
`ChannelPromptCorrelation.PrepareFirstAttempt` intentionally wraps an untouched Channel
row. The standing-continuity owner documents this exact durable wire identity.
Change the expected ordered body list and full post-baseline UserPrompt equality to
include the exact row ID for the Channel message. UI, Delegation and Scheduled bodies
remain byte-exact. Add persisted Body and Origin assertions for every delivered row.
This strengthens identity proof and keeps every order, routing, attempt and receipt check;
it does not strip markers, accept substrings or trust Sent as delivery evidence.

C5: `Reservation_and_real_queue_flush_serialize_in_both_orders(True)` times out waiting
for its SavedChanges interceptor. `StandingRecoveryFixture.SeedAsync` seeds both sessions
Stopped, while `SessionMessageQueueService.DeliverNextLockedAsync` refuses any status other
than Running before claiming delivery. Registering a fake adapter does not alter DB status.
Seed the source Running in the delivery-first case. Once the interceptor confirms the
durable claim and holds the actual singleton queue lock, record its Stopped status before
starting history selection. This models stop observation after an in-flight claim and
preserves the requirement to settle attempted input before selecting history. Keep the
same 15-second entry bound, 150-ms contender-block assertion, exact refusal code,
source/target ownership and attempt-floor assertions. The selection-first case is unchanged.

Rerun CP-1 after this second committed test-only slice; retain the first receipt as
`reruns=1` evidence. CP-2 and CP-3 remain the same closed-list rows.
## Verification design

CP-RED is the explicitly requested unchanged-start diagnostic, completed before this
note or the implementation was authored. CP-2 covers both full harness-owner classes
because the shared executable and the existing profile seed changed. CP-3 is the required
single full Unit lane. Run these rows sequentially with `scripts/run-checkpoint.ps1`
(self-leasing), passing the exact committed SHA through `-ExpectedSourceSha`.
The brief explicitly requires the script directly because the checkpoint tool refuses
owner-unverified tasks without a task token. All runs retain literal filter pipes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-RED | unchanged start | `tests/Antiphon.Tests -> bin-c981-red/` | unchanged-red | `/*/Antiphon.Tests.Application/(Standing*)\|(InstructionBundleTests*)/*` | red baseline, all trace rows | 221 executed; 64 known failures | 221 | 8 |
| CP-1 | all | `tests/Antiphon.Tests -> bin-c981-final/` | standing-final | `/*/Antiphon.Tests.Application/(Standing*)\|(InstructionBundleTests*)/*` | all trace rows, selection and ownership regression | 221 executed, 0 failed/skipped | 221 | 5 |
| CP-2 | all | CP-1 | harness-owners | `/*/Antiphon.Tests.Application/(AgentControlServiceIntegrationTests*)\|(AgentSupervisionTests*)/*` | shared harnesses, profile executable | all listed, 0 failed/skipped | 40 | 3 |
| CP-3 | all | CP-1 | unit | `/*/*/*/*[Category=Unit]` | full Unit lane | >= 2000 executed, 0 failed; report every skip | 2000 | 5 |

### Scratch controls (not SourceLanding PCs)

Two method-scoped diagnostics are required by this Code brief; they do not discharge
post-land PCs. First run each exact method green from the committed build; then change
only its production guard's error code in a scratch edit, run the same exact method red,
restore the original file bytes, rebuild into a fresh output, and run the same method green.
Do not commit mutations. Omit strict ExpectedSourceSha only during the stable dirty red
phases; retain their source fingerprints and check `git diff --exit-code` after restoration.

| Control | Exact test filter | Scratch mutation | Expected red |
|---|---|---|---|
| SM-1 | `/*/*/StandingSessionRecoveryHttpTests/History_and_start_preserve_wire_contract_and_revalidate_eligibility*` | `StandingSessionOwnership.Refusal`: active-target code becomes `conflict` | HTTP body lacks `standing_resume_target_active` |
| SM-2 | `/*/*/StandingSessionSwitchConcurrencyTests/Reservation_rechecks_changes_committed_after_runner_preflight*` | `AgentControlService.StartInteractiveSessionAsync`: changed-generation code becomes `standing_resume_current_changed` | generation argument fails exact refusal code; other 5 arguments pass |

## Windows and post-land obligations

Do not run Windows as part of this Linux Code task. A separate Windows task must rerun
CP-1 and CP-2 at the final pushed SHA, including **StandingSessionOwnershipTests** and
StandingSessionSelectionTests. Windows must continue passing system `cmd.exe` preflight.
Ordinary Final Review follows this Code task; a plain land by the Code owner follows a
clean Final Review. All SourceLanding Mutation PCs remain pending.
