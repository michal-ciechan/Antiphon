# CARD-1851 mutation candidate (unbound)

Plan: `docs/superpowers/plans/2026-11-12-card-1851-script-closures-plan.md`.
Original Code / landing owner: `2eeb94f5-6f28-4c2c-8b5a-f53a505460f7`.
Binding C/O/L/R and external evidence root are the caller's SourceLanding commissioning work.
All controls below are **pending**. Code ran ordinary regression witnesses only.

Project: `tests/Antiphon.SessionRunner.Tests`. Each deliberate mutant requires its own
exact-method compiling-defect/intended-red/restore/fresh-build/green cycle at published L.
Use the native worktree outputs through the build-slot gate; preserve assertion/count/TRX
and restoration evidence externally. Never widen a timeout or loosen an assertion.

| PC / variant | Deliberate defect | Exact method filter | Required detection |
|---|---|---|---|
| PC-1 / swallowed-read | Restore the old catch-all PID reader | `/*/*/RunnerRestartScriptCompatibilityTests/Pid_read_errors_stop_restart_before_any_control` | directory, exclusive lock, invalid, overflow, zero, unreadable wrapper and missing-parent cases report action-failed with zero controls; missing/live remain valid |
| PC-2 / registration-http-name | Restore by-name HTTP lookup in GetRegistration | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | both London date arguments detect the scope failure |
| PC-2 / schedule-http-name | Restore by-name HTTP lookup in GetSchedule | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | same method, schedule boundary |
| PC-2 / jobs-http-name | Restore by-name jobs/list HTTP lookup in GetJobs | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | same method, jobs list boundary |
| PC-2 / result-http-name | Restore by-name completed-result HTTP lookup in GetJobs | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | swallowed result lookup must fail expected successful job/identity assertions |
| PC-3 / conversion-name | Restore by-name job conversion in GetJobs | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | mapper lookup fails; real job assertions cannot pass |
| PC-3 / due-name | Restore by-name due-time lookup in GetJobs | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | caught lookup failure leaves no matching expected summer/winter UTC |
| PC-3 / timezone-name | Restore by-name timezone helper lookup in GetJobs | `/*/*/RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator` | caught transitive lookup failure leaves no matching expected UTC |

The tests assert setup isolation (helpers absent globally), real production factory results,
HTTP shape, fetch cap and failure handling. Missing controls and additional surviving defects
belong to Mutation discovery, including when a candidate proves noncompiling/equivalent.
No evidence from a different source revision can bind this pack. Restart: none.

CARD-1860 follow-up: this table records the historical CARD-1851 source contract.
Missing-parent is now intentionally permissive and is covered by the separate
missing-logs method under both entry forms; it must not be counted among the
current swallowed-read failure arguments. The current restart candidate is
`docs/superpowers/mutations/2026-11-12-card-1860-restart-and-driver.md`, including
Hard/WaitOnly refusal variants. Historical outcomes and all unexecuted controls
remain pending; the nightly closure controls are unchanged by CARD-1860.
