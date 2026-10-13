# CARD-1205 S4 — unbound Mutation candidate

Status: pending; no SourceLanding Mutation has run. Code/landing owner:
0ced9d05-9c38-478e-81ba-3dc9c9375223, branch `feat/card-task-0ced9d05`.
Original S4 Code task: 902bc6d6-3b26-465f-bdef-ff32f5920dd5.
Continuation base: ab32d51ad1a71c87e3098993bfe2f07365c04f01; predecessor
7b7d644a-a776-42e8-bc12-3d57e5df5ace. S1-S3 are inherited; S5 is outside this slice.

All 55 S4 IDs, PC-41..83, PC-101..110 and PC-191..192, and every variant below remain
pending. Ordinary CP-4 green and the missing-feature baseline reds discharge
none. Payloads, guard IDs and intended assertion reds are owned by the frozen
[test design](../plans/2026-10-12-card-1205-test-design.md#positive-controls).
Mutation owns compiling adaptation, deliberate mutants, method-scoped
red/restore/green and missing-control discovery. Independent guards must not be
credited with a red that only demonstrates absence of the entire watchdog.

| Pending PCs | Method-scoped selection | Pending variants |
|---|---|---|
| PC-41, PC-101, PC-102, PC-110 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Interval_boundary_controls_probe_and_log` | 120/180-second settings; exact due comparison, due/result metadata and configured interval |
| PC-42 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Wall_clock_jumps_do_not_change_due_time` | UTC +6h / -6h independently of monotonic time |
| PC-43 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Healthy_keepalives_do_not_reset_probe_debt` | keepalive, cache read, queue attempt, unrelated transcript; bind any independent freshness-reset branches |
| PC-44, PC-45, PC-105 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Fair_pages_and_single_flight_bound_work` | one discovery page, retained/wrapping cursor, fair failed-probe retry |
| PC-46..48, PC-104 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Concurrency_and_generation_are_bounded` | worker cap, generation single flight, old-generation ownership until join, deferred warning |
| PC-49..51 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Capacity_overflow_remains_visible` | stable 1025-session overflow and queued-generation churn; directory/pending ownership and overflow visibility |
| PC-52, PC-53, PC-103 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Failed_probe_retains_debt_and_retries` | Unknown debt, poll spacing, warning, later checked-idle wakeup |
| PC-54..56 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Overdue_operation_is_logged_and_joined` | independent discovery while save ignores cancellation, overdue warning, shutdown join |
| PC-57, PC-58 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Hosted_restart_discovers_working_sessions_immediately` | real Program registration and immediate discovery in both cold host epochs |
| PC-59..61 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Disabled_setting_logs_the_missing_bound` | feature disabled / transport disabled and explicit warning |
| PC-62..71 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Invalid_settings_are_rejected` | all 12 frozen invalid scalar rows; independent lower/upper bound mutations |
| PC-72 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Valid_settings_limits_are_accepted` | actual startup and configured values at valid minimum / maximum |
| PC-73, PC-74, PC-106..108 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Discovery_requires_positive_local_working_evidence` | no note, remote, missing, stopped, idle, cold cache, generation change |
| PC-75, PC-76 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Discovery_failure_retains_visible_debt` | directory/debt retention and discovery warning |
| PC-77 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Probe_budget_covers_headers_body_and_retries` | headers, body, admitted retry backoff; exact ten-second logical-read cancellation |
| PC-78, PC-79 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Recovered_idle_schedules_only_coalesced_work` | actual coalesced flush-worker submission, commit-gate availability and independent scheduling |
| PC-80, PC-81 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Ownership_cleanup_is_bounded_and_conservative` | confirmed terminal / uncertain ownership, one reconciliation page |
| PC-82, PC-83, PC-109 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Recovery_does_not_manufacture_idle_or_release` | Working, Unknown, checked idle; no synthetic end, kill, direct input or release |

PC-77 binding was strengthened in e9eca807a8c44169f8a2dd0264e97ff3fcf5c36c.
The fixture admits its designed 30-second Retry-After delay, requires the actual
retry log and live delay timer, and observes the logical caller token outside
the retry pipeline. All three arms assert cancellation at exactly ten fake-clock
seconds. Teardown releases controlled I/O and advances the unchanged inner
budget so a failing guard does not strand an owned probe. No timeout or assertion
was loosened. CP-4 executed 48/48 green in run 20261013-081416-2abe; this is
ordinary evidence, not a PC cycle.

Mutation must additionally assess independent implementation branches, including
generation replacement, wakeup failure/recovery sweep, cancellation/shutdown
ownership and fallback ownership reads. Any missing control remains its finding;
no unexecuted additional variant is claimed passed here.

Caller commissions Review, lands repair Code/landing owner 0ced9d05 after a clean Review,
then commissions method-scoped SourceLanding Mutation. Run only local inherited
children in the landed snapshot; retain red/restore/green evidence externally.
No timeout or assertion relaxation. Restart: none.

F1/F2 repair controls (all pending, no deliberate mutant executed by Code):

| Pending PCs | Method-scoped selection | Pending variants / intended red |
|---|---|---|
| PC-191 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Reconciliation_preserves_owned_recovery_wakeup` | Restore directory-membership wake guard: post-commit removal loses the one wake. Duplicate TryEnqueue: exact Accepted list must fail; held adapter submission remains one. Four interleavings: before-commit, after-commit, after-return, two admission attempts. |
| PC-192 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Hosted_backlog_bound_includes_poll_refill` | Ignore configured host poll or continuously refill after probe completion: measured admission counts/timestamps must fail. This is a scheduler witness, not proof of arbitrary DB/commit latency. |