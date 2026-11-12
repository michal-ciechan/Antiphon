# CARD-1860 unbound Mutation candidate

Code / landing owner: `042b61c0-ab0d-49e7-a298-93ce0cf8792b`.
Plan: `docs/superpowers/plans/2026-11-12-card-1860-restart-and-driver-plan.md`.
Binding: pending caller-commissioned SourceLanding Mutation at the actual published SHA.
Operator waived deliberate mutants in this Code task (2026-11-12). No PC is passed.

| ID / variant | Deliberate defect | Exact method filter | Expected red |
|---|---|---|---|
| PC-1 / missing-directory-catch | Remove only the DirectoryNotFoundException catch in the production PID reader | `/*/*/RunnerRestartScriptCompatibilityTests/Missing_logs_is_an_absent_pid_without_creating_the_directory` | Both native available entry-form arguments reject the absent logs path |
| PC-2 / swallowed-unreadable | Add catch-all PID read suppression | `/*/*/RunnerRestartScriptCompatibilityTests/Pid_read_errors_stop_restart_before_any_control` | Directory/locked/wrapper errors and Hard/WaitOnly variants no longer fail before controls |
| PC-2 / invalid-as-absent | Return null for malformed/overflowing/non-positive PID content | `/*/*/RunnerRestartScriptCompatibilityTests/Pid_read_errors_stop_restart_before_any_control` | Invalid/overflow/zero and Hard/WaitOnly garbage variants reject the permissive result |
| PC-3 / omitted-driver-prerequisite | Omit the declared driver prerequisite in an owned fresh checkout with no driver output | `/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged` | Missing Antiphon.DerivedImpactIndex.dll; not a build/fixture failure |

All four variants are **pending**, including missing-control discovery. Mutation owns
method-scoped red/restore/fresh-build/green if commissioned. Preserve each assertion
failure and source binding, restore every deliberate defect, and record fresh green
for the same method. Never interpret Linux skips as native Windows proof. The ordinary
red/green tests are regression evidence and do not discharge these controls.
