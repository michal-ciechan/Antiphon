# CARD-1300 unbound Mutation candidate

Original Code/landing owner: task 08ee53e0 on `feat/card-task-08ee53e0`.
Plan: `docs/superpowers/plans/2026-11-10-card-1300-refusal-order.md`.
CARD-1289 PC-1 through PC-6 stay pending in
`docs/superpowers/mutations/2026-10-15-card-1289-admission-candidate.md`.
Do not treat this file as discharging them.

All controls below stay pending for method-scoped SourceLanding Mutation.
Local Code mutant cycles are evidence only. Bind the landed source and an
external evidence root before use. Filter the exact method, with a trailing
`*` for parameterized rows. Do not run the class.

| PC | Method | Mutant | Expected red |
|---|---|---|---|
| PC-1 | `PipelineSpeedIntegrationTests.C1300_ClosedCardPrecedesCapacityAndBase` | Remove the ordinary-create `RefuseImplementationAdmission` call that runs before the capacity preview | capacity throws `ConcurrencyLimitException`; base throws `worktree_base_source_invalid`; git has `Calls > 0` |
| PC-2 | `PipelineSpeedIntegrationTests.C1300_AdmissionChangePrecedesCapacityAndBase` | Same removal as PC-1 | capacity throws `ConcurrencyLimitException`; base throws `worktree_base_source_invalid` |
| PC-3 | `PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit` argument `board` | Drop `BoardId` from `IsBenignAdmissionTransition` | the create is admitted |
| PC-4 | `PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit` argument `identifier` | Drop `Identifier` from `IsBenignAdmissionTransition` | the create is admitted |
| PC-5 | `PipelineSpeedIntegrationTests.C1300_AdmissionChangePrecedesCapacityAndBase` and `C1289_ConcurrentCardEdit` argument `review-policy` | Drop `ReviewPolicy` from `IsBenignAdmissionTransition` | review-policy admits; the C1300 admission rows continue into capacity or base |
| PC-6 | `PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit` argument `workflow` | Add `WorkflowRunId` to the must-match list | `card_admission_changed` instead of an admitted child |

PC-1 and PC-2 are one source edit witnessed by two methods. Mutation still records each method. A change during git remains on the locked recheck and is not a new control.
