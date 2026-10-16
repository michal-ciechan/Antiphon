# CARD-1303 R3 unbound mutation candidate

SourceLanding Mutation binds this after ordinary Review and caller publication.
Original Code owner: 6121c04b-5c8b-4b0a-b636-8c2db674fa3e.
Plan: docs/superpowers/plans/2026-10-16-card-1303-r3-card-lock.md.

All controls are pending; ordinary red-first development is not Mutation completion.

| PC | Mutation | Exact detecting filter | Expected red |
|---|---|---|---|
| PC-1 | Restore AgentTaskService card admission FOR UPDATE | `/*/*/PipelineSpeedIntegrationTests/C1303_CardAdmissionAllowsClaimSessionInsert` | claim session insert barrier not reached; FK lock timeout |
| PC-2 | Remove AgentTaskService card admission lock | `/*/*/PipelineSpeedIntegrationTests/C1303_CardAdmissionAllowsClaimSessionInsert` | independent NOWAIT probe finds no held fence |
| PC-3 | Remove AgentTaskService card admission lock | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AdmissionFence` | close-first attempted fence witness fails |

Mutation owns method-scoped red/restore/green and missing-control discovery.
