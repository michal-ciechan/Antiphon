# CARD-0510 S2 capacity-grant repair

Code owner 52db3427; base fa15261bbb8fae3f00ed24391fe2ea1d72d0819b.
Third repair after Final Review 4134521e. Master-only. No Unit lane. No PCs bound.

Invariant: a standing agent that does not reserve a fresh start, after ModelDisabledException with capacity recovery on, starts on the next tick once the hold has cleared and the sole grant has arrived. The crash ladder is not consulted for that grant. Infrastructure failures on the ordinary restart path still record a bounded future retry. A Herdr or standing-continuity held-code conflict on the capacity path does not charge the ladder.

The second repair's model-disabled witness no longer expects NextRestartAt = now + 80s. It expects the prior due instant (now - 1s). Infrastructure failures still expect now + 160s.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-S2-RG | Repair | `tests/Antiphon.Tests -> bin-c510-s2-grant/` | grant-green | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingContinuityRecoveryTests*)\|(StandingRestartAccountingTests*)\|(CapacityRecoverySupervisionTests*)/(C510_S2_*)\|(C510_counts_only_distinct_admitted_failed_resumes*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)\|(Missing_native_target_holds_after_one_resume_without_create*)\|(C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling*)\|(C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again*)\|(C561_healthy_uptime_resets_the_resume_failure_counter*)\|(Card0412_*)` | D-1 grant regression; prior 34-case S2 selection; CapacityRecoverySupervisionTests | all 41 listed cases, zero failed/skipped | 41 | 15 |

Run the committed row with --row-timeout 40m --expected-source-sha <HEAD>. One red witness, `C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick`, was committed and shown failing before this fix. Delete leftover `bin-c510-grant-red/` before the row. Retry once on a receipt refusal. No repetition after green.

PC-1..PC-89 and variants remain pending SourceLanding Mutation. Restart: none. Next: Review.
