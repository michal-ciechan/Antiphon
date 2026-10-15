# CARD-1289 unbound Mutation candidate

Original Code/landing owner: 5a9cd727-c32f-4f59-96ac-40b5a91c70b2.
Plan: docs/superpowers/plans/2026-10-15-card-1289-admission.md.
All controls remain pending for method-scoped SourceLanding Mutation after
ordinary Review and landing of the original Code task. No deliberate mutant was
run by Code. Bind the landed source and an external evidence root before use.

| PC | Method (PipelineSpeedIntegrationTests) | Pending variants |
|---|---|---|
| PC-1 | C1289_ConcurrentCardEdit | restore all-write token comparison: title, description, owner, token |
| PC-2 | C1289_ConcurrentCardEdit | omit state comparison; omit archive refusal; omit platform comparison; omit verification-policy comparison |
| PC-3 | C1289_GitLockDuration | move ordinary Git preview back under the card lock |
| PC-4 | C1289_GitLockDuration | inspect Git before returning an already linked handoff child |
| PC-5 | C1289_CardLockOrder | omit/reorder card lock in observation writer; refusal writer; child admission; swap host/concurrency locks |

Use exact method filters, e.g. /*/*/PipelineSpeedIntegrationTests/C1289_CardLockOrder.
Parameterized methods include their variants; do not substitute the whole class.
Mutation owns red/restore/green and missing-control discovery, including additional
board/identifier/workflow binding controls and other card-lock writers. Existing
CARD-1281 S0/S6a controls remain pending under their own inventories.

PC-6 pending: remove the early capacity preview; guard is
AgentTaskWorktreeBaseCreateTests.T0442_V17_existing_create_guards_precede_source_selection
(concurrency variant). Remove the locked recheck separately and use the existing
AgentTaskConcurrencyLimitTests racing-create control; Mutation must select its
exact method before commissioning. No Code mutant or claimed control green.
