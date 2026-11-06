# Unbound mutation candidate: C0824 role-gate Git admission

Code/landing owner: be08aac7. Plan:
docs/superpowers/plans/2026-11-06-c0824-role-git-limiter.md.

PC-1/remove-role-gate-attribute remains pending for SourceLanding Mutation.
At the actual landed source, remove only the class-level
ParallelLimiter<GitProcessSpawnLimit> from
tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleGateTests.cs.
Build and select exactly
`/*/*/ProcessSpawnLimitTests/Git_only_classes_carry_the_git_limiter`.
Require one executed test and the missing-attribute assertion naming
C0824WorkspaceSlotRoleGateTests; compilation/fixture errors do not count.
Restore the exact attribute, fresh build, and execute that same method green.
Do not change the one-second GitBudget or 80-second class timeout.

This candidate is not a bound mechanical pack or execution receipt. Mutation
owns binding, deliberate red/restore/green execution and missing-control discovery.
Ordinary Code red-first evidence at the guard-only commit does not discharge PC-1.
