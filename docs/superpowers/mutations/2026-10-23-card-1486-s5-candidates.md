# CARD-1486 S5 unbound mutation candidates

Original Code/landing owner: `75c32a51-a96a-4e1f-af75-359c9bf55d17`.
Plan: `docs/superpowers/plans/2026-10-22-card-1486-reply-followups-plan.md`, S5/D-5.

The plan has zero PC-n rows. These are unbound candidates, not executed controls.
SourceLanding Mutation owns binding, method-scoped red/restore/green and missing-control
discovery after ordinary Review and landing of the original Code task.

- Drop the stage at the AnswerAsync call or omit FindingContract from BlockedReplyContract.
  Guard: BlockedReplyReportRetentionTests.Blocked_reply_wire_to_a_stage_task_restates_the_finding_marker.
- Move finding instructions before the caller text or after the closing report tokens;
  omit or substitute a task-specific found/clean token. Same guard.
- Append finding instructions when Stage is null.
  Guard: BlockedReplyReportRetentionTests.Blocked_reply_wire_requires_a_complete_report_and_closing_token.
- Discovery: bind a different non-null stage to check against hard-coded Review wording.
  The ordinary S5 witness uses Review; the plan's suggested Stage=Code is not an enum value.

No candidate or variant is discharged by ordinary green tests. No deliberate mutant was run
by this Code task. The pre-fix failure is an ordinary regression witness.
