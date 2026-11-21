# CARD-2043 retirement audit candidate

The original Code task and landing owner is 855208da-adcd-441f-b039-333b754d9f14. The [checkpoint plan](../plans/2026-11-21-card-2043-checkpoints.md) records ordinary verification. All controls are pending for SourceLanding Mutation.

| ID / variant | Mutation | Exact method in VolumeRetirementContentTests | Expected red |
|---|---|---|---|
| PC-1 / discard-diagnostic | Omit the sanitized stderr suffix in consider_dirty | Nested_scratch_status_failure_reports_safe_stderr | The refusal loses the missing executable or the redaction marker |
| PC-2 / permit-fsmonitor | Remove the fsmonitor override from the audit Git environment | Nested_scratch_audit_does_not_execute_repository_commands | The fsmonitor vector observes the configured executable |
| PC-3 / permit-filter | Omit the filter overrides for status | Nested_scratch_audit_does_not_execute_repository_commands | The clean-filter vector observes the configured executable |
| PC-4 / skip-linked-content | Skip queue_dirty for the agreed runner-tmp checkout while retaining its publication roots | Nested_scratch_requires_clean_content_and_published_recovery_tips | The dirty vector loses its refusal |
| PC-5 / skip-linked-head | Omit the linked admin HEAD from both pseudoref and resolved-tip collection while retaining its reflog | Nested_scratch_requires_clean_content_and_published_recovery_tips | The unpublished detached HEAD vector loses its refusal |
| PC-6 / skip-linked-reflog | Omit linked admin log roots while retaining its HEAD | Nested_scratch_requires_clean_content_and_published_recovery_tips | The reflog-only vector loses its refusal |

Filter each red and restored-green invocation to `/*/*/VolumeRetirementContentTests/<ExactMethod>`. Controls share the production shell and require separate cycles.
