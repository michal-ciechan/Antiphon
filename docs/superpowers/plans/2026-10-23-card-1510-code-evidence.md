# CARD-1510 Code evidence and frozen handoff

Original Code/landing owner: 5019ea31-13ce-48dd-bdc7-4017c6529e1f.
Branch: feat/card-task-5019ea31. Worktree: /work/worktrees/task-5019ea31.
Task base: ef121b23f3d78bbeb31e09886dcf8448e4a633e5, clean origin/master at start.
Plan: docs/superpowers/plans/2026-10-23-card-1510-path-probe-plan.md.

Fixture-only repair: the Windows RunnerProcessProbe test launcher now calls the
existing ScriptHarnessProcessFixture installed-pwsh resolver and asserts the result
is not a WindowsApps alias. The resolver retains real file existence and
FileAttributes.ReparsePoint rejection and exposes supplied PATH for deterministic
fixtures. Its pure WindowsApps-component predicate handles both separator forms.
The original bounded-PATH test body, exact ordered-entry comparison, 4000-character
bound, duplicate/missing filtering and relative-path test assertions are unchanged.
No deadline, product source or assertion was loosened. No host PATH mutation.

The brief's OwnedPty-helper reference was checked against master and pushed
CARD-1515/1516 source c7b0899cbf6ad2b6965ab47055a37cc9007468e6: those OwnedPty
sources contain no separate installed-pwsh helper. The reused existing helper is
ScriptHarnessProcessFixture.ResolveInstalledPowerShell, used by the Windows script
and directory-link fixtures. The prior interim census was read from
origin/feat/card-task-77c15d47 because its named path is absent on master. The group-5
Debug report and CARD-1510 were read in full for the relevant failure.

## Demonstrated regression tests

S1 red source: dc2cb478edb5702ccf1f843eaea4eedcfd165065 (committed/pushed before run).
Run: .antiphon/checkpoints/20261023-023937-c03a.
Fresh TRX TestMethod identities and result counters were inspected: CP-1 selected
all 10 classifier arguments, with the slash/mixed-separator aliases failing the
expected Boolean assertion (8 passed, 2 failed); CP-3 selected all 5 filesystem
cases, with alias-first/reparse-first returning the wrong path and alias-only,
no-pwsh and empty-path failing the required FileNotFoundException (0 passed,
5 failed). A real symbolic link's FileAttributes.ReparsePoint was observed before
selection. No build error, timeout, skip or zero count is a red witness.

One build ran (337.2612208 seconds); report builds=4 counts the manifest inventory,
including three unused outputs. Wall 6m48s. Build slot granted/waited=0s; each test
row granted/waited=15s. Source is clean and buildSource verified. The red receipt
validator exited 2 with cleanup_refused because failed-run output was retained;
this historical red is not a clean green certificate. Evidence remains ignored.

The separately explained checkpoint-tool bootstrap was the only unlisted build:
build-slot.ps1, OutputPath=bin-c1510-tool/, slot=granted waited=1s held=11s,
0 errors (4 existing compiler warnings). No ad hoc test driver was run.
The duplicate-build warnings for native CP-5/6 are intentional: portable and native
outputs belong to different OS executions and cannot share a build.

```text
CHECKPOINT CP-1 commit=dc2cb478edb5702ccf1f843eaea4eedcfd165065 build=ok filter=/*/*/RunnerProbePowerShellFixtureTests/WindowsApps_component_is_classified_independently_of_host executed=10 passed=8 failed=2 skipped=0 trx=/work/worktrees/task-5019ea31/.antiphon/checkpoints/20261023-023937-c03a/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=dc2cb478edb5702ccf1f843eaea4eedcfd165065 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=dc2cb478edb5702ccf1f843eaea4eedcfd165065 build=reused filter=/*/*/RunnerProbePowerShellFixtureTests/Probe_selects_regular_install_or_refuses_absence executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-5019ea31/.antiphon/checkpoints/20261023-023937-c03a/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=dc2cb478edb5702ccf1f843eaea4eedcfd165065 sourceState=clean buildSource=verified
```

## Final source and outstanding obligations

This report is committed with the fix before the last run. Its containing HEAD is
the expected SHA for CP-2/4/7. Results at this report's commit are pending, never
prospectively green. The actual final SHA, validated receipt, fresh TRX counts,
unedited CHECKPOINT lines, compatibility checks and cleanup record are written
only after completion to ignored .antiphon/task-5019ea31.md. This keeps the final
pushed tip exactly equal to the tested source without a post-test report commit.

| IDs | Status at this frozen report |
|---|---|
| V-1 / R-1 | CP-1 intended assertion red established; CP-2 final green pending |
| V-2 / R-2 | CP-3 intended assertion red established; CP-4 final green pending |
| V-3 / R-3 | CP-5/6 OWED Windows; not run, not passed |
| R-4 | CP-7 exact adjacent Linux probe pending |

Explicit brief excludes Unit lane, full affected classes and whole suites; no such
run occurred. Native acceptance remains incomplete. Desktop, live qualification
supervisor PID 209072 and C:\Antiphon\releases were not accessed. No Windows green,
Final/Full gate, land or activation is claimed. Restart: none.
GET /api/runner-defaults and /api/session-runners were read: revision 2 selects the
available Linux default; no runner/platform pin or fleet address was embedded.

Mutation candidate: docs/superpowers/mutations/2026-10-23-card-1510-candidate.md.
PC-1 and all separator/case/component-boundary variants, PC-2 and all alias-first,
alias-only, no-pwsh, empty-path, reparse-first variants remain PENDING for
method-scoped SourceLanding Mutation, including missing-control discovery.

--- next stage ---
next: review
handoff: Review the fixture-only repair and exact portable red/green evidence. Retain Windows CP-5/6 (V-3/R-3) as owed and the native gate incomplete. Original Code 5019ea31 owns landing; all PCs await SourceLanding Mutation. Restart none.
artifact: docs/superpowers/plans/2026-10-23-card-1510-code-evidence.md
