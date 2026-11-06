# C0824 role-gate scratch Git admission

Original Code/landing owner: be08aac7. Branch: feat/card-task-be08aac7.
Base: 5abd946dd2c235b6e873b0cbb95c738f35216dcf. Verification: Final.

## Implementation and verification design

Add C0824WorkspaceSlotRoleGateTests to the existing GitOnlyClasses reflection
guard before fixing its missing ParallelLimiter<GitProcessSpawnLimit> declaration.
The scratch helper starts git with UseShellExecute=false through the custodied
CheckpointProcessRunner. RunnerWorkspaceService starts only git in owned scratch
repositories; the phone-home host and runtime stub are in-process. Keep GitBudget
at one second, class Timeout at 80 seconds, and all behavior assertions intact.

Sibling audit: C0824WorkspaceSlotServerTests' four transport methods already carry
ProcessSpawnLimit; C0824TransportGitTests is strict (its drain witness starts a
shell child). C1677RetentionServerTests' five transport methods already carry the
strict limiter. RefOnlyRedirectWitness callers already have strict class limiters.
No sibling admission repair is required. Git and strict lanes remain independent,
as the existing policy specifies; this does not establish a fleet-wide spawn cap.

V-1/R-1: CP-0 must fail the actual compiled-attribute reflection assertion, naming
C0824WorkspaceSlotRoleGateTests. Source otherwise equals base plus this guard/plan.
V-2: CP-1 executes all 19 role-gate cases and the whole limiter guard class.
V-3: CP-2 executes classification, slow-registry/tripwire, native marker policy,
layering guard, mechanical contract, and platform guidance classes in full.
V-4: CP-3 executes the whole Unit lane, including method-level category tags.
Unit omits delivery, landing, leases and persistence; those paths are unchanged.
R-2: run scripts/test-mechanical-pc-contract.ps1 through a build slot.
R-3: full task base..HEAD check-evidence-diff, diff --check, fetch origin master
and conflict-free merge-tree. Inspect every fresh TRX for intended classes/methods
and nonzero counts. Rerun only red selections, never green rows.

## Guard and positive-control inventory

G-1: ProcessSpawnLimitTests.Git_only_classes_carry_the_git_limiter checks the real
compiled attribute. PC-1/remove-role-gate-attribute: remove the new declaration,
run only that method, require a missing-attribute assertion naming this class,
restore, fresh build, same method green. Pending SourceLanding Mutation; Code's
red-first missing declaration is ordinary evidence, not the deliberate PC cycle.
Mutation also owns missing-control discovery. No ordinary IDs deferred to Final.

## Performance budget

No full assembly run. The whole Unit lane is required to preserve the broad Unit
invariant; its class list is unbounded by this localized change. Existing whole-
Unit runtime exceeds 30 minutes (CARD-1720); performance Debug remains owed to the
caller, and a failed/unfinished row earns no Full credit. CP-3's 15-minute estimate
is provisional, not measured p95; unchanged derived deadline is 45 minutes.
Native Windows evidence b5e998ee measured 74.296 seconds for the role class under
89% mean CPU, 14 deadline failures. Linux Debug 680b704d reports 158 Git calls under
300 ms. CP-1/2 estimates are provisional loaded-host allowances, not p95 samples.
Record actual build, slot wait and execution independently; retain TRX durations
and standard phase recorder data where available; unknown phases stay unknown.
One native incremental compile first (explicit brief requirement), then isolated
checkpoint builds. Same UseAppHost=false properties; TEMP=/tmp. Each build/row
has its own slot. At most three rows per run; CP-0 is the red group, CP-1..3 final.
No repetitions required after green. Never widen a timeout or relax an assertion.

## OS and startup witnesses

Test-only class admission metadata and reflection roster change; no OS-selected
production branch, optional startup input or rc startup behavior changes. Existing
real fixture types remain in use. Runtime defaults and catalogue read via GET
/api/runner-defaults and /api/session-runners; portable execution stays on this
assigned Linux mirror. Comparable-load native Windows acceptance is owed to the
next qualification; this task must not run on the desktop. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-0 | red | tests/Antiphon.Tests -> bin-be08aac7-red/ | missing-declaration | /*/*/ProcessSpawnLimitTests/Git_only_classes_carry_the_git_limiter | V-1,R-1 | 1 missing-attribute failure | 1 | 2 | true | TEMP=/tmp |
| CP-1 | fix | tests/Antiphon.Tests -> bin-be08aac7-final/ | role-and-limiter | /*/*/(C0824WorkspaceSlotRoleGateTests*)\|(ProcessSpawnLimitTests*)/* | V-2 | 0 failed | 40 | 4 | true | TEMP=/tmp |
| CP-2 | fix | CP-1 | policy-contract-layering | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/* | V-3 | 0 failed | 50 | 5 | true | TEMP=/tmp |
| CP-3 | fix | CP-1 | whole-unit | /*/*/*/*[Category=Unit] | V-4 | disclose every failure/skip | 1000 | 15 | true | TEMP=/tmp |
