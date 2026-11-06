# CARD-1581: dependent dispatches must not delay their land

Original Code/landing owner: edc7b76c-0b0e-4c38-8410-41193227cf49.
Base: 5abd946dd2c235b6e873b0cbb95c738f35216dcf. Branch:
feat/card-task-edc7b76c. Worktree: /work/worktrees/task-edc7b76c.
Final verification. The card has no revisions and no existing CARD-1581 plan.
This bounded implementation/verification design follows the card's expected behavior.

## Cause and implementation

The current dispatcher already releases its actual repository lease on WaitForLand.
Its off-thread sibling inspection also holds before acquisition. The remaining cycle
is a dispatch waiter registered on an earlier lease refusal: a sibling land can arrive
and be picked before the next dispatcher tick reconciles that waiter. Land admission
yields to that dependent just like an independent dispatch, costing a whole yield
budget despite the dispatch being unable to progress until the land finishes.

S1 commits a failing integration witness against unchanged base production. Real
Git, the base resolver, dispatcher, database and land protocol demonstrate the
dependency and eventual publication/dispatch. No sleeps or timeout changes.
The original S2 filtered queued, uncut, ordinary same-card Worktree dispatch waiters out of
the current land's yield decision. The registry keeps those entries for its owner
to reconcile; independent waiters, settlements, read-only, already-cut and repair
tasks retain their turn. A mixed list yields only to independent entries. Exclusion
is conservative while the same-card pending land exists; it does not change source
selection, repository lease acquisition, writer exclusion or the maximum yield budget.

Review B-1 found that this original predicate was over-broad; S4 below supersedes it
with actual sibling-guard dependency proof. This is a product defect proven by the card
and the red witness. Review is required.
Read-only Investigate admission semantics are unchanged; the witness also exercises
an Investigate task using Worktree mode, matching the reported dependency.

## Verification design

- V-1: `AgentTaskLandDispatchYieldTests.C1581_a_same_card_dispatch_waiting_for_land_does_not_make_that_land_yield`
  (Code and Investigate): resolver says WaitForLand; dispatcher leaves task queued
  without a checkout/session and the actual lease remains acquirable. A registered
  dependent waiter must not stop the first land admission/publication. The same
  dispatcher then cuts its worktree and dispatches. RED at base, GREEN after S2.
- V-2: `C1581_an_independent_waiter_keeps_its_dispatch_priority`: different-card,
  already-cut, read-only, repair, settlement and mixed controls retain a real Held
  result, zero admission attempts and the independent waiter's identity. Mixed
  diagnostics must omit the dependent identity. That variant is also RED at base.
- R-1: every full affected integration class: AgentTaskLandDispatchYieldTests,
  AgentTaskDispatchBaseGuardTests. Adjacent lease lifecycle smoke: full
  DispatcherLeaseSpanTests. Full RepositoryLeaseWaitersTests guards the pure decision.
- R-2: full TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*,
  SlowTestTripwire*, NativeLinuxPolicyWitnessTests*, C1729LayeringGuardTests*,
  MechanicalPcContractTests* and TaskPlatformGuidanceTests*. Do not change policy inputs.
- R-3: the entire exact Unit lane, including method-level Unit cases. Unit alone
  omits landing, leases and persistence; R-1 supplies those integration witnesses.
- V-3: manual diff review, full base..HEAD evidence policy check, clean source receipt
  validation, fresh TRX roster/count inspection and fetched origin/master merge-tree.
  Read runner-defaults/session-runners. Portable work stays unpinned. No OS-selected
  production behavior/helper or rc startup dependency changes. Restart impact: server
  after caller lands and activates the fix; runner none. No startup smoke needed here.

All PC-1/Code, PC-1/Investigate and PC-2/mixed, PC-3/independent variants remain
pending for method-scoped SourceLanding Mutation. Unbound candidates are in
docs/superpowers/mutations/2026-11-06-card-1581-land-dispatch-cycle.md.
Code executes no deliberate mutants. Red-first base execution is ordinary evidence.

## Performance budget

One native incremental compile is required by the brief, plus a separately leased
checkpoint-tool bootstrap; both are explicitly listed exceptions to the closed
checkpoint rows. Every driver uses a host slot, UseAppHost=false and TEMP=/tmp.
S1 and S2 need separate isolated builds because their source differs. S2's three
serial rows share one isolated build; no more than three rows per run.

No full-assembly or namespace run. The entire Unit selection is required by Final,
protecting the cross-cutting pure decision/guard contracts, not expanding integration
scope. R-1 bounds affected integrations to the three named classes. Unit p95 is
unknown: the existing provisional 15-minute estimate derives the existing 45-minute
row cap, not a widened timeout. Existing CARD-1720/Debug 86a941a3 tracks the >30-minute
Unit incident; retain timing/run identity for caller Debug if it recurs, do not repeat.
Other estimates are provisional one-run measurements to collect, not p95 claims.
Expected ordinary S2 floor: 27 minutes plus authoring; finite derived total allowance
64 minutes plus bounded slot admission. No unchanged green row is repeated.

Assembly-owned PostgreSQL/migrations are shared per run; test schemas and real Git
repos provide isolation. Existing fixtures own teardown. Fresh TRX provides per-test
durations; checkpoint reports provide admission/build/run wall timings under ignored
.antiphon/checkpoints/. Existing unavailable subphase/external-call attribution stays
unknown. No new test timeouts, retries or assertion relaxations.

### Historical first-round checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1581-red/ | base-witness | /*/*/AgentTaskLandDispatchYieldTests/C1581* | V-1,V-2 red-first | expected assertion failures against unchanged base production; retain actual outcomes | 8 | 5 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1581-final/ | lease-integration | /*/*/(AgentTaskLandDispatchYieldTests*)\|(AgentTaskDispatchBaseGuardTests*)\|(DispatcherLeaseSpanTests*)\|(RepositoryLeaseWaitersTests*)/* | V-1,V-2,R-1 | every full named class, nonzero; 0 failed | 1 | 8 | true |
| CP-3 | S2 | CP-2 | guards | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/* | R-2 | every full named class, nonzero; 0 failed | 1 | 4 | true |
| CP-4 | S2 | CP-2 | unit | /*/*/*/*[Category=Unit] | R-3 | whole exact Unit predicate, nonzero; actual inherited/native outcomes reported | 1 | 15 | true |

## Round-1 Review repair (Code ad87e56f)

Reviewed source/base for this repair: 136083c7a181d942a78f92d9d43afb1cb5206864.
Original landing owner remains edc7b76c-0b0e-4c38-8410-41193227cf49.
Review: /work/review-evidence/task-9152ad37/review.md, B-1 and B-2.
The explicit brief accepts Interim because CARD-1720 owns B-3: do not run Unit.
R-3/CP-4 remains deferred, never passed. This repair cannot authorize landing.

S3 commits seven witnesses before changing production. V-4 proves the actual
sibling guard and resolver permit same-card explicit-source/descendant and
already-contained dispatches, then requires Held/Attempt=0/retained waiter.
V-5 traverses the hosted land queue, real dispatcher and launch queue, notification
scanner and session message queue to complete recipient UserPrompt evidence.
Eligible, busy, lost-volatile-queue restart and committed-enqueue interruption
variants join task, request, terminal event, note, queue row and transcript sequence;
they assert one publication and one dispatch per task. Fake terminal submission
records the actual submitted body; no expected prompt is planted. Barriers and
joined workers replace sleeps. The first hosted pass yields to a runnable explicit
same-card waiter; the next passes its dependent sibling and unblocks that dispatch.
The mixed fixture now names its different-card control explicitly, rather than
accidentally changing the source card while adding a second waiter.

S4 excludes only candidates whose real CardSiblingInspectionService result proves
pending_land on this exact landing task. Exempt, contained and unknown results
retain priority. Constructor callers without the optional inspection service retain
waiters conservatively. No lease acquisition or publication fence changes.

V-1/V-2 and all eight original cases remain required; R-1 runs the original full
129-case selection plus all new cases once. R-2 runs every original guard class in
one union-filter row. V-3 retains manual diff/evidence/provenance/merge-tree checks.
No full assembly run, timeout widening or repetition of a green selection.
CP-5/6 estimates are provisional under load, not p95; each bounded integration row
has the existing 24-minute derived cap. Bootstrap of the checkpoint CLI is the only
unlisted native build, separately leased with UseAppHost=false and TEMP=/tmp.
Rows use isolated checkpoint outputs. Portable service and test changes add no
OS-selected behavior or startup dependency; server restart after activation,
runner restart none. Read runtime runner settings; placement stays unpinned.

PC-4 (restore the over-broad database-only filter), PC-5 (remove dependent filtering,
hosted acceptance) and every original PC variant remain pending SourceLanding
Mutation; ordinary red-first tests do not discharge them.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-5 | S3 | tests/Antiphon.Tests -> bin-c1581-r2-red2/ | repair-red | /*/*/AgentTaskLandDispatchYieldTests/(C1581_a_runnable*)\|(C1581_hosted*) | V-4,V-5 | expected priority assertion failures at reviewed production | 7 | 8 | true |
| CP-6 | S4 | tests/Antiphon.Tests -> bin-c1581-r2-green/ | affected | /*/*/(AgentTaskLandDispatchYieldTests*)\|(AgentTaskDispatchBaseGuardTests*)\|(DispatcherLeaseSpanTests*)\|(RepositoryLeaseWaitersTests*)/* | V-1,V-2,V-4,V-5,R-1 | every named full class; zero failed | 136 | 8 | true |
| CP-7 | S4 | CP-6 | guards | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/* | R-2 | every named full class; zero failed | 75 | 4 | true |
