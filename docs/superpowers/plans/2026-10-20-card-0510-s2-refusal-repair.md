# CARD-0510 S2 refused-fresh repair

Code/landing owner `1480cec4`; base `9469b90673af815236a5209825ce76dc4d1db86c`.
Repair the restart loop found by Final Review `f8316245`.
The explicit repair brief limits ordinary verification to one red witness and one
combined green run: no Unit lane, whole classes, or deliberate mutants.
The [S2 selection](2026-10-20-card-0510-s2-execution.md) supplies the other 31 cases;
the [parent plan](2026-10-20-card-0510-standing-restart-escalation-plan.md) owns the design.

Invariant: a pre-commit automatic Fresh refusal persists a continuity hold with no
due schedule, preserves the unused allowance and attempted pending input, and
prevents subsequent supervisor Start calls. One witness exercises ordinary and
capacity-granted handoffs, both with and without an existing repeated-resume hold.
Each scenario counts real runner inventory preflights to distinguish refused Start
calls from the supervisor's ordinary probe, then reads persisted state independently.
No new test class or Slow registry row is needed.

The shared restart helper rechecks the persisted generation and eligibility under
the agent lock after refusal. `AutomaticFreshRefused` is a continuity reason that
cannot bypass the hold; it does not consume the allowance or add an alert kind.
An existing hold timestamp is retained. A committed reservation or concurrent
accepted human start is not overwritten by refusal bookkeeping.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-S2-RW | Witness | `tests/Antiphon.Tests -> bin-c510-refusal-red/` | red-witness | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_S2_refused_fresh_stays_held_across_later_ticks*` | refusal-loop regression | one assertion failure before fix | 1 | 5 |
| CP-S2-RG | Repair | `tests/Antiphon.Tests -> bin-c510-refusal-green/` | bounded-green | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingContinuityRecoveryTests*)\|(StandingRestartAccountingTests*)/(C510_S2_*)\|(C510_counts_only_distinct_admitted_failed_resumes*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)\|(Missing_native_target_holds_after_one_resume_without_create*)\|(C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling*)\|(C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again*)\|(C561_healthy_uptime_resets_the_resume_failure_counter*)` | refusal-loop regression; V-3, V-4, V-5, R-2, R-3; changed V-1/V-2 and R-5 cases | all 32 listed cases, zero failed/skipped | 32 | 6 |

Run each committed group through the checkpoint tool with `--serial --row-timeout 40m
--expected-source-sha <committed HEAD>`, wait to terminal, and inspect fresh TRX.
One gated tool bootstrap is needed before those two isolated test builds.
Expected cost: 11 minutes plus bootstrap/slot waits; task budget 45 minutes.
No repeats after green. A receipt refusal permits removal of owned output and one retry.

V-1/V-2 and R-5 are only the changed S2 cases; R-1/R-6 stay in prior S1 evidence.
S3 V-6/V-7/V-8 and R-4 remain deferred except this refusal hold/schedule repair.
Dedicated alert kinds, receipt transfer, and continuity-owner documentation remain S3.
Every parent PC-1..PC-89 and variant remains pending for SourceLanding Mutation;
this repair binds no PCs. Restart: none. Master-only; next stage: Review.
