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

Client-only repair of Final Reviews 18cdf39f F1-F4 and 9133fca0 D1-D2. The server resolver remains unchanged.
The preview models a fresh create without a card, explicit agent/kind, follow-up, complexity,
model/quota bypass or refuse-if-exhausted flag. Settings uses that context. The modal
withholds its target for a possible card binding in the derived title or card context from Hand back,
because card pins are absent from these reads (V-22/V-23). Identifier boundaries are exact for ASCII
neighbours; a non-ASCII neighbour counts as a possible boundary because browser and server Unicode
tables can differ (V-22 Unicode drift and ASCII controls). Its selected level and shape are known.
It reads role policy, the active stage pin and the role's effective complexity cells.

1. Compose the stage pin without a chain first (AgentTaskService.cs:789-833 and
   RoutingCandidates.cs:44-96): fill partial pairs, narrow by explicit level, append the policy
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
   except when a Required pin leaves one effective candidate. That branch applies single-target
   refusal checks (AgentTaskService.cs:995-1034), including forbid-only pins with role/any/config
   rows (V-17 D1 and V-21). An available single candidate still wins; without a Required pin,
   a single exhausted row still becomes Blocked. Walked Orchestrator levels bypass ResolveLevel.
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
| V-17 | 58 table-driven server-branch rows in `nextCreatePreview.test.ts`; literal target/source/refusal assertions and server citations. |
| V-18 | Modal held single pin is refused, not Blocked; exact 409 label. |
| V-19 | Modal walked Orchestrator pin retains Medium and does not show High. |
| V-20 | Modal unknown Preferred policy tail has the exact dependency label and no Frontier badge. |
| V-21 | Modal Required forbid-only single-row held/quota/forbidden each shows the exact refusal and no Blocked result. |
| V-22 | 72 create-context rows, including ASCII boundaries, conservative Unicode boundaries and unobserved request inputs. |
| V-23 | Modal Hand back, typed card/forbid/explicit-tier, and card-free edit controls. |
| V-24 | Modal admission dependency remains visible with explicit tier. |
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
| CP-1 | Repair-tests | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/nextCreatePreview.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx` | V-22..V-24 D1 and boundary red | expected assertion failures at the matching pre-fix SHA | n/a | 4 | true | n/a |
| CP-2 | Repair | n/a | client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/nextCreatePreview.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx src/features/settings/rolePolicyModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx -JsonResultPath ../.antiphon/c1250-repair-client.json` | V-1..V-24, R-1 | six files, 249 passed, 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 5 | true | n/a |
| CP-3 | Repair | n/a | client-types | `node client/node_modules/typescript/bin/tsc -b client/tsconfig.json --pretty false` | R-2 | exit 0, 0 TypeScript errors | n/a | 3 | true | n/a |
| CP-4 | Repair | n/a | client-lint | `node client/node_modules/eslint/bin/eslint.js --config client/eslint.config.js --max-warnings 0 client/src/features/settings/nextCreatePreview.ts client/src/features/settings/nextCreatePreview.test.ts client/src/features/settings/RolePolicySection.tsx client/src/features/settings/RolePolicySection.test.tsx client/src/features/delegations/DelegateModal.tsx client/src/features/delegations/DelegateModal.test.tsx client/src/features/thread/CardThreadPanel.tsx` | R-2 | 0 errors, 0 warnings | n/a | 2 | true | n/a |

### Execution and cost

Round-4 ordinary estimate: 14 minutes plus authoring, within the 60-minute repair brief budget. Extra disclosed drivers
through `scripts/build-slot.ps1`: `npm ci --prefix client` (fresh worktree has no
`node_modules`; one esbuild hard link re-copied afterwards) and the checkpoint tool build into
`tools/Antiphon.Checkpoints/bin-c1250-r4-tool/`, and the required esbuild/Node differential
probe in `.antiphon/c1250-r4/` (GET-only snapshots and assertions, no task creation). Import the plan, set generated manifest `timeouts.rowMinutes: 40`; each group runs once with `--expected-source-sha`,
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

The round-2 V-17 selection was a table of 40 literal server-branch expectations, with f047d2ef7 server file:line citations. Round 3 reopens and corrects those citations below. CP-1 runs these regressions against the unchanged f047d2ef7 implementation before the repair. CP-2 runs the complete six-file selection after the repair; Min stays n/a because these are Vitest results. Configured Orchestrator floor, unevaluated partial-slot availability, standing-agent details and masked quota-versus-hold precedence must be disclosed as unknown rather than invented. Final scope remains client-only: no whole Unit or server integration lane (explicit repair brief).

V-18..V-20 bind modal refusal, unfloored walked Orchestrator level and unknown policy-tail availability. They join CP-1 red and CP-2 green.

### Repair branch inventory and mutation variants

The round-2 V-17 selection recorded 40 server branches with kind/level, matrix, pin strength/candidates/forbids,
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

## Repair 63109ffc: create outcome inventory (before implementation)

Round-3 model scope (extended by round-4 card detection below): a fresh card-free create with no explicit kind/agent,
follow-up, complexity, bypass flag or refuse-if-exhausted flag. The modal can specify a level.
Both Worker and Orchestrator pass through the following routing decisions. This inventory
was written before changing the preview; server source is the task's starting 805d4a6d3 commit.

| Server outcome / condition | Worker preview | Orchestrator preview | Observable boundary / witness |
|---|---|---|---|
| Required pin disagrees with an explicit level: 409 `routing_pin_conflict` | `refused` | `refused` | V-17 explicit-required-conflict; RoutingPinService.ResolveAsync |
| Single resolved alias forbidden by stage pin: 409 `routing_pin_forbidden` | `refused` | `refused` when target/floor is known | V-17 single-forbidden, Preferred forbid-only, D1 forbidden; RoutingPinService.EnforceForbiddenAliases |
| Single target quota gate: 409 `subscription_quota_low` | `refused` | same for a known target | V-17 required-single-quota and D1 quota; AgentTaskService.CreateAsync quota calls |
| Single target hold: 409 `model_disabled` | `refused`, with quota precedence disclosed | same for a known target | V-17 F4, legacy-policy-held, D1 held; GET checks holds before quota, so the precise code can be masked |
| Unsupported explicit/overlaid delegate kind, or a non-ClaudeCode single Orchestrator kind: 422 `validation_failed` | unsupported target is withheld/unknown by the reads | `refused` for the observed non-ClaudeCode overlay | V-17 orchestrator-explicit-pin-kind-refused; ResolveAgentKind |
| Invalid policy level: 422 `role_policy_level_invalid` | withheld target or `unknown` policy target | same | V-17 role_policy_level_invalid rows use an unresolved row; the read withholds the alias. A complete row/pin can bypass policy resolution, so this is not a blanket refusal |
| Unsupported policy kind: 422 `role_policy_kind_unsupported` | withheld target or `unknown` policy target | same, before kind clamp | V-17 role_policy_kind_unsupported rows; RolePolicyReadService.PolicyTarget withholds the pair. Undefined kinds and unset kinds share null, so raw rejected configuration is unavailable |
| Resolved default-tier row chooses a survivor | `matrix` or `pin`, observed ClaudeCode/Codex/Grok pair and tier | row ineligible | V-1..V-4, V-9, V-17 any/config/Grok; MatrixDefaultApplies and WalkAsync |
| Required forbid-only pin, one effective candidate, no survivor | single-target 409 semantics above | row ineligible | D1: CreateAsync's Required + !Walked branch; role/any/config each need held/quota/forbidden witnesses |
| Default-tier row has no survivor, without that Required-single condition | `exhausted`, head retained, Blocked | row ineligible | V-3, V-15, V-17 no-pin-single-held and matrix exhaustion |
| Two-or-more composed pin candidates, no applicable row | first eligible ClaudeCode/Codex/Grok pair; or Blocked at the head | first eligible ClaudeCode pair at its listed level, or Blocked | V-10, V-17 F3/exhaustion, V-19; WalkComposedListAsync does not apply ResolveLevel |
| One composed candidate | `pin`/`rolePolicy`, row bypassed; then single gates | explicit non-ClaudeCode refused; ClaudeCode level depends on configured floor below Frontier | V-17 F2/dedup/single, V-5; ResolveLevel/ResolveAgentKind |
| No resolved row or composed list | policy target with single gates | policy kind clamped to ClaudeCode; configured floor may change level | V-8, V-17 policy clamp/floor; alias/availability absent from reads stays disclosed |
| Explicit level without pin | `explicit <level>`; kind/alias/admission still server-dependent | same for Frontier, unknown floor below it | V-7 and V-17 explicit-floor; this label does not predict single-target admission |
| Standing-agent pin | agent model/owner quota/admission depend on server | same | V-6/V-16; mixed agent/candidate pins unknown (V-17) |

Server-only create refusals are outside the routing snapshots. The Settings copy and modal admission note
disclose server dependency (modal V-24); these previews do not predict these codes:

- 409 `provider_sign_in_required`: Grok local/remote or remote Codex login probe; selected
  runner, provider home and credentials are not in the two routing reads (CreateAsync provider
  probes and RefuseUnauthenticatedGrokAsync/RefuseUnauthenticatedRunnerCodexAsync).
- 409 `concurrency_limit`: DelegationOpenGate count/insert admission, including after routing
  exhaustion. The preview holds no admission reservation.
- 409 `conflict`: depth, root task count/cost ceiling, or standing-agent kind mismatch.
- 409 `workspace_reserved`: WorkspaceUseAdmission retirement reservation.
- 422 `stage_tool_bundle_unknown`: InstructionBundles.ResolveRoleToolBundles validates
  configured role tool bundles, which the routing reads omit.
- 422 `workspace_default_not_git`, `workspace_existing_agent_conflict`, `llm_project_required`
  and generic `validation_failed`: checkout, workspace, launch environment, request shape or
  runner admission. Those inputs are outside NextCreateContext.
- 409 `runner_platform_mismatch`, `runner_platform_unknown`, `runner_platform_unavailable`
  and 422 `runner_platform_requirement_invalid`: a supplied/card platform and live runner
  evidence, outside this card-free routing projection. Other runner-platform constants govern
  dispatch/defaults mutation, not this fresh create path.

Additional modes are excluded by the stated context: 409 `routing_exhausted` requires
`RefuseIfExhausted`; follow-up refusals (`follow_up_agent_blocked`,
`follow_up_remote_pool_unsupported`, `follow_up_source_unavailable`), repair/base/start-ref,
Interim verification and SourceLanding admission require their respective request fields.
Goal/title card binding can introduce a card pin/platform, so the final create remains authoritative.
Successful create also includes Blocked for repeated launch failure history; that history is
not in the reads. The selected target may subsequently wait for dispatch or fail admission.

Read failures and withheld partial candidates are `unknown`, rather than a guessed survivor.
The differential acceptance uses the built preview with literal new-case oracles and the live
GET role-policy / complexity-chains reads. It creates no tasks. Mutation owns deliberate mutants.

### Round-3 qualification and citation audit

CP-1 at ddecbdf25f94d024fe0be6d13e2b3ce0efe230ec (unchanged preview implementation):
14 failed / 95 passed across three files. Nine D1 rows returned exhausted instead of refused;
three V-21 modal rows found no refusal label; two withheld Orchestrator policy rows lacked
the explicit server-dependency label. Run 20261014-205413-2fad, slot=granted waited=0s,
sourceState=clean, buildSource=notApplicable. CP-2 includes these cases after the fix.

V-17 now has 58 rows, including the nine D1 refusal variants, three available-row controls,
a no-pin single-held Blocked control, a Grok target and four invalid-policy disclosure rows.
CP-2 expects 173 Vitest cases across six complete files; Min remains n/a because the checkpoint
Min column counts TUnit executions. CP-3/CP-4 retain TypeScript and zero-warning ESLint.

The source audit reopened 57 pre-existing file:line citation occurrences: 52 in V-17, two in
this plan and three in the owner docs (the API/HTTP docs have no file:line citations).
Corrections include the five D2 rows, Preferred available dedup, missing Orchestrator alias,
partial-pair filling, missing-policy forbid, and tighter ranges for overlay, filtering,
withholding, row sources and read failures. These are source-reading results, not test results.
The final audit reopened 94 file:line occurrences (54 distinct references): 88 in V-17,
three in this plan and three in the owner docs. The stored round-3 report points to the
source excerpts, including the references introduced for the new rows.

Additional pending Mutation candidates: PC-17 Required-single-row refusal (held, quota, forbidden
for role/any/config, with separate modal variants); PC-18 overbroad Required-single guard
(available row and no-pin exhaustion controls); PC-19 withheld policy dependency label.
No deliberate mutants were introduced by Code. See the unbound candidate inventory.

CP-2/CP-3/CP-4 passed at 79a8cdb7f192d6a47af674bd1c9fbc6945d2505f
(run 20261014-205737-717e): 173 passed, tsc exit 0, ESLint exit 0. Receipt inspection
found Vitest truncated the four config-case names to one identity. The follow-up change
registers the same table with literal titles (same assertions/count), so method-scoped Mutation
can distinguish them. CP-2/CP-3/CP-4 requalify that test-source change; this is not repeat proof.

## Repair a1ba464f: D1 context inventory before implementation

Choice: use the conservative unknown rendering for potential card bindings, with no target badge.
No card-pin query is added. This is a client-only Final repair; the brief excludes whole Unit.

| Create input / missing fact | Modal boundary and parity witness | Server source opened for this repair |
|---|---|---|
| Goal/title card reference | V-22 leading/interior/case/boundary/title-clamp rows: unknown; bare #N in prose does not bind | AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4535 |
| Explicit -Card, including #N and GUID | Not a modal field; V-22 explicit-card contexts are unknown | AgentTaskCardBinder.cs:94-95,127-154 |
| Card-origin/inherited binding | V-22 card-context and V-23 edited Hand back goal retain unknown | AgentTaskService.cs:707-719 |
| Card pin before stage pin; Human card forbid exemption | V-22 card-over-stage/card-forbid rows withhold the staged target/refusal | RoutingPinService.cs:306-308,413-418 |
| Agent-only pin and standing-agent arm | V-22 agent-only/mixed rows: model, owner quota and admission unknown | AgentTaskService.cs:840-861,1140-1144 |
| Quota/holds at admission | V-22 quota read/unknown rows; V-24 visible admission disclosure | AgentTaskService.cs:1117-1144 |
| Explicit provider -Kind | Not a modal field; V-22 explicit-kind context is unknown | AgentTaskService.cs:924-945,4169-4204 |
| Explicit -Level | Known modal selection; V-22 explicit-level keeps requested tier and V-24 discloses server routing/admission | AgentTaskService.cs:4117-4135 |
| Orchestrator floor | Missing from reads; V-22 floor row stays unknown below Frontier | AgentTaskService.cs:4132-4133 |
| Worker versus Orchestrator | Known modal selection; V-22 paired rows and retained V-13 cover row eligibility | AgentTaskService.cs:924-945,4190-4204 |
| Provider login, concurrency, workspace, runner/platform and changed snapshots | V-24 visible server-admission disclosure; precise refusal not predicted | Existing round-3 inventory above |

V-22 extends the parity table without changing the earlier 58 contexts or exact assertions.
V-23 exercises the Hand back prefill, the card-origin hint after goal edits, and a card-free goal.
V-24 checks the visible server dependency disclosure, including an explicit tier.
R-1/R-2 remain the six complete client files, TypeScript and ESLint. CP-1 qualifies D1 red before
implementation; CP-2/CP-3/CP-4 qualify the repair. Min is n/a for these non-TUnit rows; Expect
records the Vitest count. Additional drivers: dependency install, one isolated checkpoint tool
build, and required esbuild/Node differential probe (GET only), each through the build-slot gate.
No Mutation run: PC-1..PC-19 and the new card-context/goal-detection/disclosure variants remain pending.

Round-4 CP-1: run 20261014-213344-c997 at 658bed58f53cf66e8b24fd20113be04c069e04fa
finished with 20 assertion failures / 131 passes in three files. Sixteen V-22 card/explicit-context
rows and the four V-23/V-24 modal cases failed against unchanged df244bfb implementation.
slot=granted waited=0s; sourceState=clean, buildSource=notApplicable. Earlier exact assertions
including C097 V25 are unchanged. CP-2 expects 215 cases, including 58 V-17 and 38 V-22 rows.

Differential probe at d2c6a5a90157065e692b04fc83ff7580c214bbbb: the retained 58 and
new 38 literal rows passed, but 202/1224 direct .NET title/binder comparisons disagreed on
supplementary characters or join controls. Eight boundary regression rows are added before
the matcher correction. The source audit also reopened MatrixDefaultApplies
(AgentTaskService.cs:3740-3744): specialists bypass the default row. The three new specialist
expectations are corrected to policy; these were outside the earlier 58-row table.
The first CP-2 passed 215/215 and CP-3 passed; CP-4 rejected the new whitespace range under
no-control-regex. Use named whitespace escapes without disabling the lint rule.
Final CP-2 expects 223 cases, V22=46; the earlier V17 and V25 assertions remain unchanged.

Boundary CP-1 at 5e6b2622798d1170e773e346999495b6fca3d84d (run 20261014-214019-a515):
10 failed / 149 passed in three files. Seven Unicode boundary assertions and the three
specialist eligibility assertions were red. slot=granted waited=0s, sourceState=clean,
buildSource=notApplicable. The matcher now classifies adjacent UTF-16 units, including
join controls, and the matrix eligibility excludes specialist roles.

## Repair 2da9a077: Unicode boundary drift (Review eaee1cf7 D1)

Round 5 uses exact ASCII neighbour rules and conservative non-ASCII boundaries.
The server uses its runtime Unicode tables in the regex and Int32 parsing
(AgentTaskCardBinder.cs:44-64); title derivation and the UTF-16 clamp remain
AgentTaskService.cs:4519-4535,4546-4547. These source lines were reopened for this repair.

V-22 adds U+A7CB, U+1C89 and U+1AD0 in five positions: prefix, suffix, in-word
prefix, in-digits and in-word suffix. U+10400 uses the same five positions.
Those 20 rows require the exact unknown card-pin label and null target fields.
Six ASCII letter/digit/underscore prefix/suffix controls require the exact matrix target.
The suffix digit control includes a trailing ASCII letter to block the longer numeric identifier.
V-17 and C097 V25 retain their exact assertions.

| Changed V-22 row | Round-5 result and reason |
|---|---|
| goal-unicode-prefix-boundary (é) | unknown: a non-ASCII neighbour no longer relies on browser Unicode tables |
| goal-nonjoiner-prefix (U+200C) | unknown: conservative non-ASCII prefix rule, though .NET 9 blocks this boundary |
| goal-joiner-prefix (U+200D) | unknown: conservative non-ASCII prefix rule, though .NET 9 blocks this boundary |
| goal-nonjoiner-suffix (U+200C) | unknown: conservative non-ASCII suffix rule, though .NET 9 blocks this boundary |
| goal-joiner-suffix (U+200D) | unknown: conservative non-ASCII suffix rule, though .NET 9 blocks this boundary |
| goal-unparseable-decimal (CARD-12١) | unknown: the ASCII numeric prefix has a non-ASCII neighbour; .NET 9 rejects the full decimal token |

CP-1 demonstrates the new assertions against the unchanged implementation before the fix.
CP-2 requires six files and 249 passed cases (V-22=72); Min remains n/a because
command rows use fresh Vitest JSON, not a TUnit execution count. CP-3 and CP-4
remain TypeScript and ESLint. No whole Unit or server integration class is in scope.
Import the manifest with rowMinutes 40 and run with --total-timeout 150m, without
--keep-outputs. Extra slot-gated drivers: dependency installation, one isolated
checkpoint-tool build, and the required esbuild/Node GET-only differential probe.
The probe retains the old rows, adds the new rows, and separates conservative unknowns
from fail-open mismatches against a .NET 9 oracle. No create calls. Budget: 40 minutes.

Round-5 CP-1 at 34f5d01089b9f6fc02f756035e5acbc5773382b2, run
20261014-220457-2313: 21 assertion failures / 164 passes across three files.
The 15 new BMP drift cases and six revised non-ASCII expectations fail against the
unchanged implementation; supplementary and ASCII controls pass. No compile/fixture errors.
slot=granted waited=0s, sourceState=clean, buildSource=notApplicable.
PC-1..PC-25 and their variants remain pending SourceLanding Mutation; PC-23 join-control
variants now guard against treating those non-ASCII neighbours as definite blockers,
and PC-25 guards the browser Unicode-table dependency. See the candidate inventory.
