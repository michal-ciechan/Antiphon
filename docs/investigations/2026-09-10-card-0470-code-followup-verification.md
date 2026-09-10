# CARD-0470 Code follow-up verification

Task `935d4289` completed V-2/V-4/V-6/V-10/V-13 from `5eb9e55c5f6ef5448e83f637239be98a4144c8a0`.
Implementation/test tip: `5447fa20cf08bdc7121c578138d051d782c2ea48` on `feat/card-task-ce744e22`.
Worktree: `C:\Antiphon\worktrees\card-task-ce744e22`. Original Code landing owner: `ce744e22`.
The commit adding this record changes documentation only.

| Verification | Change and evidence |
|---|---|
| V-2 | `MutationRoleContractTests` explicitly checks the default 240-minute ceiling, independent override, and missing-policy fallback. `MutationAdmissionTests` creates through real routing services after a Mutation cell update, then after clearing to any-role and config fallbacks; Code's cell and pin remain unchanged. Real pin writes accept Mutation. |
| V-4 | `MutationDispatchTests.C470_process_cap_includes_mutation` queues Worktree Code behind Working Mutation at process cap 1. No adapter launches while capped; after settlement exactly one launches in a separate, real Git worktree whose path matches the session cwd. |
| V-6 | All four Queued/Working x Review/Land variants run through `AgentTaskReplyService.OnTurnEndAsync` using marked transcript reports. Fresh SQL queries using `AgentTaskRoles.Stage` verify persisted completion, handoff and artifact; caller queue notes carry the canonical destination. Readiness is checked for the fixture's card. |
| V-10 | `DelegateBundleLaunchTests` checks retained cwd for Claude and Codex, literal Mutation bundle composition, and absent read-only flags/configuration. Codex instructions are extracted as raw config text, matching its existing launch contract. Grok composition tests also pass. |
| V-13 | `TaskDetailBody` links the plan to `/plans` with encoded file, optional ref and source task. Both ref-present and ref-absent links are tested. All five specified client files pass and production dist builds. |

## Results

Final focused coverage: **62 backend tests green**, using the latest result for each changed class:

- `backend-2/results.trx`: MutationRoleContractTests 2, MutationAdmissionTests 22, MutationDispatchTests 5, MutationPipelineTests 6, GrokRulesCompositionTests 9; all 44 passed.
- `backend-3/results.trx`: DelegateBundleLaunchTests 18 passed, 0 failed (75.014 seconds).
- Client: 5 files, 67 tests passed, 0 failed (63.32 seconds). `npm.cmd run build` exited 0 and rebuilt dist.

Earlier authoring failures were corrected: backend-1 had 58 passed/4 failed (global readiness assertions included other fixture cards); backend-2 had 61 passed/1 failed (the test incorrectly attempted JSON decoding of Codex's raw instructions). No outstanding failures. No deliberate PC mutation was run.

Raw evidence is retained below `C:\Antiphon\worktrees\card-task-ce744e22\.antiphon\935d4289`:
`backend-1/results.trx`, `backend-2/results.trx`, `backend-3/results.trx`, `backend-2.log`, `backend-3.log`, and `client-tests.log`.

## Rerun

From the task worktree, choose a fresh result directory:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c470-followup/ -- --treenode-filter '/*/Antiphon.Tests.Application/(MutationRoleContractTests*)|(MutationAdmissionTests*)|(MutationDispatchTests*)|(MutationPipelineTests*)|(DelegateBundleLaunchTests*)|(GrokRulesCompositionTests*)/*' --results-directory .antiphon/935d4289/rerun --report-trx --report-trx-filename results.trx
pwsh -NoProfile -File scripts/test-client.ps1 pipelineStageModel.test.ts PipelineStagesPanel.test.tsx RoutingSettingsTab.test.tsx TaskDetailBody.test.tsx DelegateModal.test.tsx
```

Run `npm.cmd run build` from `client`.

## Handoff

All 17 planned PC variants remain pending for D-8's separate Debug worker, together with missing-control discovery. Use the retained branch and original landing owner `ce744e22`; do not land until that pass and any required Review finish. V-15/B-1's loaded-runtime observation remains caller-owned after landing and the canonical server/client rollout. This follow-up did not restart the shared stack or run real provider sessions.

Plan: [Code/Mutation split](../superpowers/plans/2026-09-09-card-0470-code-mutation-split-plan.md).
