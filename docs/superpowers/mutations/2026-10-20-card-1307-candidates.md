# CARD-1307 unbound Mutation candidates

Code owner: `e207a2d4-9d81-40ae-be94-8b688e62b26f`.
Stacked S3 Code task: `7fa0910b-0f44-4607-8a4c-bb9628895544`.
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
| C-9 | Omit the console handler, suppress no signal, lose repeated cancellation, or retain the subscription after completion/error | `C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes` | Pending |
| C-10 | Install cancellation suppression on an internal helper | `C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes` | Pending |
| C-11 | Drop the caller token at Row probe, acquire or driver, or fail to release its lease | `C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver` | Pending |
| C-12 | Publish crash/timeout/owner-ended/green for caller cancellation; finish before driver/lease cleanup; lose cancellation during final log flush; omit terminal source binding | `C1307CliCancellationTests.Executor_cancel_persists_130` | Pending |
| C-13 | Classify pre-request caller cancellation as crash or overwrite a foreign receipt | `C1307CliCancellationTests.Cancel_before_request_read_is_not_crash` | Pending |
| C-14 | Classify an internal deadline, unrelated OCE, owner settlement, crash or max-wait as caller cancellation | `C1307CliCancellationTests.No_caller_cancel_preserves_existing_results` | Pending |

Bind each variant to one exact method before execution; the multi-method entries
above are alternatives to inspect, not whole-class PC filters. The pre-execution
token check also needs independent control discovery: a wait's own token check can
mask its omission at the command boundary.

S3 adds ordinary V-3/R-3 coverage through CP-5/CP-6; its actual execution outcomes
belong to the S3 Code report. These candidates do not discharge any positive
control. Native operating-system signal delivery remains outside this portable
subscription-adapter proof.
