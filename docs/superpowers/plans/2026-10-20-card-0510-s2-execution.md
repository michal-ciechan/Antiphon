# CARD-0510 S2 execution selection

Code task 85240dad, base `2d49f44111f533d9d242ae529304ae6bd95e5ea0`.
The explicit S2 brief overrides the generic Final profile with one red witness and
one combined narrow green run, no Unit lane, no full classes and no PC execution.
The accepted design remains
[the parent plan](2026-10-20-card-0510-standing-restart-escalation-plan.md).

CP-S2-W is the sole pre-fix red witness. CP-S2-G merges parent CP-5/6/7/8/9/10:
V-3/V-4 share the real three-admitted-resume and failed-Fresh episode witness;
V-5 uses focused guards and existing concurrency regressions; R-2/R-3 retain
their existing methods. The original exhaustive 76-result S2 roster is replaced
by this bounded selection under the brief. S1 CP-1..4 are not repeated in full:
the changed accounting witness and three legacy hold/reset methods are included in green.
Their old-ID assertions now explicitly exercise the disabled fresh policy.
S3
CP-11..15 / V-6..8 / R-4 remain for later dispatch. No new test class is added.
All PC-1..PC-89 and their variants remain pending; no payload is bound.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-S2-W | Witness | `tests/Antiphon.Tests -> bin-c510-s2-red/` | red-witness | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_S2_three_failed_resumes_reserve_fresh_only_once*` | V-3, V-4 | one failure before fix at different-session assertion | 1 | 5 |
| CP-S2-G | S2 | `tests/Antiphon.Tests -> bin-c510-s2-green/` | bounded-green | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingContinuityRecoveryTests*)\|(StandingRestartAccountingTests*)/(C510_S2_*)\|(C510_counts_only_distinct_admitted_failed_resumes*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)\|(Missing_native_target_holds_after_one_resume_without_create*)\|(C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling*)\|(C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again*)\|(C561_healthy_uptime_resets_the_resume_failure_counter*)` | V-3, V-4, V-5, R-2, R-3, changed V-1/V-2 and R-5 cases | all 31 listed cases, zero failed/skipped | 31 | 6 |

Use checkpoint tool `run --plan <this file> --rows CP-S2-W` then, after the
implementation commit, `--rows CP-S2-G`; both use `--serial --row-timeout 40m
--expected-source-sha <committed HEAD>`. Wait to terminal and inspect fresh TRX.
Estimated total execution cost: two isolated builds plus narrow cases, 11 minutes.

Green roster: core three-resume/one-Fresh witness (1); admission guards (17:
repeated-hold, legacy-hold, native-hold, herdr-hold, suspended, latched, disabled,
consumed, stale-generation, stale-pointer, live, open-task, attempted, keyed,
manual, not-standing, specialist); concurrent fresh reservations (1); capacity
grant/retarget (2: ClaudeCode, Grok); changed accounting/health witness (1);
existing concurrent-start variants (4), commit/enqueue race (1), native-missing
hold (1), and the three changed legacy hold/reset methods (3).

S3 remains a separate dispatch: dedicated Warning/Error incident kinds, routed
alert episode deduplication, refusal hold/schedule handling, delivery receipts
and continuity-owner documentation. S2 keeps existing refusal behavior and
existing acceptance incidents, and adds no public bypass or replay authority.
No live/manual deployment acceptance is authorized; restart: none.
