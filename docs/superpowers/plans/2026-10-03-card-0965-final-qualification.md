# CARD-0965 published-tip qualification

Companion to `2026-10-03-card-0965-remaining-input-proofs.md`, commissioned by
the continuation brief's final affected-class qualification requirement.
Original Code/current landing owner: `4c1697dc-d944-46ad-ae9e-085276be1663`.

Reuse one freshly built, source-qualified isolated output for the exact existing
CP-1/2/3/7 selections. This uses the owner's documented same-group build reuse;
there is no widened filter or additional test class. Each row has a fresh TRX.
The original CP-6 Unit lane ran once: 3910 passed, 0 failed, 52 inherited skips.
CP-4's 29 passing client tests are credited from the preceding Code task.

Invariants: runner-owned exact input, atomic fallback, private body authorization,
read-only stable attention, strict sweep ownership, seeded upgrade preservation.
Scope is bounded to the ten named integration classes; no unbounded production
impact or full-assembly run. Unit does not prove delivery/leases/landing/persistence.
This qualification supplies restored green for the ordinary strength diagnostics;
all PC-1..PC-36 variants remain pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | Q1 | `tests/Antiphon.Tests -> bin-c965-qualified/` | task-input | `/*/*/(AgentTaskInputSpillTests*)\|(AgentTaskRefineTests*)\|(AgentTaskReplyOverlayTests*)/*` | V-1..V-15, R-1, R-2 | AgentTaskInputSpillTests,AgentTaskRefineTests,AgentTaskReplyOverlayTests | 31 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | Q1 | CP-1 (-NoBuild) | fallback | `/*/*/(AgentTaskInputFallbackTests*)\|(PhoneHomeSpillTests*)\|(PhoneHomeSpillTransportTests*)\|(DurableRunnerSpillReceiptTests*)/*` | V-16..V-24, R-3 | AgentTaskInputFallbackTests,PhoneHomeSpillTests,PhoneHomeSpillTransportTests,DurableRunnerSpillReceiptTests | 29 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | Q1 | CP-1 (-NoBuild) | attention | `/*/*/TaskInputReadFailureTests*/*` | V-25..V-32 | TaskInputReadFailureTests | 8 | 2 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | Q1 | CP-1 (-NoBuild) | compatibility | `/*/*/(ParkedMessageSweepServiceTests*)\|(CapacityRecoveryCompatibilityTests*)/*` | malformed-key, seeded migration | ParkedMessageSweepServiceTests,CapacityRecoveryCompatibilityTests | 18 | 2 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

13 minutes budgeted for a single build and four exact filters; no normal repetitions
after green. This is the brief-authorized qualification, not a second Unit run.
