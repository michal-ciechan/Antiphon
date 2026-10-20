# CARD-1233 S8 unbound Mutation candidate

Original Code and landing owner: `c519b9f0-e8f6-4fcf-b9be-fcc93d51ceb6`.
Branch: `feat/card-task-c519b9f0`. Target: master only. Restart: none.
Base: `ff2fd81f3d143ee283e91155c24aca0393999b01`.
Plan: [remaining bounded readers and child custody](../plans/2026-10-20-card-1233-bounded-helper-remaining-plan.md).
Inherited controls: [CARD-1199 plan](../plans/2026-10-12-card-1199-detect-report-plan.md).

S8 routes F1/F9's two confinement launchers and F2/F3's shape-conflict and
tracked-baseline `WaitAsync` through the cancellation-aware fixture helper.
The callers retain their existing token deadlines, combined stdout/stderr
and result shapes. Fixture Git continues using installed Git with `git: false`.
Production Git byte identity, execution drivers and intentional holders are unchanged.

The ordinary W8 detector was committed before the fix at
`cca098a42602c48c3971bd49deedc29655bffc2a`. CP-8 run
`20261020-064446-befa` executed all four cases and failed each on
`cancellation left an owned child alive (parentExits=False)`.
This is ordinary defect evidence, not a deliberate Mutation cycle.

All PCs and variants remain **pending and unbound**. Mutation owns binding to
the published source, compiling mutants, intended red, restoration, fresh green
and missing-control discovery after ordinary Review and caller landing.

## S8 candidates for binding and discovery

Use `/*/*/CheckpointFixtureRoutingTests/C1233_TokenCallers*`.
Its four cases are F1/F2/F3/F9. Each includes ordinary stdout/stderr and
nonzero-result controls, then cancellation with a living direct parent and
with an exited parent whose descendant retains a pipe. PID/start identity and
nonce bind readiness; the operation has a two-second cancellation budget,
return before four seconds, an independent eight-second watchdog and another
two seconds for descendant death. Emergency rescue cannot satisfy the assertion.

| Candidate | Compiling defect to consider | Intended detector |
|---|---|---|
| Actual caller routing | Restore one of the four prior direct launches and waits. | The corresponding W8 case detects a surviving child before teardown. |
| Caller cancellation | Omit the caller token from the shared runner. | The independent watchdog and return bound fail. |
| Cancellation identity | Return a timeout result or lose the original caller token. | Exception type and cancellation-token assertions fail. |
| Descendant custody | Retain only direct-child cleanup in the shared runner. | The exited-parent scenario detects the recorded descendant alive. |
| Output/result mapping | Drop a stream or replace the actual exit code. | Fixed nonce-tagged stdout/stderr and exit-23 controls fail. |

S15 retains the broader source-pin and split-budget sensitivity obligations.
S8 performs no Windows qualification and does not migrate S9-S14.

## Inherited pending PC inventory

Every variant below remains pending method-scoped SourceLanding Mutation.
Resolve exact methods and variant rosters from the inherited plan before binding.

| PC | Pending variants |
|---|---|
| PC-1 | Nine receipt verb/path pairs; remove precheck or nonblocking open. |
| PC-2 | Eighteen receipt verb/path/type pairs; remove type/link guards. |
| PC-3 | Required fields null, wrong-type or missing; element and empty variants; remove shape guards. |
| PC-4 | State/report row/build identities: empty, whitespace and null at every admitted consumer; valid controls and no-write assertions. |
| PC-5 | Tool/script tracked stamps: precheck, nonblocking-open, cap or cancellation removal; FIFO, symlink, directory, dangling and regular controls. |
| PC-6 | Import/coverage identity guards: null, missing, empty, whitespace and derived build syntax. |
| PC-7 | Malformed state crossed with verbs; exception/identity guard removal. |
| PC-8 | Project precheck/nonblocking-open bypass; tool Git, script Git, tool graph and script graph deadline/custody bypass; ordinary controls and Git config-driver discovery. |
| PC-9 | Disable Git hardening or restore direct launch in `SafeDelete.RunGit`; installed-Git tracked/untracked controls. |
| PC-10 | Separate exit/drain deadlines; remove process-group kill; healthy control. |
| PC-11 | Restore synchronous fixture drains; remove group kill; separate exit/drain limits; revert a listed fixture caller to direct launch. S15 retains the complete source-pin and split-budget work. |

R-2 remains pending. No PC is discharged by ordinary CP-8 evidence.
