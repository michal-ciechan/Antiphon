# CARD-0407 S2a checkpoint manifest

This manifest runs the ordinary S2a verification from the reviewed
[internal decision authority plan](2026-09-08-card-0407-internal-decision-authority-plan.md).
The original plan predates the required checkpoint table. Positive controls remain
pending method-scoped SourceLanding Mutation.

## Verification design

The Unit lane covers grant evaluation, schema and existing pure contracts. The
named integration rows cover persistence, HTTP admission, task lifecycle and
reply compatibility. Each row uses a fresh TRX and one isolated output.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2a | `tests/Antiphon.Tests -> bin-card0407-s2a-cp/` | unit | `/*/*/*/*[Category=Unit]` | V-2, V-3, V-8, V-9, R-1 | >= 2000 executed, 0 failed | 2000 | 12 |
| CP-2 | S2a | CP-1 | decision-service | `/*/*/(AgentTaskDecisionQuestionTests*)\|(AgentTaskDecisionQuestionIntegrationTests*)/*` | V-2, V-3, V-5, V-7, V-9, V-10, V-11, V-12, R-4 | all listed, 0 failed | 14 | 4 |
| CP-3 | S2a | CP-1 | decision-api | `/*/*/AgentTaskDecisionQuestionApiTests/*` | V-6, V-7, V-8 | all listed, 0 failed | 2 | 3 |
| CP-4 | S2a | CP-1 | affected-integrations | `/*/*/(AgentTaskServiceIntegrationTests*)\|(AgentTaskInternalDecisionLifecycleTests*)\|(AgentTaskCallerResolutionTests*)\|(AgentTaskReplyIntegrationTests*)/*` | V-4, V-5, R-1, R-2, R-4 | all listed, 0 failed | 100 | 15 |
