# CARD-0510 S3 execution selection

Code task 323e503d, base `ebd5632a0fd3e3291f83ba867acda8035fbf7453`.
The explicit S3 brief overrides the generic Final profile and the 147-execution
TestDesign manifest. One red witness per behaviour, committed before the fix,
then one narrow green run. No Unit lane, no whole-class run, no bound PCs.
Parent design: [2026-10-20-card-0510-standing-restart-escalation-plan.md](2026-10-20-card-0510-standing-restart-escalation-plan.md).

Behaviours: an Error alert once per refused-fresh hold, one Warning audit per
successful fresh episode, and untouched queue identity transferred with that
reservation. Fullness-triggered replacement stays deferred. The capacity-path
Herdr-conflict catch already returns without charging the ladder; a new test of
it would pass on this base, and Mutation is paused, so that witness is skipped.

CP-S3-W is the pre-fix red row (three methods). CP-S3-G merges those methods
with the landed CP-S2-RG 41-case selection. The later-episode repair adds
`C510_S3_later_episode_raises_one_warning_per_reservation`, so the green floor
is 45. Parent CP-11 through CP-15 (the 11+4+7+8+4 parameterized roster) are skipped.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-S3-W | Witness | `tests/Antiphon.Tests -> bin-c510-s3-red/` | red-witness | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_S3_*` | refused-hold alert; episode audit; queue transfer | three failures before the fix | 3 | 8 |
| CP-S3-G | S3 | `tests/Antiphon.Tests -> bin-c510-s3-green/` | bounded-green | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingContinuityRecoveryTests*)\|(StandingRestartAccountingTests*)\|(CapacityRecoverySupervisionTests*)/(C510_S2_*)\|(C510_S3_*)\|(C510_counts_only_distinct_admitted_failed_resumes*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)\|(Missing_native_target_holds_after_one_resume_without_create*)\|(C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling*)\|(C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again*)\|(C561_healthy_uptime_resets_the_resume_failure_counter*)\|(Card0412_*)` | S3 witnesses plus CP-S2-RG | later-episode warning plus the prior 44 cases, zero failed/skipped | 45 | 20 |

Run each committed group with `--serial --row-timeout 40m --expected-source-sha <HEAD>`.
Wait to a terminal exit and inspect the fresh TRX. Delete an owned leftover output
and retry once only on a receipt refusal. No repetition after green.
PC-1..PC-89 and variants remain pending SourceLanding Mutation. Restart: none.
