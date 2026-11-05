# CARD-1722 item 3: pending SourceLanding controls

Original Code/landing owner edbaee26-2ba5-4999-aa4d-17a5b60a7480.
Plan: docs/superpowers/plans/2026-11-05-card-1722-scheduler-receipt.md.
Both controls are pending; Code has executed neither. Ordinary red-first evidence
is distinct from deliberate mutation. Bind to caller-landed source, then mutate
only tools/Antiphon.Checkpoints/Execution/RunScheduler.cs, one control at a time.

Exact selector for both:
`/*/*/RunSchedulerTests/rows_wait_for_the_build_source_receipt_before_readiness`.

- PC-1 / reorder: publish progress.State=ok before the receipt gate and Write.
  Require the unchanged CP-2 green assertion to fail with build_source_mismatch.
- PC-2 / drop: omit built.Write and publish ok as though it succeeded.
  Require build_source_mismatch; a build error or zero executions is not red.

Mutation owns red/restore/green and discovery of any missing control. Restore the
exact source path and run the same method green after each control, preserving
separate receipts. No wider selector, retry, timeout change or assertion weakening.
