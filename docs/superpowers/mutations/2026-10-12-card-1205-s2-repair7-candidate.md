# CARD-1205 repair 7 — unbound Mutation candidate

Status: pending; post-land SourceLanding Mutation NOT run. Landing owner:
efe40115-531a-4664-86d5-c0a87bc899fc, inheriting cbce1a3f-5d59-47d2-a8a3-859ca571c16f
and S1 34df9193c. S1 lands with S2 after Review. This supersedes repair 6's
candidate index only; every PC-1..190 and variant remains pending.

Bindings, payloads and red oracles: [test design repair 7](../plans/2026-10-12-card-1205-test-design.md#repair-7-single-classification-table-t57-t60).
Inherited PC-1..182 retain their obligations in that design; Mutation owns payload
adaptation to the shared classifier and missing-control discovery.

Method-scoped filters only:
- PC-183..186: `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Settled_callback_classification_table`
- PC-187..189: `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Late_callback_classification_table`
- PC-190: `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Completed_callback_wins_cancelled_wait`

Do not use class/suite selection for PCs. Restore exact source and build provenance,
then run those same methods green. No timeout/assertion relaxation. Bind this
candidate to the separately commissioned landed snapshot; no snapshot repair or
publication is authorized. Caller lands this original Code owner after Review,
then commissions SourceLanding Mutation. Unmeasured additive budget: eight
cycles at seven minutes = 56 minutes plus setup, inherited budget separate.
Restart: none.

