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

Client-only repair of Final Review 18cdf39f F1-F4. The server resolver remains unchanged.
The preview models a fresh create without a card, explicit agent/kind, follow-up, complexity,
model/quota bypass or refuse-if-exhausted flag. Those caller contexts are outside this UI.
It reads role policy, the active stage pin and the role's effective complexity cells.

1. Compose the stage pin without a chain first (AgentTaskService.cs:789-829 and
   RoutingCandidates.cs:47-94): fill partial pairs, narrow by explicit level, append the policy
   target for Preferred, deduplicate by kind/level. Required mismatch is 409 routing_pin_conflict.
   A one-slot composition overlays the request and bypasses the default-tier row, including
   forbid-only Preferred pins and pins equal to policy. A zero-slot Required forbid pin leaves
   the row eligible. V-17 F1/F2 and explicit/partial/dedup rows bind these branches.
2. Eligible Workers use the configured default-tier effective list when resolvedFrom is role,
   any or config. Required candidates replace the chain; Preferred candidates precede it.
   With no resolved row, a composed list of two or more is walked without a complexity stamp.
   V-1..V-4, V-8..V-10 and V-17 any/config/Preferred/Required rows cover these paths.
3. Walks filter delegate kinds (ClaudeCode only for Orchestrator), case-insensitive stage forbids,
   holds and quota, then choose the first survivor. Exhaustion becomes Blocked with the head pair
   under this UI's defaults. Walked Orchestrator levels bypass ResolveLevel and are not floored.
   V-17 F1/F3, quota and exhaustion rows plus V-19 bind the filters and level behavior.
4. Single targets apply the kind check/clamp and ResolveLevel path, then forbids, quota, holds.
   A forbidden alias is 409 routing_pin_forbidden; quota is 409 subscription_quota_low; a held
   target is refused, rather than Blocked. GET checks hold before quota while create checks quota
   before hold, so a held snapshot discloses model_disabled or subscription_quota_low instead of
   claiming an unobserved error precedence. V-17 F4 and refusal rows, V-18 bind this distinction.
5. Missing routing facts stay unknown: unevaluated policy tails or partial slots in a walk,
   withheld candidates/read errors, missing policy/default-row reads, agent details, and aliases
   absent from the observations. Single policy targets with no availability observation name the
   target with an explicit availability caveat. V-17 unknown rows and V-20 bind these boundaries.
   The DTOs omit configurable MinOrchestratorLevel. A single Orchestrator target below Frontier
   therefore reports an unknown level; the client does not infer High. Frontier needs no floor.
   V-5 and V-17 floor rows bind this; PC-7 removes this guard. Agent-only pins identify the binding
   with an admission caveat (V-6/V-16), and pins combining an agent with candidates stay unknown.

Snapshots refresh at 15-second intervals. availableNow includes the null-owner quota gate as well as
holds. The read does not reserve admission: create can encounter changed routing, concurrency,
provider authentication, workspace or host conditions. There is no client alias ladder.

The Settings column keeps Worker and Orchestrator lines. The modal shows refusal/unknown text,
hides a guessed tier for unknown routing, and uses a labelled built-in suggestion when an invalid
policy target is withheld. V25's two exact held-head labels remain; other exact legacy labels now
include missing-availability or missing-floor disclosure. V-7 retains the explicit-tier assertion.

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
| V-5 | `C1250 V5 orchestrator-ignores-the-row-and-discloses-the-floor`: Docs level is unknown because MinOrchestratorLevel is not exposed; Frontier remains Frontier. |
| V-6 | `C1250 V6 agent-only-stage-pin`: Worker and Orchestrator agent-only pins → `stage pin binds a standing agent; its model and admission depend on the server`. |
| V-7 | `C1250 V7 explicit-level-bypasses-the-row`: `explicit Low`. |
| V-8 | `C1250 V8 <case>`: no-cells is unknown; empty-matrix names the policy with an availability caveat; unresolved-row with evaluated fable names the policy. |
| V-9 | `C1250 V9 required-stage-pin-walks-inside-the-row`: `Required Human pin → ClaudeCode/High (opus), skipping held gpt-6-astra`. |
| V-10 | `C1250 V10 legacy-pin-list-skips-a-held-head`: both kinds skip a held pin head without a row. |
| V-11 | `C1250 V11 withheld-row-is-unknown`: unknown source and null target, explicit server-dependency label. |
| V-12 | `RolePolicySection` `C1250 V12 worker-column-reads-the-default-tier-row` (default Hard) and the corrected `C097 V25` Plan line (held head skipped). |
| V-13 | `DelegateModal` `C1250 V13` (Worker reads the row, Orchestrator Code returns to `rolePolicy` Frontier), `V14` (Easy default), `V15` (exhausted), `V16` (agent). |
| V-17 | 40 table-driven server-branch rows in `nextCreatePreview.test.ts`; literal target/source/refusal assertions and server citations. |
| V-18 | Modal held single pin is refused, not Blocked; exact 409 label. |
| V-19 | Modal walked Orchestrator pin retains Medium and does not show High. |
| V-20 | Modal unknown Preferred policy tail has the exact dependency label and no Frontier badge. |
| R-1 | Whole files: `rolePolicyModel.test.ts`, `RoutingSettingsTab.test.tsx`, `complexityChains.test.tsx` plus the three files above (C097 and C1234 regression cases retained; V17 now uses a real pin DTO, V18/V27 require the exact unknown labels, V25 awaits the second query with the same exact pin assertions). |
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
| CP-1 | Repair-tests | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/nextCreatePreview.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx` | V-17 F1-F4 red | expected assertion failures against f047d2ef7 implementation | n/a | 4 | true | n/a |
| CP-2 | Repair | n/a | client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/nextCreatePreview.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx src/features/settings/rolePolicyModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx -JsonResultPath ../.antiphon/c1250-repair-client.json` | V-1..V-20, R-1 | all six files, 152 passed, 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 5 | true | n/a |
| CP-3 | Repair | n/a | client-types | `node client/node_modules/typescript/bin/tsc -b client/tsconfig.json --pretty false` | R-2 | exit 0, 0 TypeScript errors | n/a | 3 | true | n/a |
| CP-4 | Repair | n/a | client-lint | `node client/node_modules/eslint/bin/eslint.js --config client/eslint.config.js --max-warnings 0 client/src/features/settings/nextCreatePreview.ts client/src/features/settings/nextCreatePreview.test.ts client/src/features/settings/RolePolicySection.tsx client/src/features/settings/RolePolicySection.test.tsx client/src/features/delegations/DelegateModal.tsx client/src/features/delegations/DelegateModal.test.tsx` | R-2 | 0 errors, 0 warnings | n/a | 2 | true | n/a |

### Execution and cost

Repair ordinary floor 14 minutes plus authoring, within the 75-minute brief budget. Extra disclosed drivers
through `scripts/build-slot.ps1`: `npm ci --prefix client` (fresh worktree has no
`node_modules`; one esbuild hard link re-copied afterwards) and the checkpoint tool build into
`tools/Antiphon.Checkpoints/bin-c1250-repair-tool/`. Import the plan, set generated manifest `timeouts.rowMinutes: 40`; each group runs once with `--expected-source-sha`,
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
- PC-7 drop the unknown-floor guard on the single-target Orchestrator path: V-5 Docs and V-17 configured-floor fail. The server floor is not in these DTOs.
- PC-8 report exhausted as the head without Blocked: V-3 and V-15 fail.

## Repair 679ecbea (Final Review 18cdf39f F1-F4)

V-17 is a table of 40 literal server-branch expectations, with f047d2ef7 server file:line citations in each row. CP-1 runs these regressions against the unchanged f047d2ef7 implementation before the repair. CP-2 runs the complete six-file selection after the repair; Min stays n/a because these are Vitest results. Configured Orchestrator floor, unevaluated partial-slot availability, standing-agent details and masked quota-versus-hold precedence must be disclosed as unknown rather than invented. Final scope remains client-only: no whole Unit or server integration lane (explicit repair brief).

V-18..V-20 bind modal refusal, unfloored walked Orchestrator level and unknown policy-tail availability. They join CP-1 red and CP-2 green.

### Repair branch inventory and mutation variants

V-17 records 40 server branches with kind/level, matrix, pin strength/candidates/forbids,
availability/quota and literal outcomes in each fixture. V-1..V-16 cover the default-tier choice,
held walks, explicit level, empty/unresolved rows, and rendered UI assertions. The matrix read
uses the server's resolvedFrom/active-pin projection; expiration/NotBefore do not become client
clock decisions. Stage forbid exemptions require a Human card pin, outside this card-free context.
Explicit kind/agent, live follow-up, specialist seat ownership, explicit complexity, bypass flags,
RefuseIfExhausted and card-pin precedence require server context and are not inferred here.

Pending additional PCs (method-scoped by Mutation; no mutants run by Code):
- PC-9 omit stage forbid filtering: V-17 F1 required-forbid-only and legacy-forbidden-head (two variants).
- PC-10 skip no-chain composition or omit pair deduplication: V-17 F2 preferred-forbid-only-refusal / preferred-dedup-held / required-dedup-single (three variants).
- PC-11 omit the Orchestrator walk kind filter: V-17 F3 orchestrator-kind-filter.
- PC-12 apply a floor to a walked list: V-17 F3 walked-medium-not-floored and V-19.
- PC-13 treat a held single target as exhausted: V-17 F4 single-required-held and V-18.
- PC-14 assume unknown policy-tail availability is true: V-17 preferred-policy-tail-unknown and V-20.
- PC-15 ignore quota in availability: V-17 matrix-quota-skip / required-single-quota (two variants).
- PC-16 skip explicit pin narrowing/conflict: V-17 explicit-required-conflict / explicit-required-narrowed-single (two variants).

CP-1 repair red: run 20261014-201811-31b2 at 8e9118305f5076fa3332d5c32b543d6a16a9cdc1
contains tests/docs only over f047d2ef7. 38 failed / 50 passed in three files; F1-F4 and V-18..V-20
are red assertions, not compile errors. slot=granted waited=0s; clean source, buildSource=notApplicable.
Detailed final receipt facts are in the task report, not relabelled onto later documentation commits.

Qualification run 20261014-202401-951f at ab320b74ca4b6cd5653f094b85544678461ea72f: CP-2 151 passed / 1 failed (V25 read the transient unknown before the matrix query completed); CP-3 two TypeScript errors in the new test helper (held reason inferred as null-only); CP-4 green. The repair awaits the same exact rendered V25 value and types the helper as RoutingPinCandidateDto. No assertion or timeout was relaxed. CP-2/CP-3 are rerun; CP-4 accompanies the changed TypeScript test files for final SHA qualification.

Unbound Mutation candidate inventory: [PC-1..PC-16 and variants](../mutations/2026-10-14-card-1250-preview-parity-candidates.md). The candidate/plan-link commit is documentation-only after the final green run at 4eb039453416c138f149a187274ef1e30f6a8aed; it does not change the tested client sources or checkpoint selection.
