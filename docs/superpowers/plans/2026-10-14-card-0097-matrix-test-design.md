# CARD-0097 verification design amendment — the matrix as the single routing surface

Plan task `c491a627` (Frontier) wrote this amendment with [the rework plan](2026-10-14-card-0097-matrix-plan.md)
(same commit, branch `feat/card-task-c491a627`, base `054795510c1249ffbf65c552ac4e6d3e87defb99`). Source census
2026-10-14, read from source only: no build, no test run, no discovery execution. It amends
[the 2026-10-13 test design](2026-10-13-card-0097-test-design.md): the read slices it froze (V-1–V-10, V-16–V-30, PC-1–PC-27)
stand and are landed; its Appendix A (the held S3 pin editor, V-11–V-15, R-3, CP-5) is **withdrawn** and replaced by
V-C7–V-C14 below. IDs here are prefixed by slice (V-A1, V-B1, V-C1, V-F1; R-A1 …; PC-1 … PC-27 of the plan's table)
and do not collide with the 2026-10-13 numbering.

**Operator decisions applied (plan §2).** OD-1 default tier `Medium`; OD-2 cells warn, pins refuse; OD-3 pins are
migrated and cleared only by the operator's command; OD-4 last-writer-wins stated and tested (V-B6); OD-5 card pins
read-only with a per-row count; OD-6 exhausted stamped create is Blocked; OD-7 orchestrator creates are not stamped.
Post-land Mutation is paused: PC-1–PC-27 are specified and pending; none is claimed run.

**Slice order and sizing.** S-A (75 min) ∥ S-B (60) → S-C1 (60) ∥ S-D (45) ∥ S-F (75) → S-C2 (90) → S-E script (60).
S-C1 depends on S-B's wire contract (F-A); S-C2 on S-C1's `MatrixCell`; S-D's `get` on F-A; S-F on S-A's
`DefaultComplexity` and S-B's `GetEffectiveAsync`. Each slice commits and pushes before its checkpoint rows run.
S-D and S-E have no automated row (M-2, M-3 are transcripts in the Code report).

**Collisions (read 2026-10-14 ~06:25Z).** `GET /api/agent-tasks/pipeline?unscoped=true`: 0 in flight, queued or ready
in all 14 stages. CARD-0506 S1–S4 are on master and S5 is `tests/Antiphon.E2E` only; no file here overlaps. CARD-0097
S1/S2/S4/S5 are on master; S-C2 edits `RolePolicySection.tsx` (Configure button, one sentence) and S-C1 edits
`client/src/test/mocks/handlers.ts` (one field on the complexity-chains handler at `:76-82`). The orchestrator re-reads
the pipeline route before dispatching S-C1/S-C2 (same-area rule for `client/src/features/settings`).

**Ground truth confirmed from source.** `CreateAsync` opens the walk only on `request.Complexity`
(`server/Application/Services/AgentTaskService.cs:921-932`) and otherwise resolves through `ResolveLevel`/`ResolveAgentKind`
(`:1068-1072`, `:3976-3986`, `:4004-4040`); the stored row carries `Complexity`, `RoutingPinId`, `ExplicitAgentKind`,
`ExplicitModelLevel` (`:1364-1367`); an exhausted walk Blocks (`:1448-1452`); the Created detail says `(explicit
override)` or `(role policy)` (`:1554-1560`); `CreateCommitTaskAsync(settled, reason, dirtyPaths, headSha, stderr, ct)`
is `internal` and already called from `CommitOnSettlePolicyTests.cs:194` (the server exposes internals to
`Antiphon.Tests`, `server/Antiphon.Server.csproj:12`). `LoadChainAsync` returns `ResolvedFrom` `role`/`any`/`config`/`none`
(`ComplexityRoutingService.cs:312-344`); `WalkAsync` composes the pin decision with the chain through
`RoutingCandidates.Compose` (`:212-219`; `RoutingCandidates.cs:44-97`); `ResolveAgainstRolePolicy` has no orchestrator
floor (`:432-446`) while `ResolveLevel` clamps to `MinOrchestratorLevel` (default `High`, `DelegationSettings.cs:967`).
`ComplexityChainService` is constructed with `(db, time, routing, options, logger)` in three test files
(`ComplexityChainServiceTests.cs:303-312`, `ComplexityChainRoleTests`, `MutationAdmissionTests`); the HTTP factory is
`ComplexityChainApiWebAppFactory : AntiphonWebAppFactory` (`ComplexityChainHttpTests.cs:142`), `[NotInParallel]`,
`PerClass`. `RoutingPinService.ResolveAsync(null, role, Ask(null,null,null,false))` yields the stage-pin decision
(`RoutingPinService.cs:303-410`); `RoutingPinRefDto` exists (`RoutingPinDtos.cs:77-88`). The renderer asserts section
headings literally (`OrchestratorInstructionsRendererTests.cs:34-62`), `Covered_writes_each_regenerate_and_notify` takes
six `[Arguments]` (`OrchestratorInstructionsRefreshTests.cs:60-79`) through `CoveredWriteAsync` (`:351-392`), and
`BridgeQueueHarness` registers `ModelAvailability` but neither chain service (`TestHelpers/BridgeQueueHarness.cs:173`).
Client: `MatrixCell` badge from `effectiveResolvedFrom` → `resolvedFromLabel` (`RoutingSettingsTab.tsx:517-531`;
`routingSettingsModel.ts:32-47`); pin banner `pinEffectCopy` (`:113-119`); `serveRouting`, `chain()`, `pin()`,
`planEffective`, `codeEffective`, `defaultPins` fixtures (`RoutingSettingsTab.test.tsx:50-190`); msw
`onUnhandledRequest: 'error'`. Vitest counts today: `RoutingSettingsTab.test.tsx` 16, `routingSettingsModel.test.ts` 9,
`ComplexityChainCellEditor.test.tsx` 10, `RolePolicySection.test.tsx` 2, `complexityChains.test.tsx` 7,
`rolePolicyModel.test.ts` 3 `it.each` blocks (expanded count as printed).

## Verification design

### Inspection

| Bodies read | Boundary → V/R or exclusion |
|---|---|
| `AgentTaskService.cs:395`, `:485`, `:700-860`, `:900-1200`, `:1355-1375`, `:1430-1460`, `:1540-1575`, `:2800-2820`, `:3811-3860`, `:3970-4045`, `:4250-4294` | stamp point, exclusions, Blocked path, event text, commit delegate → V-A1–V-A8, R-A1–R-A4 |
| `DelegationSettings.cs:340-430`, `:967`, `:1252-1365` | new setting and validator row → V-A9 |
| `ComplexityRoutingService.cs:20-90`, `:150-250`, `:300-350`, `:425-450`, `:480-530` | load/compose/walk, skips, sentences → V-A3, V-B1–V-B4 |
| `RoutingCandidates.cs` (all) | pure precedence reused by S-B → V-B1–V-B3 |
| `ComplexityChainService.cs` (all), `ComplexityChainDtos.cs` (all), `ComplexityChainEndpoints.cs` (all) | DTO shape, `?role=` view, last-writer-wins → F-A, V-B5, V-B6, R-B1, R-B2 |
| `RoutingPinService.cs:36-49`, `:125-160`, `:273-291`, `:300-420`, `:480-500`; `RoutingPinDtos.cs`; `RoutingPinEndpoints.cs` | stage decision, DELETE by id, signal → V-A5, V-B5, V-C9, V-F2 |
| `AgentTaskDispatcher.cs:700-750`, `:1680-1800` | re-walk is keyed on `task.Complexity` → R-A4 |
| `AttentionService.cs:456-627` | stamped tasks group under `chain:<Role>/<tier>` → noted, not re-tested (existing `ComplexityAttentionTests`) |
| `OrchestratorInstructionsSnapshot.cs`, `SnapshotBuilder.cs:1-60`, `:180-260`, `Renderer.cs:1-230`, `Delta.cs:20-26`, `Signal.cs` | section, signal, delta → V-F1–V-F3 |
| `tests/…/ComplexityCreateTests.cs:1-24`, `:161-282`, `:470-496`; `RoutingPinCandidateCreateTests.cs:1-70`, `:520-632`; `ComplexityDispatcherTests.cs:1-27`, `:227-269`, `:336-520`; `ComplexityChainRoleHttpTests.cs:1-24`, `:91-131`; `RolePolicyEndpointTests.cs:1-30`; `ComplexityRoutingComposeTests.cs` titles; `OrchestratorInstructionsRendererTests.cs:34-66`; `OrchestratorInstructionsRefreshTests.cs:60-79`, `:351-392`; `OrchestratorInstructionsDeltaTests.cs` titles | fixtures and recipes (isolated schema, `Service(db, workspace)`, `JsonElement` oracles, hand-built `Decision`) → every V |
| `client/src/api/complexityChains.ts`, `routingPins.ts`, `rolePolicy.ts`; `features/settings/RoutingSettingsTab.tsx:60-90`, `:285-620`; `routingSettingsModel.ts`; `ComplexityChainCellEditor.tsx`; `RolePolicySection.tsx`; `rolePolicyModel.ts`; `test/mocks/handlers.ts:70-90`; the five test files above | types, cell, editor, section, fixtures → F-A client side, F-B, V-C1–V-C14, R-C1, R-C2 |
| `scripts/complexity-chain.ps1` (all), `scripts/routing-pin.ps1:1-60`, `:240-300`, `scripts/delegate.ps1:241`, `:1007-1010`, `:1177`, `:1299-1333` | script verbs, pin clear by grain, `-Pin` card pin → M-2, M-3 |
| `docs/orchestration-loop.md:870-1000`, `docs/antiphon-api.md:640-720`, `docs/ops-http.md:130-145`, `docs/testing-and-build.md:304-474` | doc targets and manifest schema → M-1, Checkpoints |

#### F-A Frozen wire contract for the effective cell (S-B; binds S-C1, S-C2, S-D and the msw fixtures)

Property names camelCase; enums as strings; nullable fields present explicitly.

```
ComplexityChainListDto { chains: ComplexityChainDto[], roles: AgentTaskRole[], complexities: string[],
                         defaultComplexity: 'Hard' | 'Medium' | 'Easy' }        // NEW, on both views
ComplexityChainDto     { …unchanged fields…, effective: ComplexityEffectiveDto | null }
                       // non-null ONLY on GET /api/complexity-chains?role=<Role>; null on the list view and on any-role rows
ComplexityEffectiveDto { source: 'pin' | 'pin+chain' | 'role' | 'any' | 'config' | 'rolePolicy',
                         candidates: ComplexityCandidateDto[],   // ordered; what WalkAsync would try, with availability
                         stagePin: RoutingPinRefDto | null,      // the active STAGE pin composed in (never a card pin)
                         governsDefault: boolean }               // complexity == defaultComplexity
```

`source` is a fail-closed whitelist (hand-written expected rows; V-B1–V-B5):

| stage pin | chain (`LoadChainAsync`) | composed by `RoutingCandidates.Compose` | effective |
|---|---|---|---|
| Required with ≥1 candidate | any | pin slots only | `pin`, pin candidates, `stagePin` set |
| Preferred with ≥1 candidate | non-empty | pin slots then chain, deduplicated | `pin+chain`, that list, `stagePin` set |
| Preferred with ≥1 candidate | empty (`none`) | pin slots then the RolePolicy pair (`Compose` appends it when `chain` is null; S-B passes null for `none`) | `pin+chain`, `stagePin` set |
| Required with no candidate (forbid-only / agent-only) | non-empty | chain only | chain's `resolvedFrom` (`role`/`any`/`config`), `stagePin` set |
| none | non-empty | chain | `role`/`any`/`config`, `stagePin` null |
| none | empty | empty → RolePolicy pair via `ResolveAgainstRolePolicy(Worker, role, null, null)` | `rolePolicy`, one candidate, `stagePin` null |

`governsDefault` is `complexity == settings.DefaultComplexity` and nothing else. Card pins never populate `stagePin`
or `candidates` (the composition asks `ResolveAsync` with `cardId: null`). Availability on each candidate is the
server's verdict (`EvaluateAvailabilityNowAsync`), as the stored list already carries.

#### F-B Default msw handler and fixtures (S-C1 adds; S-C2 reuses)

`client/src/test/mocks/handlers.ts:76-82`: the `/api/complexity-chains` handler body gains `defaultComplexity: 'Medium'`.
`RoutingSettingsTab.test.tsx`'s `chain()` helper gains `effective: null` in its base object; `listDto`, `planEffective`,
`codeEffective` gain `defaultComplexity: 'Medium'`. New per-test rows build `effective` by spreading
`{ source, candidates, stagePin, governsDefault }` with literal values; no production composer is called to derive an
expectation. `stagePin` literals use the `RoutingPinRefDto` shape (`id`, `cardId`, `cardIdentifier`, `role`,
`provenance`, `strength`, `agentKind`, `modelLevel`, `notBefore`, `reason`, `candidateCount`).

#### F-C Recipes

`A` = real HTTP through `ComplexityChainApiWebAppFactory` (`PerClass`, `[NotInParallel]`, `[Category("Integration")]`),
`JsonElement` oracles, every row written is deleted in `finally`. `I` = `AgentTaskService` over an isolated schema
exactly as `ComplexityCreateTests.cs:470-496` builds it (`Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] })`,
`ModelAvailability`, `ComplexityRoutingService`, `RoutingPinService`), `[Category("Integration")]`. `U` = pure static
calls with hand-built records, `[Category("Unit")]`. `M` = direct calls into `routingSettingsModel.ts`, `it.each` rows
with literal expected strings, titles `C097M <id> <name>`. `E` = `renderWithProviders` + `server.use` + `userEvent`,
notifications mocked as the touched files already do. Test files and titles below are **binding required** until Code
creates them and records their execution.

### Delivery inventory

No new async delivery path, queue, hub event, session input or persistence boundary is introduced. S-A changes a
value on the create request before the existing walk; S-B composes two existing reads; S-C writes through existing
routes plus one existing DELETE; S-F fires the existing coalesced instructions signal (whose delivery is already
verified by `OrchestratorInstructionsRefreshTests`). No producer-to-recipient or UserPrompt receipt claim is made.
Deadlines: none. Fixture timestamps (`updatedAt`) are display data; V-B6 compares two of them for order only.

### Proves it works now

**S-A — `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` (I unless marked; committed before
the production change for CP-1 red; `[Category("Integration")]` class with the two `U` methods marked `[Category("Unit")]`)**

- V-A1 `A_plain_create_walks_the_role_row_at_the_default_tier` | I | seed role cell Plan/Medium `[Codex/Frontier,
  ClaudeCode/High]` Human (direct `ComplexityChains` insert as `SeedHardChainAsync` does, with `Role = Plan`,
  `Complexity = Medium`); create `new CreateAgentTaskRequest("plan it", Role: Plan, Workspace: Shared)` →
  `Status Queued`, `AgentKind Codex`, `ModelLevel Frontier`, `Routing != null`, `Routing.Complexity == Medium`,
  `Routing.Source == "chain:Plan/Medium"`, `Routing.Walked == true`; stored `task.Complexity == Medium`,
  `task.ExplicitAgentKind == null`; Created event detail contains `(matrix default Medium)` and
  `chain=Plan/Medium candidate 1/2 gpt-6-astra`, and not `(role policy)`. Case → mutation: skip the stamp →
  `ClaudeCode/Frontier`, `Routing null` → red.
- V-A2 `An_unset_row_keeps_the_role_policy_path` | I | no rows at all; create Plan as above → `Queued`,
  `ClaudeCode/Frontier`, `Routing == null`, `task.Complexity == null`, Created detail contains `(role policy)` and not
  `(matrix default`; `Blocked` event count 0. Mutation: stamp unconditionally → `Blocked` → red.
- V-A3 `An_exhausted_default_row_blocks_with_the_cell_name` | I | role cell Code/Medium `[ClaudeCode/Frontier]`;
  `SeedHoldAsync(db, ClaudeCode, "fable", until: null)`; session seeded (`SeedSessionAsync`) and the caller carries
  it; create Code → `Status Blocked`, `Warning` starts with `routing exhausted: ` and contains `Code/Medium chain` and
  `fable`; `task.Complexity == Medium`; exactly one `Blocked` event; `SessionQueuedMessages` has one note for the task
  containing `Code/Medium`. Mutation: force `RefuseIfExhausted` on stamped creates → `RoutingExhaustedException` → red.
- V-A4 `An_explicit_kind_is_not_stamped_and_warns` | I | Plan/Medium cell `[Codex/Frontier]`; create Plan with
  `AgentKind: ClaudeCode` → `Queued`, `ClaudeCode/Frontier` (RolePolicy level), `Routing == null`,
  `task.Complexity == null`, `task.ExplicitAgentKind == ClaudeCode`, `Warning` contains `Plan/Medium` and
  `not consulted` (S-A's new sentence: `explicit kind/level: the Plan/Medium row was not consulted`). Mutation: stamp
  despite the explicit kind → 422 `complexity cannot be combined` → red.
- V-A5 `A_required_stage_pin_still_wins_over_the_row` | I | Plan/Medium cell `[Codex/Frontier]`; stage pin
  `PutRoutingPinRequest(Plan, Provenance: Human, Strength: Required, AgentKind: ClaudeCode, ModelLevel: High,
  Reason: "opus for Plan")`; create Plan → `ClaudeCode/High`, `Warning` contains `complexity chain bypassed`,
  `Routing!.Candidates` single, `Routing.Walked == false`, `Routing.Source == "pin:stage Plan"`,
  `task.Complexity == Medium`. Mutation: pass `Decision.None` to `WalkAsync` → `Codex` and no bypass warning → red.
- V-A6 `The_stamp_guard_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled` | U |
  `[Arguments]` × 6 over the extracted predicate `AgentTaskService.MatrixDefaultApplies(request, liveFollowUp)`:
  (Worker, Plan, nothing set, false) → true; (Worker, Check, …) → false; (Orchestrator, Plan, …) → false; (Worker,
  Plan, `AgentId` set) → false; (Worker, Plan, `ModelLevel: High`) → false; (Worker, Plan, `IgnoreModelDisabled: true`)
  → false; plus a 7th row (Worker, Plan, nothing set, liveFollowUp `true`) → false. Executed results: 7. Mutation:
  drop the specialist clause → the Check row returns true → red.
- V-A7 `An_orchestrator_create_keeps_role_policy_and_the_level_floor` | I | Plan/Medium cell `[ClaudeCode/Low]`;
  create `new CreateAgentTaskRequest("plan it", Kind: AgentTaskKind.Orchestrator, Role: Plan, Workspace: Shared)` →
  `ModelLevel == Frontier` (RolePolicy Plan), `Routing == null`, `task.Complexity == null`; a second create with
  `DelegationSettings.RolePolicy["Plan"].Level = Low` in the fixture → `ModelLevel == High` (`MinOrchestratorLevel`).
  If `Kind: Orchestrator` needs more setup in this fixture (an orchestrator bundle, a runner), Code extends the fixture,
  not the assertions. Mutation: stamp orchestrators → `Low` → red.
- V-A8 `The_settlement_commit_delegate_takes_the_commit_row` | I | Commit/Medium cell `[Codex/Frontier]`; a settled
  Worker task seeded as `CommitOnSettlePolicyTests.cs:194` seeds its subject; `service.CreateCommitTaskAsync(settled,
  "reason", ["a.txt"], null, null, ct)` → returned task `AgentKind Codex`, `ModelLevel Frontier`,
  `Complexity == Medium`; with no Commit row → `ClaudeCode/Medium` (RolePolicy Commit) and `Complexity == null`.
  Mutation: keep `ResolveAgentKind` only → `ClaudeCode` → red.
- V-A9 `An_undefined_default_complexity_fails_validation` | U | `new DelegationSettingsValidator().Validate(null,
  new DelegationSettings { DefaultComplexity = (TaskComplexity)999 })` → `Failed == true`, one failure containing
  `Delegation:DefaultComplexity`; the default instance → `Succeeded`. Mutation: drop the validator line → `Succeeded`
  → red.

**S-B — `tests/Antiphon.Tests/Application/ComplexityEffectiveComposeTests.cs` (U; CP-7 red over a compile-only
stub returning an empty list) and `ComplexityEffectiveHttpTests.cs` (A)**

- V-B1 `A_required_pin_replaces_the_cell_and_sources_pin` | U | `Decision` hand-built as
  `ComplexityRoutingComposeTests` builds one (stage pin Required Human, candidates `[Codex/Frontier]`); chain
  `[ClaudeCode/Frontier]` with label `Plan/Hard`, resolvedFrom `role`; RolePolicy pair `ClaudeCode/Frontier`;
  `ComplexityEffective.Compose(decision, chain, "Plan/Hard", "role", rolePolicyPair, isDefaultTier: false)` →
  `Source == "pin"`, `Candidates` is `[Codex/Frontier]`, `StagePin!.Id == pin.Id`, `GovernsDefault == false`.
  Mutation: return the chain → `Source "role"` → red.
- V-B2 `A_preferred_pin_prepends_and_sources_pin_plus_chain` | U | Preferred pin `[Grok/Frontier]`; chain
  `[ClaudeCode/Frontier, Grok/Frontier]` (`any`) → `Source == "pin+chain"`, candidates `[Grok/Frontier,
  ClaudeCode/Frontier]` (deduplicated), `StagePin` set. Mutation: ignore the pin → first candidate `ClaudeCode` → red.
- V-B3 `No_pin_and_no_cell_falls_back_to_role_policy` | U | `Decision.None`, chain `[]` resolvedFrom `none`,
  RolePolicy pair `ClaudeCode/High` → `Source == "rolePolicy"`, candidates `[ClaudeCode/High]`, `StagePin == null`;
  `Decision.None` with chain `[X]` resolvedFrom `any` → `Source == "any"`. Mutation: return an empty list for `none` →
  `Source` missing `rolePolicy` → red.
- V-B4 `Governs_default_is_true_only_on_the_default_tier` | U | same inputs with `isDefaultTier: true` → `true`;
  `false` → `false`. Mutation: always `true` → red on the second.
- V-B5 `Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded` | A | PUT
  `/api/complexity-chains/Plan/Hard` `{candidates:[{Codex,Frontier}], provenance:"Human"}` → 200; PUT
  `/api/routing-pins` `{role:"Plan", provenance:"Human", strength:"Required", agentKind:"ClaudeCode",
  modelLevel:"High", reason:"C097M V-B5"}` → 200, keep `id` S; seed a card with
  `RoutingPinServiceTests.SeedCardAsync(db, "CARD-9097M")` from a factory `AppDbContext` scope; PUT a **card** pin
  `{role:"Plan", card:"<card guid>", provenance:"Human", strength:"Required", agentKind:"Grok", modelLevel:"Frontier"}`
  → 200, keep `id` C; GET `/api/complexity-chains?role=Plan` → `defaultComplexity == "Medium"`; `chains[0]` (Hard)
  `effective.source == "pin"`, `effective.candidates[0].agentKind == "ClaudeCode"`, `effective.candidates[0].modelLevel
  == "High"`, `effective.candidates[0].alias == "opus"`, `effective.stagePin.id == S` (not C),
  `effective.governsDefault == false`; `chains[1]` (Medium) `effective.source == "pin"`, `effective.governsDefault ==
  true`; DELETE S → 204; GET again → `chains[2]` (Easy) `effective.source == "rolePolicy"`,
  `effective.candidates[0].alias == "fable"`, `effective.stagePin` null; `chains[0]` `effective.source == "role"`;
  GET `/api/complexity-chains` (list) → `defaultComplexity == "Medium"` and every entry's `effective` is JSON null.
  `finally` DELETE the cell, S and C. Mutation: compose `cardPin ?? stagePin` → `stagePin.id == C` → red.
- V-B6 `Put_twice_is_last_writer_wins_without_a_409` | A | PUT `/api/complexity-chains/Debug/Easy`
  `{candidates:[{Codex,Frontier}], provenance:"Human", reason:"first"}` → 200, read `updatedAt` U1; PUT the same cell
  `{candidates:[{Grok,Frontier}], provenance:"Human", reason:"second"}` → 200; GET `?role=Debug` → Easy `candidates`
  is `[Grok/Frontier]`, `reason == "second"`, `updatedAt >= U1`. `finally` DELETE. Mutation: refuse a second Human
  write with 409 → red. (This is OD-4's statement as a test; a future concurrency token flips it on purpose.)

**S-C1 — `routingSettingsModel.test.ts` (M), `RoutingSettingsTab.test.tsx` (E), `complexityChains.test.tsx` (R)**

- V-C1 `C097M V-C1 <case>` `effectiveCellLabel` | M | rows: `pin` Required Human `[Codex/Frontier gpt-6-astra]` →
  `Required Human pin → Codex/Frontier (gpt-6-astra)`; `pin+chain` Preferred Human `[Grok/Frontier grok-4.7, …]` →
  `Preferred Human pin → Grok/Frontier (grok-4.7), then this row`; `rolePolicy` `[ClaudeCode/High opus]` → `Falls back
  to RolePolicy → ClaudeCode/High (opus)`; `role` → `Own rule`; `any` → `Inherits Any role`; `config` → `Configuration
  fallback`. Mutation: ignore `effective.source` → red on the first row.
- V-C2 `C097M V-C2 <case>` `unsetCellLabel(tier, governsDefault)` and `nonComplexityBoundary(tier)` | M | (`Easy`,
  false) → `Unset — a -Complexity Easy dispatch blocks`; (`Medium`, true) → `Falls back to RolePolicy` prefix is used
  instead (label from V-C1); `nonComplexityBoundary('Medium')` contains `Medium` and `every dispatch without
  -Complexity`. Mutation: drop the tier from the sentence → red.
- V-C3 `C097M V-C3 card-pin-count` `cardPinCountLabel(role, pins)` | M | 0 → `null`; 1 → `1 card pin`; 2 → `2 card
  pins`. Mutation: always plural → red.
- V-C4 `C097M V-C4 pin-overlay-first` | E | `serveRouting` with Plan Hard `effective: { source: 'pin', stagePin:
  {…Required Human ClaudeCode/High}, candidates: [opus], governsDefault: false }` → `routing-matrix-cell-Plan-Hard`
  renders `Required Human pin → ClaudeCode/High (opus)` as its first text node and `Own rule` after it. Mutation:
  render the stored list first → red.
- V-C5 `C097M V-C5 default-column-and-fallback` | E | list `defaultComplexity: 'Medium'`; Plan Medium
  `effective: { source: 'rolePolicy', candidates: [fable], stagePin: null, governsDefault: true }`; Plan Easy
  `effective: { source: 'rolePolicy', …, governsDefault: false }` and `resolvedFrom 'none'` → column header `Medium
  (default for dispatches without -Complexity)`; `routing-matrix-cell-Plan-Medium` shows `Falls back to RolePolicy →
  ClaudeCode/Frontier (fable)`; `routing-matrix-cell-Plan-Easy` shows `Unset — a -Complexity Easy dispatch blocks`
  (an unset non-default tier is still unset for an explicit `-Complexity Easy`). Mutation: drop the header suffix →
  red.
- V-C6 `C097M V-C6 card-pin-count-in-row` | E | `defaultPins` (one Code card pin) → `routing-matrix-row-Code` contains
  `1 card pin`; `routing-matrix-row-Plan` does not. Mutation: count stage pins too → `2 card pins` → red.
- Rewritten with reason (the copy they assert is the copy S-C1 changes): `shows inheritance, configuration fallback,
  and unset text` (`:347-360`: Plan-Easy and any-Easy now read `Unset — a -Complexity Easy dispatch blocks`; the
  any-role row has no `effective`); `wording for Required bypass versus Preferred prepend, and names Code → Grok`
  (`:373-385`: `pinEffectCopy` becomes `Required Human pin: Code → Grok. Overrides every Code cell until cleared.` /
  `Preferred Human pin: Plan → ClaudeCode/Frontier. Tried before every Plan cell.`); `states the D6 propagation
  boundary and the D7 non-complexity RolePolicy boundary` (`:447-458`: asserts the V-C2 sentence).
- R-C1 the 7 `complexityChains.test.tsx` cases run unchanged (the DTO gains optional fields only).

**S-C2 — `ComplexityChainRowEditor.test.tsx` (E), `client/src/api/routingPins.test.ts` (E), `routingSettingsModel.test.ts`
(M), `RolePolicySection.test.tsx` (E)**

- V-C7 `C097M V-C7 three-puts-in-tier-order` | E | msw records PUTs; editor opened for Review with candidates
  `[Codex/Frontier]`, reason `only Astra reviews`; Save → confirm → exactly three PUTs, paths
  `/api/complexity-chains/Review/Hard`, `/Medium`, `/Easy` in that order, each body `{ candidates: [{ agentKind:
  'Codex', modelLevel: 'Frontier' }], provenance: 'Human', reason: 'only Astra reviews', notAfter: null }`; success
  notification. Mutation: PUT Hard only → red.
- V-C8 `C097M V-C8 stops-on-first-failure` | E | Medium PUT → 422 `{ errors: { Candidates: ['boom'] } }` → exactly two
  PUTs; the form shows `Hard written; Medium failed: boom; Easy not written`; no DELETE; modal stays open. Mutation:
  continue after a failure → three PUTs → red.
- V-C9 `C097M V-C9 clears-the-stage-pin-after-the-puts` | E | `stagePin` prop set (`id 'pin-review'`), checkbox left
  on → one `DELETE /api/routing-pins/pin-review` recorded **after** the third PUT (msw records a sequence array).
  Mutation: DELETE first → order assertion red.
- V-C10 `C097M V-C10 unchecked-keeps-the-pin` | E | checkbox unchecked → zero DELETEs; the confirm copy says the pin
  `still overrides` the row. Mutation: DELETE regardless → red.
- V-C11 `C097M V-C11 no-pin-no-checkbox` | E | `stagePin: null` → no checkbox in the DOM; confirm copy equals
  `rowReplaceCopy('Review', null)` and does not contain `pin`. Mutation: always render the checkbox → red.
- V-C12 `useClearRoutingPin DELETEs /routing-pins/<id> and invalidates pins, role-policy and complexity-chains` | E |
  recipe from `complexityChains.test.tsx:166-190`; after the mutation msw records a second GET on `/api/routing-pins`,
  `/api/role-policy` and `/api/complexity-chains`. Mutation: invalidate pins only → no second complexity-chains GET →
  red.
- V-C13 `C097M V-C13 <case>` `rowCellRequests` and `rowReplaceCopy` | M | `rowCellRequests('Review', cands, 'r')` →
  three entries `{ role: 'Review', complexity: 'Hard'|'Medium'|'Easy', candidates, reason: 'r', notAfter: null }` in
  that order; `rowReplaceCopy('Review', pin)` contains `Hard, Medium and Easy`, `replaces`, `stage pin` and
  `Codex/Frontier`; `rowReplaceCopy('Review', null)` has no `pin`. Mutation: drop Easy → red.
- V-C14 `C097M V-C14 configure-opens-row-editor` | E | render `RolePolicySection` with an `onConfigure` spy → click
  `Configure Plan` → called once with `'Plan'`; the section copy no longer contains `scripts/routing-pin.ps1`.
  Mutation: omit the callback → red.
- R-C2 the 10 `ComplexityChainCellEditor.test.tsx` cases run unchanged (the cell editor is not modified; its
  draft/validation helpers are imported by the row editor).

**S-F — `OrchestratorInstructionsRendererTests` (U), `OrchestratorInstructionsRefreshTests` (I, harness),
`OrchestratorInstructionsDeltaTests` (U)**

- V-F1 `Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (edited with reason: the matrix
  section renumbers 5–7 to 6–8) | U | `Sample()` gains `Matrix: new MatrixSection("Medium", [new
  MatrixInstructionRow("Plan", Hard: "Codex/Frontier (gpt-6-astra)", Medium: "…", Easy: "unset", Effective:
  "ClaudeCode/High (opus)", Source: "stage pin")])`; body contains `## 5. Role × complexity matrix`, `default Medium`,
  `GET /api/complexity-chains`, `- Plan:`, `via stage pin`, `## 6. Routing pins`, `## 7. Model levels`, `## 8. Operator
  standing instructions`. Mutation: omit the section → red.
- V-F2 `Covered_writes_each_regenerate_and_notify("complexity-chain")` | I | new `[Arguments("complexity-chain")]`;
  `CoveredWriteAsync` arm: `scope.ServiceProvider.GetRequiredService<ComplexityChainService>().UpsertAsync(Plan,
  Medium, new PutComplexityChainRequest([new(Codex, Frontier)], Human, "cell"), null, ct)`, `WhenIdleAsync`, return
  `"matrix:"`; `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside
  `ModelAvailability` (`:173`). Mutation: drop the signal from `UpsertAsync` → `SubmittedBodies.Count == 0` → red.
- V-F3 `An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` | U | two snapshots with
  equal `Matrix` → no line starts with `matrix:`; change Plan's Medium → one line `matrix: Plan Medium …`. Mutation:
  `DiffMatrix` always reports → first assertion red.

### Guards the regression

- R-A1 `ComplexityCreateTests` (14 methods) unchanged. `No_complexity_is_unchanged_when_fable_is_held` (`:230-245`)
  stays green **because** its seed is a Hard any-role chain and OD-1 is Medium; V-A2 is its explicit sibling. If OD-1
  becomes `Hard`, that method's meaning changes and must be rewritten with the operator's decision as the reason.
- R-A2 `RoutingPinCreateTests` (13) unchanged: pins without rows are untouched by S-A (D-2).
- R-A3 `RoutingPinCandidateCreateTests` (17) unchanged: every method seeds a pin list or a Hard chain, none a Medium
  row.
- R-A4 `ComplexityDispatcherTests` (9) unchanged: the re-walk is keyed on `task.Complexity`, which S-A sets to the same
  value a `-Complexity Medium` create would.
- R-B1 `ComplexityChainRoleHttpTests` (3) unchanged: `effective` is additive; the three existing `JsonElement` reads
  name their properties.
- R-B2 `ComplexityChainServiceTests` (10) unchanged: the optional constructor parameter defaults to null and
  `GetEffectiveAsync` without a pin service composes with `Decision.None`.
- R-C1, R-C2 as above.
- R-F `OrchestratorInstructionsRendererTests` other 5 methods and `OrchestratorInstructionsRefreshTests` other 10
  methods unchanged (the renumbering touches one method's literals and the `Sample()` fixture only).

### Guard inventory

| Guard | Where | Existing test |
|---|---|---|
| `-Complexity` + `-Kind/-Level` is 422 | `AgentTaskService.cs:736-746` | `ComplexityCreateTests.Explicit_kind_plus_complexity_is_422` |
| `-Complexity` + `ignoreModelDisabled` is 422 | `:749-756` | `ComplexityCreateTests.IgnoreModelDisabled_plus_complexity_is_422` |
| Required pin + chain → pin wins with bypass warning | `RoutingCandidates.cs:58-63`; `AgentTaskService.cs:933-941` | `ComplexityCreateTests.A_required_pin_plus_complexity_uses_the_pin_pair_and_warns_bypass` |
| Explicit kind vs Required pin → 409 | `RoutingPinService.cs:348-358` | `RoutingPinCreateTests.An_explicit_kind_against_a_required_pin_is_409_routing_pin_conflict` |
| Exhausted chain → Blocked, resumes on hold clear | `AgentTaskService.cs:1448-1452`; `AgentTaskDispatcher.cs:1749-1778` | `ComplexityCreateTests.All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note`; `ComplexityDispatcherTests.Clearing_a_hold_requeues_a_routing_blocked_task` |
| Reroute ends governance | `AgentTaskService.cs:2813-2816` | `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` |
| Auto never overwrites Human; empty/duplicate 422 | `ComplexityChainService.cs:176-187`, `:279-334` | `ComplexityChainHttpTests.Auto_over_human_is_409_and_empty_or_duplicate_is_422` |
| `?role=` view resolves role → any → none | `ComplexityChainService.cs:111-134` | `ComplexityChainRoleHttpTests.Get_role_Plan_returns_effective_resolvedFrom` |
| Pin write regenerates instructions | `RoutingPinService.cs:48-49`, `:270` | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify("routing-pin")` |
| Unchanged write neither rewrites nor notifies | `OrchestratorInstructionsDelta.cs` | `OrchestratorInstructionsRefreshTests.An_unchanged_write_neither_rewrites_nor_notifies` |

### Positive controls

The plan's PC-1–PC-27 table is the binding list (one per behaviour; exact method names above). PC-6 binds to V-A6's
`[Arguments]` row `Check`; PC-9 to V-A9; PC-16–PC-24 name the Vitest titles above. None has been run.

### Out of scope

- S-D and S-E have no automated verification. M-2: the Code report pastes `complexity-chain.ps1 set -Role Review
  -Complexity All -Candidates Codex/Frontier -Json` (three 200 bodies), `get -Role Review` (the `effective:` line),
  `clear -Role Review -Complexity All`, and a run where the second tier is refused (a 422 provoked with a duplicate
  candidate on a hand-edited body is acceptable) showing `Hard written, Medium refused, Easy not written`. M-3: the
  Code report pastes the dry run of `migrate-stage-pins-to-cells.ps1` against the live server (13 rows, 0 manual, 0
  writes, the backup path) and the `-ClearPins` without `-Apply` usage error. Neither `-Apply` is run by Code.
- M-1 docs: `grep -n "Routing pins beat RolePolicy\|read-only for pins\|does not govern" docs/orchestration-loop.md`
  returns the rewritten lines; `docs/antiphon-api.md` names `effective`, `defaultComplexity` and last-writer-wins;
  `docs/ops-http.md` has the matrix row; `.claude/skills/antiphon-delegate/SKILL.md:147` names the default tier.
- No E2E (the Routing tab has none). No Mutation run in this card. No concurrency token (OD-4). No per-row strength
  (OD-2). No hub event (D-10). The attention feed is covered by the existing `ComplexityAttentionTests` and is not
  re-tested for stamped tasks (their rows carry `Complexity`, the same key).

### Checkpoints

The closed list is the plan's `### Checkpoints` table (CP-1–CP-17); this amendment adds no row. Corrections to the
plan's `Expect` text that Code applies when reporting: CP-1/CP-2 execute 9 methods and **15 results** (V-A6 carries 7
`[Arguments]`); `Min` stays 9. V-A6 and V-A9 are `[Category("Unit")]` methods in the same class; the class filter runs
them with the integration methods.

### Cost

As the plan states: ordinary green floor 104 minutes, red-first 15 minutes, authoring 465 minutes across seven Code
dispatches (S-A 75, S-B 60, S-C1 60, S-C2 90, S-D 45, S-E 60, S-F 75). No production timeout or assertion is loosened.
Edited existing tests, each with its reason recorded above: three `RoutingSettingsTab.test.tsx` cases (copy change),
one `OrchestratorInstructionsRendererTests` method and its `Sample()` fixture (section renumbering).

## Appendix A — withdrawn: the 2026-10-13 Appendix A (S3 stage-pin editor)

V-11–V-15, R-3, PC-8–PC-11 and CP-5 of the 2026-10-13 design are withdrawn by plan D-6. `StagePinEditor.tsx`,
`usePutRoutingPin`, `pinRequestFrom` and `requiredConfirmCopy` are not built. The one pin write the client gains is
`useClearRoutingPin` (V-C12), used only from the row editor after its three cell writes succeeded (V-C9).
