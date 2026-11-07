# CARD-1739 unbound mutation candidates

Original Code / landing owner: 68c34eea-6a4d-4a4f-97eb-b8e77ad9e9a6.
Plan: docs/superpowers/plans/2026-11-07-card-1739-receipt-fixture-plan.md.
Binding to the actual landing SHA and every deliberate mutant/red/restore/green
cycle belong to SourceLanding Mutation. All 16 variants remain pending.

| ID | Method-scoped selection | Variants / intended assertion |
|---|---|---|
| PC-1 | `/*/*/CheckpointReceiptSafetyTests/receipt_row_and_build_identities_refuse_blank_without_writing` | Delete each count token (executed, passed, failed, skipped) from Fixture's healthy receipt; CP-fixture expected healthy validate controls fail |
| PC-2 | `/*/*/CheckpointReceiptSafetyTests/malformed_state_shape_by_verb_table_matches_documented_exits` | Delete each count token from Fixture's healthy receipt; report-only expected healthy validate controls fail |
| PC-3 | `/*/*/CheckpointSourceExecutionTests/validators_refuse_changed_count_tokens` | Bypass one count-token check at a time in each real validator (4 tokens x 2 validators); missing-token expected exit/reason fails |

Retain every existing assertion/deadline and run exact methods, never a whole class
for deliberate cycles. Mutation must establish compilation and the intended failing
assertion, restore, rebuild and obtain fresh green with nonzero execution counts.
Missing-control discovery remains owed even if a candidate is equivalent/invalid.
