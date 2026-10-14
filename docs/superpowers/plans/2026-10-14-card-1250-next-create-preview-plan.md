# CARD-1250 next-create previews read the default-tier cell

Original Code / landing owner: `0814a3a5-8a8e-456a-869f-1e2a10f359ae`.
Task base: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c` (fetched `origin/master` at start).
Round: Final. Restart: none for the server; the client bundle is rebuilt by the AppHost watcher.

CARD-0097 D-6 left two previews projecting pin-or-policy from `GET /api/role-policy`: the Role
policy section's `Effective next create` column and the delegate modal's tier preview. With the
matrix live (checked on the running server 2026-10-14: `GET /api/complexity-chains?role=Code` has
`defaultComplexity` Medium and role rows on all three tiers; the instructions file renders section
5 "Role × complexity matrix"), an ordinary Worker create of Code runs Codex/Frontier
(gpt-6-astra) while both previews said ClaudeCode/Frontier (fable).

## Design

Client only; no server, DTO or schema change. One pure model,
`client/src/features/settings/nextCreatePreview.ts`, takes the create's context: kind, explicit
level, the role's `GET /api/role-policy` row and the role's `GET /api/complexity-chains?role=`
effective view (which carries `defaultComplexity`, the stage pin, per-candidate `availableNow`
and the `agent` source). It mirrors `AgentTaskService.CreateAsync` order:

1. An explicit level bypasses the row (`MatrixDefaultApplies` is false; OD-2).
2. Worker only: an `agent` effective source means the stage pin binds a standing agent, which
   runs the create (`ComplexityEffective.Compose`). Otherwise, when the configured default tier's
   chain resolved (`resolvedFrom` role/any/config), the create is stamped with that tier and walks
   the effective candidates: the first `availableNow` wins and held heads are skipped
   (`ComplexityRoutingService.WalkAsync`); none available is Blocked with the head pair (OD-6).
   An effective `pin` source (Required pin with candidates) is labelled as the pin. A withheld
   row with no launchable candidate is `unknown`.
3. Otherwise (Orchestrator, no resolved row, chain read failed): the legacy path. An agent-only
   stage pin runs on the agent. A Required pin with two or more candidates, or a Preferred pin
   followed by the policy target, is walked past held candidates (`WalkComposedListAsync`, the
   CARD-0322 pin list); a single target keeps the existing `effective` projection. Orchestrator
   kind keeps the `MinOrchestratorLevel` floor (High) the modal already applied.

The Role policy section shows two lines per role in the `Effective next create` column, Worker and
Orchestrator. The delegate modal shows the preview's level and a source line (`matrix`, `pin`,
`rolePolicy`, `agent`, `exhausted`, `fallback`); a null preview level keeps CARD-1234's
`AGENT_TASK_ROLES` fallback. Docs replace the D-6 limitation paragraphs.

Residual, disclosed: availability is the server's 15-second snapshot; quota and per-card pins are
not in the preview context (the modal creates without a card); a Preferred pin's policy target is
assumed available because the role-policy route evaluates no availability for it.

## Slices

S1 tests (red first): `nextCreatePreview.test.ts` (new), cases in `RolePolicySection.test.tsx`
and `DelegateModal.test.tsx`. S2 implementation and docs.

## Ordinary verification

| ID | Witness and literal outcome |
|---|---|
| V-1 | `C1250 V1 worker-reads-default-tier-cell`: Worker Code with a Medium row → `matrix` Codex/Frontier gpt-6-astra; label `Medium row → Codex/Frontier (gpt-6-astra)`. |
| V-2 | `C1250 V2 held-head-walks-to-next-candidate`: held head → ClaudeCode/High opus, skipped `gpt-6-astra`. |
| V-3 | `C1250 V3 every-candidate-held-is-blocked`: `exhausted` with the head pair; label `every Medium-row candidate is held → Blocked until a hold clears`. |
| V-4 | `C1250 V4 non-medium-configured-default-tier`: default Easy reads the Easy chain → Codex/Low. |
| V-5 | `C1250 V5 orchestrator-ignores-the-row-and-keeps-the-floor`: Orchestrator reads the policy; Docs Medium floored to High. |
| V-6 | `C1250 V6 agent-only-stage-pin`: Worker `agent` source repeats the server sentence; Orchestrator agent-only pin → `stage pin binds a standing agent; the create runs on that agent`. |
| V-7 | `C1250 V7 explicit-level-bypasses-the-row`: `explicit Low`. |
| V-8 | `C1250 V8 <case>` (no-cells, empty-matrix, unresolved-row): policy `default → ClaudeCode/Frontier (fable)`. |
| V-9 | `C1250 V9 required-stage-pin-walks-inside-the-row`: `Required Human pin → ClaudeCode/High (opus), skipping held gpt-6-astra`. |
| V-10 | `C1250 V10 legacy-pin-list-skips-a-held-head`: both kinds skip a held pin head without a row. |
| V-11 | `C1250 V11 withheld-row-is-unknown`: null preview, `unknown`. |
| V-12 | `RolePolicySection` `C1250 V12 worker-column-reads-the-default-tier-row` (default Hard) and the corrected `C097 V25` Plan line (held head skipped). |
| V-13 | `DelegateModal` `C1250 V13` (Worker reads the row, Orchestrator Code returns to `rolePolicy` Frontier), `V14` (Easy default), `V15` (exhausted), `V16` (agent). |
| R-1 | Whole files: `rolePolicyModel.test.ts`, `RoutingSettingsTab.test.tsx`, `complexityChains.test.tsx` plus the three files above (existing C097 V05/V06/V08/V09/V16-V18/V27/V30 and C1234 cases unchanged). |
| R-2 | `tsc -b` for the client and ESLint over the changed sources. |

Expected red at S1: `nextCreatePreview.test.ts` fails to resolve its module; RolePolicySection
V12 and V25 fail on the missing `role-policy-next-*` test ids; DelegateModal V13-V16 fail on the
missing source ids. V25's earlier literal `Required Human pin → Codex/Frontier (gpt-6-astra)` was
the held-head projection this card corrects, so it is replaced by the walked literal, not loosened.

Unit lane: not run (brief: no whole-Unit lane; no server code changes). No integration class is
affected: no server source changes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/nextCreatePreview.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx` | V-1..V-13 red | expected red before S2: module resolution failure, V12/V25 and V13-V16 fail | n/a | 4 | true | n/a |
| CP-2 | S2 | n/a | client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/nextCreatePreview.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx src/features/settings/rolePolicyModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-1..V-13, R-1 | all six files, 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 5 | true | n/a |
| CP-3 | S2 | n/a | client-types | `node client/node_modules/typescript/bin/tsc -b client/tsconfig.json --pretty false` | R-2 | exit 0, 0 TypeScript errors | n/a | 3 | true | n/a |
| CP-4 | S2 | n/a | client-lint | `node client/node_modules/eslint/bin/eslint.js --config client/eslint.config.js --max-warnings 0 client/src/features/settings/nextCreatePreview.ts client/src/features/settings/nextCreatePreview.test.ts client/src/features/settings/RolePolicySection.tsx client/src/features/settings/RolePolicySection.test.tsx client/src/features/delegations/DelegateModal.tsx client/src/features/delegations/DelegateModal.test.tsx` | R-2 | 0 errors, 0 warnings | n/a | 2 | true | n/a |

### Execution and cost

Ordinary floor 14 minutes plus authoring; within the 90-minute budget. Extra disclosed drivers
through `scripts/build-slot.ps1`: `npm ci --prefix client` (fresh worktree has no
`node_modules`; one esbuild hard link re-copied afterwards) and the checkpoint tool build into
`tools/Antiphon.Checkpoints/bin-c1250-tool/`. Each group runs once with `--expected-source-sha`,
`--serial`, `--row-timeout 40m`, `--total-timeout 150m`, without `--keep-outputs`.

## Recorded runs

- CP-1 red, run `20261014-194806-ff14` at `c8d4d3a1a2fc050c5b8df33e69591f6603cb4b43` (tests and plan, no implementation):
  exit 1, 3 files failed, 6 tests failed / 26 passed; `nextCreatePreview.test.ts` failed to resolve its module;
  failed: RolePolicySection `C097 V25`, `C1250 V12`; DelegateModal `C1250 V13`–`V16`. slot=granted waited=75s.
- S2 run `20261014-195312-41fb` at `6ca07e6e055ee50e946803bf9df8f3a4c1e2f0a0`: CP-2 green, 6 files, 109 passed, 0 failed
  (Vitest prints aggregate counts only); CP-3 exit 0; CP-4 exit 2 before linting anything: ESLint 9 run from the
  repository root found no flat config. The row now passes `--config client/eslint.config.js`; no source change;
  CP-4 alone is rerun.

## Mutation handoff (unbound, all pending)

- PC-1 read the Medium chain regardless of `defaultComplexity`: V-4 and V-12/V-14 fail.
- PC-2 take the head without `availableNow`: V-2, V-9, V-10, V-12, V-13 fail.
- PC-3 apply the row to Orchestrator kind: V-5 and V-13 (Orchestrator Code `tier-Frontier`) fail.
- PC-4 drop the `agent` arm (variants Worker cell, Orchestrator stage pin): V-6, V-16 fail.
- PC-5 let the row win over an explicit level: V-7 fails.
- PC-6 treat `resolvedFrom: none` as resolved: V-8 `unresolved-row` fails.
- PC-7 drop the Orchestrator floor: V-5 Docs fails.
- PC-8 report exhausted as the head without Blocked: V-3 and V-15 fail.
