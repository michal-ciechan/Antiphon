# RC2 gate contracts: unbound Mutation candidate

Status: all controls pending. Original Code / landing owner:
`8a76c085-3f35-48a9-8a0d-1f65e23ad4e7`.
Plan: [RC2 gate contract repair](../plans/2026-10-16-rc2-gate-contract-repair.md).
This is a candidate for method-scoped SourceLanding Mutation after ordinary
Review and caller-owned landing. It is not a bound execution pack or evidence.

| PC / variant | Deliberate defect | Exact method filter | Expected assertion |
|---|---|---|---|
| PC-1 / ReviewThreadId | Omit only this null clause from IsNeverAttempted | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.ReviewThreadId must refuse |
| PC-1 / ReviewReplyConsumedAt | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.ReviewReplyConsumedAt must refuse |
| PC-1 / RuntimeActionKey | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.RuntimeActionKey must refuse |
| PC-1 / DirectReplyPromptSequence | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyPromptSequence must refuse |
| PC-1 / DirectReplyThroughSequence | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyThroughSequence must refuse |
| PC-1 / DirectReplyPendingThroughSequence | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyPendingThroughSequence must refuse |
| PC-1 / DirectReplyProvider | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyProvider must refuse |
| PC-1 / DirectReplyConversationId | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyConversationId must refuse |
| PC-1 / DirectReplyHandle | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyHandle must refuse |
| PC-1 / DirectReplyTailClosedAt | Omit only this null clause | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Whitelist_requires_every_fact | row.DirectReplyTailClosedAt must refuse |
| PC-2 | Remove SessionRuntimeActionDeferralTests registration and its reason | /*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata | missing/unregistered marked class |
| PC-3 | Remove only SessionRuntimeActionTests reason | /*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata | missing-reason for that class |
| PC-4 | Remove RuntimeActionKey from MessageColumns | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Queue_columns_are_accounted_for | explicit inventory differs from reflected entity |

Mutate one coordinate at a time, retain the positive companion and all existing
assertions, and prove the expected assertion red before restoring and proving
the same method green. PC-1 variants touch the same file and method and are not
independent parallel mutations. No timeout changes, retries or assertion
relaxations. Mutation owns additional missing-control discovery and any pack
binding. No Code red/green result discharges these controls.
