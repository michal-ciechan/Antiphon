# CARD-1265 unbound Mutation candidate

All controls and variants are pending; Code ran no deliberate mutations.
Ordinary Review and caller landing of original Code task
`c8d5341a-f946-4559-89d5-f1071792543c` precede SourceLanding Mutation.

Plan: `docs/superpowers/plans/2026-10-14-card-1265-windows-skip-roster.md`.
Project: `tests/Antiphon.Tests`. Restart: none.

Bind these candidates to landed source. Mutation owns deliberate mutants,
red/restore/green evidence, and missing-control discovery. Every cycle uses only
`/*/*/WindowsUnitPlatformAdmissionTests/ExactMethod` through the host build gate.
These controls touch overlapping files; restore between cycles. Build errors,
fixture errors, and zero tests are not red proof. Never widen a timeout or loosen
an assertion.

| PC / variants | Deliberate defect | ExactMethod | Expected red | Status |
|---|---|---|---|---|
| PC-1 / CRLF, upload failure, verify failure | Remove each added token-refresh roster entry separately | C1210_Roster_closure_matches_source_gates | c1210-unrostered-gate names the omitted method | All three pending |
| PC-2 | Change only RefreshGithubTokenRemoteCommandTests.NativeLinuxReason | C1210_Guard_matches_native_platform | c1210-exact-reason | Pending |
| PC-3 / CRLF, upload failure, verify failure | Move each token-refresh gate below fixture construction separately | C1210_Native_linux_entries_guard_before_work | c1210-first-statement names the method | All three pending |
| PC-4 | Remove RefreshGithubTokenRemoteCommandTests from ChildFilter | C1210_Audit_rejects_empty_or_incomplete_outcomes | c1210-child-storage-selection | Pending |

All proposed detecting methods are portable. The separate Windows child audit
still requires native Windows placement and is not established by Linux green.
