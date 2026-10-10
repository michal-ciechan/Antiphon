# CARD-0881 Code report (task 7b32e72e)

Outcome: S1 and S2 are on `feat/card-task-7b32e72e`. CP-1 green 78/78 at `3891db5b6054cc126cd33962fbfdf2a382ee7d59`. CP-2 green 99/99 at `c18147511dd0aa5756b086f8adbb6594da49f909`. Post-land Mutation was not run. PC-1..PC-4 stay pending. Restart: none. Landing owner is this Code task (`7b32e72e`).

Worktree: `/work/worktrees/task-7b32e72e`. Desktop checkout `C:\Antiphon\worktrees\card-task-7b32e72e` was not used. Branch fast-forward-only from `b30a3d8e746d7b454d2a534e564520a83a298e98`. Bound run; the task token was not printed.

Plan: `docs/superpowers/plans/2026-10-10-card-0881-effective-settings-pipeline-plan.md` (## Results holds the unedited CHECKPOINT lines).

## What landed

- `GET /api/agent-tasks/pipeline` carries `runners`, `runnerDefaults` (null before the first import; `ReadAsync` does not seed), and `hosts[].remaining`.
- Standing policy in the orchestrator bundle, AGENTS.md, `docs/orchestration-loop.md` §1 and the WIP paragraph, and `.claude/skills/antiphon-orchestrator/SKILL.md` tells the orchestrator to read that route and use its limits.
- `docs/ops-http.md` and `docs/antiphon-api.md` document the same read.
- Pins: both phrase lists, negative stale phrases, `the_effective_settings_route_is_documented`, and the orchestrator byte pin.

## Bundle

LF length 13379 (ceiling 14310). InstructionBundles prefix `b202af70`. File SHA-256 `cc5dfdfc6fd462fd162d21d1de5a07f982e94c068d7db578856e30fa0c4bf057`. Argv stays under 29500 because CP-2's InstructionBundleTests passed the `budget - 500` assertion. The success path does not print the composed length.

## Deviations

- Loop item 1 keeps `use the lower effective stage cap`. The plan block omitted it, and that phrase is only in item 1 of the standing-policy slice. Wording: "Antiphon's enforced limits are the ceiling, so use the lower effective stage cap: the lower of four and the enforced stage limit applies."
- The stale-phrase loop is in both pin classes. The plan snippet named only StandingPipelinePolicyDocumentationTests. V-4 also names OrchestratorInstructionsGuidanceTests.
- antiphon-api uses `` `/api/agent-tasks/pipeline?projectId=<guid>` `` so the contiguous string V-5 requires is present. The plan append said `` `?projectId=<guid>` ``.

## Checkpoints

Closed list only. No whole-Unit. No unlisted build or test. CP-1 executed 78 because AgentTaskPipelineStatusTests is 66 on this base (plan text said 39). Min 51 holds. The closed table was not edited.

- CP-1 compile fail, run `20261010-190256-9b99`, commit `7d3788440f77e76c4007639860bdc5faca2ffb46`, build=failed, slot line `slot=skipped` (executor granted and released the build lease). Missing usings, fixed in `1a562d839`.
- CP-1 red, run `20261010-190712-1585`, commit `1a562d8395e34a8992884a00686fffcaacbc09cc`: executed=78 passed=75 failed=3 skipped=0. Failures were absent JSON properties `runners`, `runnerDefaults`, `remaining`.
- CP-1 green, run `20261010-191127-c8d2`, commit `3891db5b6054cc126cd33962fbfdf2a382ee7d59`: executed=78 passed=78 failed=0 skipped=0, slot=granted, buildSource=verified.
- CP-2 compile fail, run `20261010-192441-c87e`, commit `c998782c6ea19d571057ff181f466fc298dfa945`, build=failed, slot line `slot=skipped` (executor granted lease `cbaed9b6-e2ce-41fa-b572-7e0ca6a8799d`). CS1503 on `ShouldContain(string, string)`. Fixed in `c18147511` with `Case.Sensitive`.
- CP-2 green, run `20261010-192656-a6da`, commit `c18147511dd0aa5756b086f8adbb6594da49f909`: executed=99 passed=99 failed=0 skipped=0, slot=granted, waited=0s, buildSource=verified.

Pre-existing warning CS8602 in `tools/Antiphon.Checkpoints/Execution/TaskOwnerGuard.cs` is not a failure.

## Ordinary V/R

V-1, V-2, V-3, R-1 passed on the green CP-1. V-4, V-5, R-2 passed on the green CP-2. Final Review ordinary scope is the plan CP list only.

## Mutation

Not run. PC-1..PC-4 remain pending for method-scoped SourceLanding Mutation.
