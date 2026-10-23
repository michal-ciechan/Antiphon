# CARD-1541 / CARD-1542 fixture repair verification

Original Code/landing owner: `47b2f26b` (Final), base `0a8e6299b1a261591973286272d81c818415e577`.
The commissioning brief explicitly limits this round to the three whole integration classes;
no Unit lane, assembly sweep, native canary, desktop action or production change is authorized.

## Repair design

S1: Retain the exact 69 failing identities and inherited signatures from
[the Debug diagnosis](../../investigations/2026-10-23-master-69-reds-after-0680-1491-debug.md).
Its direct-base receipts discharge red proof; do not rerun baseline. Add explicit absent and
Unknown observation queue-refusal coverage before repairing fixtures.

S2: Add one opt-in test helper using the fake's existing real dialog detector and guarded
conditional-input seam, bound to the persisted accepted generation. Opt in only affected
fixture worlds and independently created adapters. Retain every existing assertion.
Stamp the boundary warm Claude process with the catalog's actual Medium/Custom effort.
Reuse CARD-1529's test-only C544 recovery observation/store-write pattern and warm stamp,
so the whole class can qualify without importing its production change.

## Verification design

- V-1: Whole DelegationBriefRecoveryTests; original delivery/ordering/deadline assertions.
- V-2: Whole DelegationDispatchRecoveryBoundaryTests; custody, recovery, exact SQL budgets,
  and warm-reuse same-session/no-launch/no-prepare assertions.
- V-3: Whole VerificationRoundDeliveryTests; complete receipt, busy/idle, batching, fault cuts,
  provider restart, and C544 brief handoff.
- R-1: Both C1541_Resume_keeps_brief_pending_without_clear_dialog_observation arguments;
  absent and Unknown remain Pending with zero attempts and zero input.
- R-2: Compare full task base..HEAD: fixture/test code only; no weakened assertions/timeouts.
- R-3: Validate final-tip clean receipt/build provenance, per-class nonzero fresh TRX counts,
  evidence diff policy, pushed SHA and conflict-free merge-tree against current origin/master.

No new manual/live acceptance is required for these fixture-only repairs. Existing CARD-0680
native activation obligations remain separate and are not claimed here. RC `5a126f6fc` lacks
both gates: no fixture cherry-pick is needed for that unchanged RC; no RC green is claimed.

Mutation owns pending PC-1/absent-observation guard, PC-1/Unknown-observation guard,
PC-2/dialog fixture opt-in removed (original delivery tests), and PC-3/warm effort stamp
removed (C1153_Only_new_cold_dispatch_prepares_evidence warm-reuse argument).
These are discovery candidates, not executed mutants or completed controls.

### Cost

One shared isolated .NET build plus one combined three-class execution, estimated 20 minutes.
Run total timeout 60m, row timeout 45m. No unchanged-proof repetitions required after green.
If any class remains red, stop and report exact identities/signatures per the brief.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | tests/Antiphon.Tests -> bin-c1541-1542/ | fixture-classes | `/*/*/(DelegationBriefRecoveryTests*)\|(DelegationDispatchRecoveryBoundaryTests*)\|(VerificationRoundDeliveryTests*)/*` | V-1,V-2,V-3,R-1 | all listed, 0 failed | 438 | 20 |
