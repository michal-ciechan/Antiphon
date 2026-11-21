# Close advisories mutation candidate

The original Code task and landing owner is `bc823630-a611-48f0-9c23-996da67a5c2a`.
Ordinary verification is in the [checkpoint plan](../plans/2026-11-21-close-advisories-checkpoints.md).
These controls are pending for SourceLanding Mutation.

| ID / variant | Deliberate fault | Exact method in `VolumeRetirementCloseExemptionTests` | Expected assertion failure |
|---|---|---|---|
| PC-1 / skip-refusal | Admit a non-exempt receipt-less directory at the shared-predicate call in `c1609_temp_retirement_clearable` | `Deploy_clearable_checks_receiptless_previews` | The `ready` and `pending` cases get exit 0 instead of exit 1 |
| PC-1 / always-clearable | Return success unconditionally from `c1609_temp_retirement_clearable` | `Deploy_clearable_checks_receiptless_previews` | All five refusal cases get exit 0 instead of exit 1 |
| PC-2 / trust-character-set | Restore the charset-only filter for Close stderr | `Close_wrapper_redacts_unrecognized_remote_refusals` | The fake remote phrases appear instead of `JournalUnknown` |

Run each cycle with `/*/*/VolumeRetirementCloseExemptionTests/<ExactMethod>`.
