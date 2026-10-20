# CARD-1307 unbound Mutation candidates

Code owner: `e207a2d4-9d81-40ae-be94-8b688e62b26f`.
Plan: [cancellation and partial tool removal](../plans/2026-10-20-card-1307-cancellation-partial-removal-plan.md).
S1 implementation: `0fe7e0409e1fc7bfd82199626b1a6bda8259089f`.
S2 implementation: `ee2e64e5eab5365d7fbda33ae34768b0d9438277`.

These are unbound candidates, not a SourceLanding pack or completed positive
controls. The plan declares no PC-n rows. All candidates and missing-control
discovery remain pending for Mutation, including discovery with a zero-PC plan.
Code ran only the commissioned ordinary baseline witnesses and green checkpoints.
Mutation owns deliberate mutants, method-scoped red/restore/green cycles and any
finding that a listed candidate lacks an effective control.

| Candidate | Variants to investigate | Existing ordinary methods | State |
|---|---|---|---|
| C-1 | Omit the removal marker; publish it after the first tool unlink; lose the original read failure | `C1307ToolRemovalTests.Mid_removal_cancellation_leaves_marker`, `Read_deadline_leaves_marker` | Pending |
| C-2 | Ignore the prior-attempt marker or treat it as deletion authority | `C1307ToolRemovalTests.Mid_removal_cancellation_leaves_marker`, `Complete_removal_and_manual_recovery_are_absent` | Pending |
| C-3 | Continue after marker publication failure or replace a foreign marker | `C1307ToolRemovalTests.Marker_failure_prevents_tool_unlink` | Pending |
| C-4 | Bypass original tool or executor custody admission | `C1307ToolRemovalTests.Custody_refusal_preserves_sentinels` | Pending |
| C-5 | Lose raw, wrapped or swallowed caller cancellation; omit the post-result check | `C1307CancellationTests.Canceled_wait_returns_130`, `Entered_receipt_cancellation_returns_130` | Pending |
| C-6 | Classify a receipt deadline or unrelated OCE as caller cancellation | `C1307CancellationTests.Deadline_without_caller_cancel_stays_invalid` | Pending |
| C-7 | Access an earlier green report after printing canceled partial-cleanup evidence | `C1307CancellationTests.Partial_cleanup_prints_residue_before_canceled_exit` | Pending |
| C-8 | Signal a live executor or change receipts when canceling its observer | `C1307CancellationTests.Canceling_wait_leaves_executor_and_receipts_unchanged` | Pending |

Bind each variant to one exact method before execution; the multi-method entries
above are alternatives to inspect, not whole-class PC filters. The pre-execution
token check also needs independent control discovery: a wait's own token check can
mask its omission at the command boundary.

S3 is unimplemented in this Code budget. Its console subscription lifetime,
Row probe/acquire/driver token flow, executor terminal 130 persistence and existing
result controls remain ordinary V-3/R-3 work first. Do not interpret these candidate
notes as completion of S3 or permission to skip its CP-5/CP-6 rows.
