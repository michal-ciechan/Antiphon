# CARD-0510 S2 transient-refusal repair

Code/landing owner 129c2eba; base 8408f6db8fb2c39b14b5a48aaa88666518cbd603.
Repair substantive Final Review 1b31c8c6. Explicit brief narrows this Final round to two red witnesses and one 34-case combined green run; no Unit or whole classes.

Invariant: pre-commit inventory failure grows the infrastructure retry ladder; ModelDisabledException registers the capacity wait. Both schedule a bounded future retry even under a repeated-resume hold. Durable attempted-input refusal still holds, clears the schedule and preserves the unused allowance.
Two witnesses each cover ordinary/capacity handoffs and existing/no repeated-resume hold. Persisted state, hold linkage, exact backoff, immediate-tick suppression and ordinary infrastructure recovery are checked. Existing partial class; no Slow registry change.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-S2-TW | Witness | `tests/Antiphon.Tests -> bin-c510-transient-red/` | red-witnesses | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/(C510_S2_fresh_inventory_failure_keeps_bounded_retry*)\|(C510_S2_fresh_model_disabled_keeps_capacity_wait_and_bounded_retry*)` | transient hold regression | two assertion failures before fix | 2 | 5 |
| CP-S2-RG | Repair | `tests/Antiphon.Tests -> bin-c510-transient-green/` | bounded-green | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingContinuityRecoveryTests*)\|(StandingRestartAccountingTests*)/(C510_S2_*)\|(C510_counts_only_distinct_admitted_failed_resumes*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)\|(Missing_native_target_holds_after_one_resume_without_create*)\|(C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling*)\|(C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again*)\|(C561_healthy_uptime_resets_the_resume_failure_counter*)` | refusal-loop regression; V-3, V-4, V-5, R-2, R-3; changed V-1/V-2 and R-5 cases | all 34 listed cases, zero failed/skipped | 34 | 6 |

Run committed groups with --serial --row-timeout 40m --expected-source-sha <HEAD>; wait to terminal and inspect fresh TRX. One gated checkpoint-tool bootstrap is required. Expected 11 minutes plus authoring/slot waits; budget 45 minutes. Remove owned leftover output and retry once on receipt refusal. No repetition after green.

V-1/V-2 and R-5: changed S2 cases only. V-3/V-4/V-5, R-2/R-3: bounded S2 selection carried forward. R-1/R-6 remain in prior S1 evidence; V-6/V-7/V-8 and R-4 remain deferred to S3 except the targeted refusal repair. Parent plan: 2026-10-20-card-0510-standing-restart-escalation-plan.md. All PC-1..PC-89 and variants remain pending SourceLanding Mutation; no PCs bound here. Restart: none. Master-only. Next: Review.
