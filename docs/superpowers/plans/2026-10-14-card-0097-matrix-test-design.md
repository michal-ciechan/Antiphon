# CARD-0097 verification design amendment — the matrix as the single routing surface

Plan task `c491a627` (Frontier) wrote this amendment with [the rework plan](2026-10-14-card-0097-matrix-plan.md);
task `785ac731` repaired both for Final Review `2f218eeb` (F01–F12); task `9e5cdef3` (Plan, Frontier) **reduced** both
on 2026-10-14 for Final Review `f39ef633` (R2-01–R2-11), from `6356c5d6851f97c1fcbadc1a0abcbe007abe333d` on
`feat/card-task-9e5cdef3`. Source census 2026-10-14, read from master `d317c3f593112e8da01a046694db2e5671594969`
with `git show origin/master:<path>`: no build, no test run, no discovery execution. It amends
[the 2026-10-13 test design](2026-10-13-card-0097-test-design.md): the read slices it froze (V-1–V-10, V-16–V-30,
PC-1–PC-27) stand and are landed; its Appendix A (the held S3 pin editor, V-11–V-15, R-3, CP-5) is **withdrawn** and
replaced by V-C7–V-C13 below. IDs here are prefixed by slice (V-A1, V-B1, V-C1, V-D1, V-E1, V-F1; R-A1 …; PC-1 …
PC-38 of the plan's table) and do not collide with the 2026-10-13 numbering. Round-2 withdrawals: V-A16, V-B7–V-B9,
V-C14–V-C17, V-E7–V-E11, V-F4–V-F6, V-F8 and the delivery flow they described.

**Operator decisions applied (plan §2).** OD-1 default tier `Medium`; OD-2 cells warn, pins refuse; OD-3 pins are
migrated by the script and cleared only by the operator's `routing-pin.ps1 clear`; OD-4 last-writer-wins stated and
tested (V-B6, `updatedAt >= U1`); OD-5 card pins read-only with a per-row count; OD-6 exhausted stamped create is
Blocked (D-3 extends it to the Commit and Merge children when a row resolved and no Required pin with a candidate
exists); OD-7 orchestrator creates are not stamped (consequence disclosed in plan §6.4). Post-land Mutation is paused:
PC-1–PC-38 are specified and pending; none is claimed run.

**Slice order and sizing.** S-0 (20 min) → S-A (90) ∥ S-B (70) → S-C1 (60) ∥ S-D (60) → S-E (60) → S-F (75) → S-C2
(90, optional). S-B reads S-0's `DefaultComplexity`; S-C1 depends on S-B's wire contract (F-A); S-D's `get` on F-A;
S-E's tests on S-D's `ScriptedApi`; S-F on S-A, S-B, S-D and S-E. Each slice commits and pushes before its checkpoint
rows run. CARD-1234 and CARD-1213 are on master (`d317c3f59`, `6d49673fa`); Code branches from master at or after
`d317c3f59`.

**Collisions (read 2026-10-14 ~08:35Z).** The census uses the Antiphon project id
(`GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, `taskScope: "project"`) and the
board task list (`GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=…`); `?unscoped=true`
reports `taskScope: "null"` and is not evidence of an idle pipeline. The orchestrator re-reads the project-scoped
routes before dispatching S-C1/S-C2 (same-area rule for `client/src/features/settings`) and S-F
(`docs/orchestration-loop.md`, `docs/antiphon-api.md`).

**Ground truth confirmed from source (anchors for the recipes below; master `d317c3f59`).** `CreateAsync` resolves
the pin decision at `AgentTaskService.cs:770-781`, overlays it at `:803-836` (an agent-only pin fills `AgentId` at
`:831`), binds a standing agent at `:839-860` (`AgentKind = pinned.Kind` at `:858`), then opens the walk only on
`request.Complexity` (`:923-932`); the stored row carries `Complexity`, `RoutingPinId`, `ExplicitAgentKind`,
`ExplicitModelLevel` (`:1364-1367`); an exhausted walk Blocks (`:1448-1453`) with a Blocked event and parent note
(`:1577-1581`, `EnqueueBlockedParentNoteAsync :4610-4634`: `SourceTaskId`, `ContentDigest =
DelegationNoteDigest.Compute(reason)`, `NoteHeader`, `Status Pending`); the Created detail says `(explicit override)`
or `(role policy)` (`:1556-1560`); `routingPinId` is stamped only on a walked list (`:944-945`, `:1036-1037`).
`CreateCommitTaskAsync(settled, reason, dirtyPaths, headSha, stderr, ct)` (`:3811-3935`; routing `:3836-3869`, the
`Candidates[0]` take `:3857-3858`, the pin-failure catch `:3865-3868`, a Warning event for `pinWarning` `:3934`) and
`CreateMergeTaskAsync(conflicted, conflictFiles, ct, …)` (`:3706-3797`; `ModelLevel = ResolveLevel(Worker, Merge,
null)` at `:3770`, no `AgentKind` assignment — the entity default is `ClaudeCode`, `AgentTask.cs:97`) are `internal`,
already called from `CommitOnSettlePolicyTests.cs:187-199`. `ResolveLevel` (`:3976-3996`) throws 422
`role_policy_level_invalid` on an undefined policy/default level since CARD-1234. `LoadChainAsync` returns
`ResolvedFrom` `role`/`any`/`config`/`none` and does not validate the role (`ComplexityRoutingService.cs:312-343`);
`WalkAsync` composes the pin decision with the chain through `RoutingCandidates.Compose` (`:211-219`;
`RoutingCandidates.cs:44-97`); `Walk.ExhaustedSentence` (`:143-173`) and `SkippedWarning` (`:176-185`);
`ResolveAgainstRolePolicy` has no withholding (`:432-446`). `RolePolicyReadService.Row`/`Effective` withhold
unsupported kinds and undefined levels (`RolePolicyReadService.cs:51-69`, `:78-98`, `:104-105`, `:133-137`).
`ComplexityChainService` is constructed with `(db, time, routing, options, logger)` in `ComplexityChainServiceTests.cs:310`,
`:406` and `MutationAdmissionTests.cs:51`; the HTTP factory is `ComplexityChainApiWebAppFactory : AntiphonWebAppFactory`
(`ComplexityChainHttpTests.cs:142`), `[NotInParallel]`, `PerClass`. `RoutingPinService.ResolveAsync(null, role,
Ask(null,null,null,false))` yields the stage-pin decision (`RoutingPinService.cs:303`); the pool-delegate seed is
`RoutingPinServiceTests.cs:228-256` (set `IsPoolDelegate = false` for a standing agent) and `SeedCardAsync` is `:289`;
`PutRoutingPinRequest` is `RoutingPinDtos.cs:13-31`. The renderer asserts section headings literally
(`OrchestratorInstructionsRendererTests.cs:33-63`); its ladder test renders at the 16,384 default and checks cut-notice
order (`:81-136`); `Sample()` carries two runners (`desktop`, `server2`), a stage-wide Code pin and one card pin;
`Covered_writes_each_regenerate_and_notify` takes six `[Arguments]` (`OrchestratorInstructionsRefreshTests.cs:60-67`)
through `CoveredWriteAsync` (`:351-395`); `BridgeQueueHarness` registers `ModelAvailability` but neither chain
service (`TestHelpers/BridgeQueueHarness.cs:173`). Script harness: `ComplexityChainScriptTests.cs` runs
`scripts/complexity-chain.ps1` with `pwsh -NoProfile -NonInteractive -File` against an `HttpListener` `StubApi`
(`:148-240`; `EphemeralHttpListener.BindLoopback`), `[ParallelLimiter<ProcessSpawnLimit>]`. Client: `MatrixCell`
badge from `resolvedFromLabel` (`RoutingSettingsTab.tsx:504-531`; `routingSettingsModel.ts:39-46`); pin banner
`pinEffectCopy` (`:113`; rendered at `RoutingSettingsTab.tsx:471`); `DraftCandidate`/`toDraft` are module-private in
`ComplexityChainCellEditor.tsx:59-75`; exported list helpers in `routingSettingsModel.ts` (`CHAIN_KIND_OPTIONS :142`,
`MAX_CHAIN_CANDIDATES :150`, `candidateListError :238`, `nextUnusedCandidate :258`); the three rewritten
`RoutingSettingsTab.test.tsx` cases are at `:347`, `:373`, `:447`; msw `onUnhandledRequest: 'error'`. Vitest case
counts are read with `grep -c "^\s*it(" <file>` when Code reports; they are not restated here.

## Verification design

### Inspection

| Bodies read | Boundary → V/R or exclusion |
|---|---|
| `AgentTaskService.cs:700-870`, `:900-1125`, `:1355-1372`, `:1440-1460`, `:1548-1585`, `:2805-2820`, `:3700-3935`, `:3970-4060`, `:4600-4640` | pin decision, overlay, standing agent, stamp point, Blocked path, event text, commit and merge producers, parent note → V-A1–V-A8b, V-A10–V-A15, R-A1–R-A5 |
| `DelegationSettings.cs:340-390`, `:967`, `:1252-1380` | new setting and validator row → V-A9 |
| `ComplexityRoutingService.cs:20-100`, `:100-260`, `:300-350`, `:425-460` | load/compose/walk, skips, sentences → V-A3, V-A13, V-A14, V-B1–V-B4 |
| `RoutingCandidates.cs:40-100` | pure precedence reused by S-B → V-B1–V-B3 |
| `ComplexityChainService.cs:25-70`, `:105-140`, `:220-275`; `ComplexityChainDtos.cs:1-50`; `server/Api/Endpoints/ComplexityChainEndpoints.cs:15-45` | DTO shape, `?role=` view, last-writer-wins → F-A, V-B5, V-B6, R-B1, R-B2 |
| `RolePolicyReadService.cs` (whole file); `RolePolicyDtos.cs:20-40` | withholding contract → V-B10, R-B3 |
| `RoutingPinService.cs:30-130`, `:160-290`, `:300-420`, `:648-680`; `RoutingPinDtos.cs:1-90` | stage decision, agent-only pins, DELETE by id, signal, PUT body validation → V-A5, V-A10, V-A11, V-B5, V-C9, V-F2, plan D-9 |
| `AgentTaskDispatcher.cs:705-750`, `:1749-1792`, `:7314-7331` | re-walk keyed on `Complexity`/`RoutingPinId`; standing agent keeps its kind → R-A4; V-A10 rationale |
| `OrchestratorInstructionsSnapshot.cs`, `SnapshotBuilder.cs:28-42`, `:203`, `Renderer.cs:1-271`, `Delta.cs:1-45`, `:159`, `Service.cs:124-210`, `:212-285`, `Settings.cs:1-32` | section, bound, ladder, signal, delta → V-F1–V-F3, V-F7 |
| `tests/…/ComplexityCreateTests.cs:24-247`, `:466-500`; `RoutingPinServiceTests.cs:228-262`, `:289`; `CommitOnSettlePolicyTests.cs:173-199`; `ComplexityDispatcherTests.cs` titles; `ComplexityRoutingComposeTests.cs:55-85`, `:330-360`; `OrchestratorInstructionsRendererTests.cs:28-140`; `OrchestratorInstructionsRefreshTests.cs:55-80`, `:351-395`; `OrchestratorInstructionsDeltaTests.cs` titles; `BridgeQueueHarness.cs:165-180` | fixtures and recipes → each V |
| `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs` (whole file); `TestHelpers/EphemeralHttpListener.cs` | script harness recipe → V-D1–V-D7, V-E1–V-E6, R-D |
| `client/src/api/complexityChains.ts`, `routingPins.ts`, `rolePolicy.ts`; `features/settings/RoutingSettingsTab.tsx:320-335`, `:409-531`, `:578-600`; `routingSettingsModel.ts`; `ComplexityChainCellEditor.tsx:55-80`; `test/mocks/handlers.ts:72-84`; the test files above | types, cell, editor, fixtures → F-A client side, F-B, V-C1–V-C13, R-C1, R-C2 |
| `scripts/complexity-chain.ps1` (whole file), `scripts/routing-pin.ps1:1-30`, `:268-296`, `scripts/delegate.ps1:1208-1212` | script verbs, pin clear by grain, resolved-kind echo → V-D, V-E, M-1 |
| `docs/orchestration-loop.md:884-970`, `docs/antiphon-api.md:660-716`, `docs/ops-http.md:139`, `docs/testing-and-build.md:395-415`, `:465-475`; `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:80-100`, `:190-230`, `:379-390`; `Program.cs:35`, `:188-204` | doc targets, manifest reuse syntax, `import` verb → M-1, Checkpoints |

#### F-A Frozen wire contract for the effective cell (S-B; binds S-C1, S-C2, S-D and the msw fixtures)

Property names camelCase; enums as strings; nullable fields present explicitly.

```
ComplexityChainListDto { chains: ComplexityChainDto[], roles: AgentTaskRole[], complexities: string[],
                         defaultComplexity: 'Hard' | 'Medium' | 'Easy' }        // NEW, on both views
ComplexityChainDto     { …unchanged fields…, effective: ComplexityEffectiveDto | null }
                       // non-null ONLY on GET /api/complexity-chains?role=<Role>; null on the list view and on any-role rows
ComplexityEffectiveDto { source: 'pin' | 'pin+chain' | 'role' | 'any' | 'config' | 'rolePolicy',
                         candidates: ComplexityCandidateDto[],   // ordered; what WalkAsync would try, with availability
                         stagePin: RoutingPinRefDto | null,      // the active STAGE pin composed in (not a card pin)
                         governsDefault: boolean,                // complexity == defaultComplexity
                         withheldReason: string | null }         // non-null only with source rolePolicy and an unlaunchable policy
```

`source` is a fail-closed whitelist (hand-written expected rows; V-B1–V-B5, V-B10):

| stage pin | chain (`LoadChainAsync`) | composed by `RoutingCandidates.Compose` | effective |
|---|---|---|---|
| Required with ≥1 candidate | any | pin slots only | `pin`, pin candidates, `stagePin` set |
| Preferred with ≥1 candidate | non-empty | pin slots then chain, deduplicated | `pin+chain`, that list, `stagePin` set |
| Preferred with ≥1 candidate | empty (`none`) | pin slots then the policy pair (`Compose` appends it when `chain` is null; S-B passes null for `none`) | `pin+chain`, `stagePin` set |
| Required with no candidate (forbid-only / agent-only) | non-empty | chain only | chain's `resolvedFrom` (`role`/`any`/`config`), `stagePin` set |
| none | non-empty | chain | `role`/`any`/`config`, `stagePin` null |
| none | empty | empty → the policy pair from `RolePolicyReadService.PolicyTarget(settings, role)` | `rolePolicy`, one candidate, `stagePin` null |
| none | empty | `PolicyTarget` withheld (`WithheldReason` non-null) | `rolePolicy`, `candidates []`, `withheldReason` = that string |

`PolicyResolution(AgentKind? Kind, AgentModelLevel? Level, string? Alias, string? WithheldReason)` is the typed input
(plan D-4): `WithheldReason` is `"{role} policy names {kind ?? "ClaudeCode"}/{level}, which delegation cannot launch"`
with the raw configured values when the kind is outside `DelegatableKinds` or the level is undefined; null otherwise.
`governsDefault` is `complexity == settings.DefaultComplexity` and nothing else. Card pins do not populate `stagePin`
or `candidates` (the composition asks `ResolveAsync` with `cardId: null`). Availability on each candidate is the
server's verdict (`EvaluateAvailabilityNowAsync`, `ComplexityRoutingService.cs:452`), as the stored list already carries.

`GET /api/role-policy` is **not changed** by this card (plan D-6).

#### F-B Default msw handler and fixtures (S-C1 adds; S-C2 reuses)

`client/src/test/mocks/handlers.ts:76-82`: the `/api/complexity-chains` handler body gains `defaultComplexity: 'Medium'`.
`RoutingSettingsTab.test.tsx`'s `chain()` helper gains `effective: null` in its base object; `listDto`, `planEffective`,
`codeEffective` gain `defaultComplexity: 'Medium'`. New per-test rows build `effective` by spreading
`{ source, candidates, stagePin, governsDefault, withheldReason }` with literal values; no production composer is
called to derive an expectation. `stagePin` literals use the `RoutingPinRefDto` shape (`id`, `cardId`,
`cardIdentifier`, `role`, `provenance`, `strength`, `agentKind`, `modelLevel`, `notBefore`, `reason`,
`candidateCount`; `RoutingPinDtos.cs:77-88`). `ROLE_POLICY_FIXTURE` is not touched.

#### F-C Recipes

`A` = real HTTP through `ComplexityChainApiWebAppFactory` (`PerClass`, `[NotInParallel]`, `[Category("Integration")]`),
`JsonElement` oracles, each row written is deleted in `finally`. `I` = `AgentTaskService` over an isolated schema
exactly as `ComplexityCreateTests.cs:469-487` builds it (`Options.Create(new DelegationSettings { AllowedRoots =
[workspace.Path] })`, `ModelAvailability`, `ComplexityRoutingService`, `RoutingPinService`), `[Category("Integration")]`.
`U` = pure static calls with hand-built records, `[Category("Unit")]`. `H` = `BridgeQueueHarness`
(`[NotInParallel("MessageQueue")]`, the `OrchestratorInstructionsRefreshTests` recipes). `S` = the script harness
(`pwsh` against an `HttpListener` stub, `[ParallelLimiter<ProcessSpawnLimit>]`): S-D adds
`tests/Antiphon.Tests/Scripts/ScriptedApi.cs` beside `StubApi` — `Requests` (ordered `(Method, Path, Body)`),
`Respond(method, pathPrefix, status, body)` per path, and an in-memory `Cells` (keyed `role/complexity`, PUT body
stored, `resolvedFrom` answered as `role` when present else `none`) and `Pins` store that `GET /api/routing-pins` and
`GET /api/complexity-chains?role=` answer from and `PUT`/`DELETE` mutate, so a read-back sees the writes. `M` =
direct calls into `routingSettingsModel.ts`, `it.each` rows with literal expected strings, titles `C097M <id> <name>`.
`E` = `renderWithProviders` + `server.use` + `userEvent`, notifications mocked as the touched files already do. Test
files and titles below are **binding required** until Code creates them and records their execution.

### Delivery inventory

This card adds no delivery mechanism. The one outcome that reaches a session is inventoried so Review can see the
existing path it reuses; the instructions notice is listed because a cell write now enters its existing path.

| Outcome | Producer | Recipient | Persistence and identity | Delivery | Receipt | Loss/recovery boundary | Bound by |
|---|---|---|---|---|---|---|---|
| Blocked default-tier create (OD-6) and Blocked Commit/Merge child (D-3) | `EnqueueBlockedParentNoteAsync` (`AgentTaskService.cs:4610-4634`) in the create/settle transaction — the existing producer the `-Complexity` Blocked path already uses (`:1577-1581`) | `task.ParentSessionId` (must exist in `AgentSessions`) | `SessionQueuedMessages` row: `SourceTaskId`, `ContentDigest = DelegationNoteDigest.Compute(reason)`, `NoteHeader`, `Origin Delegation`, `Status Pending`; committed with the task | existing turn-end flush / stranded flush (`SessionMessageQueueService.OnTurnEndAsync :1554`, `FlushStrandedQueuesAsync :1599`) | one complete `UserPrompt` transcript row whose `Text` equals the row's `Body` | the row and the task commit together; a crash after commit leaves a Pending row the stranded flush delivers; no new boundary is added | V-A3, V-A13 (row fields); existing receipts: `ComplexityCreateTests.All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note` (`:54`, row), `AgentTaskCommitEndpointTests.Spawned_child_complete_brief_gated_operation_and_parent_receipt` (`AgentTaskCommitChildChainTests.cs:24`, a Commit child's parent receipt through the real queue) |
| Instructions notice after a cell write (D-10) | `OrchestratorInstructionsService.QueueNoticesAsync` (`:212-285`), unchanged: a cell write only fires `OrchestratorInstructionsSignal` | `OrchestratorInstructionsRecipients.Select` | `OrchestratorInstructionsStates` row, `SessionQueuedMessages` row with `NoteHeader` | `EnqueueAsync(WhenIdle, deliverIfIdle: true)` (`:271-278`) | one `UserPrompt` row equal to the queued `Body` | unchanged from today | V-F2 (a cell write enters the path); existing: `Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator` (`:30`), `A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row` (`:209`) |

Deadlines: none. Fixture timestamps (`updatedAt`) are display data; V-B6 compares two of them for order-or-equal.

### Proves it works now

**S-0 — `tests/Antiphon.Tests/Application/MatrixDefaultComplexitySettingsTests.cs` (U; committed before the validator
line for CP-1 red)**

- V-A9 `An_undefined_default_complexity_fails_validation` | U | `new DelegationSettingsValidator().Validate(null,
  new DelegationSettings { DefaultComplexity = (TaskComplexity)999 })` → `Failed == true`, one failure containing
  `Delegation:DefaultComplexity`. Mutation: drop the validator line → `Succeeded` → red.
- V-A9 `The_default_instance_validates` | U | `new DelegationSettings()` → `Succeeded`, `DefaultComplexity == Medium`.

**S-A — `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` (I unless marked; committed with the
scaffold for CP-3 red; `[Category("Integration")]` class with the one `U` method marked `[Category("Unit")]`)**

The scaffold commit that precedes CP-3 contains `AgentTaskService.MatrixDefaultApplies(CreateAgentTaskRequest, bool)`
returning `false`, `ResolveServerChildRoutingAsync` returning `legacy()` unconditionally, and no stamp; S-0 has landed,
so `DefaultComplexity` exists. Rows marked **red under the scaffold** must fail by the named assertion at CP-3; the
others pass there because they assert today's path is kept. Seeds: a role cell is a direct `ComplexityChains` insert
as `SeedHardChainAsync` does, with `Role` and `Complexity = Medium`; a hold is `SeedHoldAsync(db, kind, alias, until:
null)`; `settled`/`conflicted` are a seeded Worker task with `WorktreePath`, `WorktreeBranch`, `MergeTargetRef` as
`CommitOnSettlePolicyTests.cs:176-184` seeds them, plus `ParentSessionId` = a seeded session when a note is asserted.

- V-A1 `A_plain_create_walks_the_role_row_at_the_default_tier` | I | Plan/Medium `[Codex/Frontier, ClaudeCode/High]`
  Human; create `new CreateAgentTaskRequest("plan it", Role: Plan, Workspace: Shared)` → `Status Queued`, `AgentKind
  Codex`, `ModelLevel Frontier`, `Routing != null`, `Routing.Complexity == Medium`, `Routing.Source ==
  "chain:Plan/Medium"`, `Routing.Walked == true`; stored `task.Complexity == Medium`, `task.ExplicitAgentKind == null`;
  Created event detail contains `(matrix default Medium)` and not `(role policy)`. Red under the scaffold. Mutation:
  skip the stamp → `ClaudeCode/Frontier`, `Routing null` → red.
- V-A2 `An_unset_row_keeps_the_role_policy_path` | I | no rows; create Plan as above → `Queued`, `ClaudeCode/Frontier`,
  `Routing == null`, `task.Complexity == null`, Created detail contains `(role policy)` and not `(matrix default`;
  `Blocked` event count 0. Mutation: stamp unconditionally → `Blocked` → red.
- V-A3 `An_exhausted_default_row_blocks_with_the_cell_name` | I | Code/Medium `[ClaudeCode/Frontier]`; hold `fable`;
  session seeded and the caller carries it; create Code → `Status Blocked`, `Warning` starts with `routing exhausted: `
  and contains `Code/Medium chain` and `fable`; `task.Complexity == Medium`; exactly one `Blocked` event; exactly one
  `SessionQueuedMessages` row with `SourceTaskId == task.Id`, `AgentSessionId == the seeded session`, `Status Pending`,
  `NoteHeader != null`, `ContentDigest == DelegationNoteDigest.Compute(task.FailureReason)`, `Body` containing
  `Code/Medium`. Red under the scaffold. Mutation: force `RefuseIfExhausted` on stamped creates →
  `RoutingExhaustedException` → red.
- V-A4 `An_explicit_kind_is_not_stamped_and_warns` | I | Plan/Medium `[Codex/Frontier]`; create Plan with `AgentKind:
  ClaudeCode` → `Queued`, `ClaudeCode/Frontier` (RolePolicy level), `Routing == null`, `task.Complexity == null`,
  `task.ExplicitAgentKind == ClaudeCode`, `Warning` contains `explicit kind/level: the Plan/Medium row was not
  consulted`. Red under the scaffold (the warning). Mutation: stamp despite the explicit kind → the walk runs under the
  explicit kind, `Routing != null` → red (the 422 at `:737-746` ran earlier and does not fire for a stamp inserted at
  `:917`).
- V-A5a `A_single_candidate_required_stage_pin_is_an_explicit_pair_and_is_not_stamped` | I | Plan/Medium
  `[Codex/Frontier]`; stage pin `PutRoutingPinRequest(Plan, Provenance: Human, Strength: Required, AgentKind:
  ClaudeCode, ModelLevel: High, Reason: "opus for Plan")`; create Plan → `ClaudeCode/High`, `Routing == null`,
  `task.Complexity == null`, `task.RoutingPinId == null`, `task.ExplicitAgentKind == null`, Created detail contains
  `(explicit override)` and `[pin=the human required stage-wide Plan routing pin`, and not `bypassed`. Passes under
  the scaffold. Mutation: evaluate the predicate before the overlay → stamped, `Routing != null` → red.
- V-A5b `A_required_candidate_list_plus_a_row_is_stamped_and_walks_the_pin` | I | Plan/Medium `[Codex/Frontier]`;
  stage pin `Required` with `Candidates: [ClaudeCode/High, ClaudeCode/Frontier]`; create Plan → `ClaudeCode/High`,
  `Routing.Source == "pin:stage Plan"`, `Routing.Walked == true`, `Routing.Candidates` two entries both origin `pin`,
  `Warning` contains `complexity chain bypassed by`, `task.Complexity == Medium`, `task.RoutingPinId == pin.Id`. Red
  under the scaffold. Mutation: pass `Decision.None` to `WalkAsync` → `Codex`, no bypass warning → red.
- V-A6 `The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled` | U |
  `[Arguments]` × 8 over `AgentTaskService.MatrixDefaultApplies(request, liveFollowUp)`: (Worker, Plan, nothing set,
  false) → true; (Worker, Check, …) → false; (Orchestrator, Plan, …) → false; (Worker, Plan, `AgentId` set) → false;
  (Worker, Plan, `AgentKind: Codex`) → false; (Worker, Plan, `ModelLevel: High`) → false; (Worker, Plan,
  `IgnoreModelDisabled: true`) → false; (Worker, Plan, nothing set, liveFollowUp `true`) → false. Executed results: 8.
  Red under the scaffold on the first row only. Mutation: drop the specialist clause → the Check row returns true → red.
- V-A7 `An_orchestrator_create_keeps_role_policy_and_the_level_floor` | I | Plan/Medium `[ClaudeCode/Low]`; create
  `new CreateAgentTaskRequest("plan it", Kind: AgentTaskKind.Orchestrator, Role: Plan, Workspace: Shared)` →
  `ModelLevel == Frontier` (RolePolicy Plan), `Routing == null`, `task.Complexity == null`; a second create with
  `DelegationSettings.RolePolicy["Plan"].Level = Low` in the fixture → `ModelLevel == High` (`MinOrchestratorLevel`).
  If `Kind: Orchestrator` needs more setup in this fixture, Code extends the fixture, not the assertions. Mutation:
  stamp orchestrators → `Low` → red.
- V-A8a `The_settlement_commit_child_takes_the_commit_row` | I | Commit/Medium `[Codex/Frontier]`; no pin;
  `service.CreateCommitTaskAsync(settled, "reason", ["a.txt"], null, null, ct)` then `db.SaveChangesAsync()` →
  returned task `AgentKind Codex`, `ModelLevel Frontier`, `Complexity == Medium`, `Status Queued`, `RoutingPinId ==
  null`; with no Commit row → `ClaudeCode/Medium` (RolePolicy Commit), `Complexity == null`. Red under the scaffold
  (first half). Mutation: return `legacy()` unconditionally → `ClaudeCode` → red.
- V-A8b `A_required_pin_with_candidates_keeps_the_commit_child_on_todays_pin_path` | I | `[Arguments]` × 4:
  `no-row-list-pin` (no row; Required stage Commit pin `Candidates: [ClaudeCode/Frontier, ClaudeCode/High]`, the live
  shape) → `ClaudeCode/Frontier`, `RoutingPinId == pin.Id`; `no-row-single-pin` (no row; Required `Codex/Frontier`) →
  `Codex/Frontier`, `RoutingPinId == null`; `row-and-list-pin` (Commit/Medium `[Grok/Frontier]` plus the list pin) →
  `ClaudeCode/Frontier`, `RoutingPinId == pin.Id`; `row-and-single-pin` (Commit/Medium `[ClaudeCode/High]` plus the
  single pin) → `Codex/Frontier`, `RoutingPinId == null`. In each row: `Complexity == null`, `Status Queued`, no
  `Blocked` event. Executed results: 4. Passes under the scaffold. Mutation: walk the row despite the Required pin →
  row `row-and-single-pin` gives `ClaudeCode/High`, `Complexity Medium` → red.
- V-A10 `A_stage_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` | I | standing `Agent` row (recipe
  `RoutingPinServiceTests.cs:233-248` with `IsPoolDelegate = false`, `Kind ClaudeCode`); stage pin
  `PutRoutingPinRequest(Plan, Human, Required, AgentId: agent.Id)`; Plan/Medium `[Codex/Frontier]`; create Plan with
  no `AgentId` → `AgentKind ClaudeCode`, `task.AgentId == agent.Id`, `task.Complexity == null`, `Routing == null`,
  Created detail contains `[pin=`; the standing agent's `Kind` row is unchanged. If a standing-agent create needs more
  fixture, Code extends the fixture, not the assertions. Passes under the scaffold. Mutation: evaluate the predicate
  before the overlay → `Codex` → red.
- V-A11 `A_card_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` | I | as V-A10 with `SeedCardAsync(db,
  "CARD-9097A")` and a card pin (`Card: "CARD-9097A"`); create with `Card: "CARD-9097A"` → same assertions; a stage pin
  absent. Passes under the scaffold. Mutation: same as V-A10.
- V-A12a `The_server_created_merge_child_takes_the_merge_row` | I | Merge/Medium `[Codex/Frontier]`; no pin;
  `service.CreateMergeTaskAsync(conflicted, ["a.cs"], ct)` → `AgentKind Codex`, `ModelLevel Frontier`, `Complexity ==
  Medium`, `Status Queued`, `RoutingPinId == null`. Red under the scaffold. Mutation: keep `ResolveLevel` only →
  `ClaudeCode/High` → red.
- V-A12b `An_empty_matrix_or_a_required_pin_keeps_the_merge_child_on_todays_program_default` | I | `[Arguments]` × 5:
  `none` (no row, no pin); `stage-list-pin` (no row; Required stage Merge pin `[ClaudeCode/Frontier, ClaudeCode/High]`);
  `card-pin` (no row; `SeedCardAsync(db, "CARD-9097M")`, `conflicted.CardId` = that card, Required card pin
  `Codex/Frontier`); `policy-kind-codex` (no row; fixture `RolePolicy["Merge"].Kind = Codex`); `row-and-required-pin`
  (Merge/Medium `[Codex/Frontier]` plus the stage list pin). Each → `ModelLevel High`, `AgentKind ClaudeCode` (the
  entity default; today's producer assigns none), `Complexity == null`, `RoutingPinId == null`, `Status Queued`.
  Executed results: 5. Passes under the scaffold. Mutation: resolve the Merge pin in the legacy arm → row
  `stage-list-pin` gives `Frontier` → red.
- V-A13 `An_exhausted_commit_row_blocks_the_commit_child` | I | Commit/Medium `[ClaudeCode/Frontier]`; hold `fable`;
  `settled.ParentSessionId` = a seeded session; `CreateCommitTaskAsync` → `Status Blocked`, `FailureReason` starts
  with `routing exhausted: ` and contains `Commit/Medium chain`, `Complexity == Medium`, exactly one `Blocked` event for
  the child, exactly one `SessionQueuedMessages` row with `SourceTaskId == child.Id` for the parent session, no Warning
  event containing `using role policy`. Red under the scaffold. Mutation: fall back to the RolePolicy pair → `Status
  Queued` → red.
- V-A14 `A_held_commit_row_head_falls_to_the_next_candidate_with_the_skipped_warning` | I | Commit/Medium
  `[ClaudeCode/Frontier, Codex/Frontier]`; hold `fable` → `Codex/Frontier`, `Complexity == Medium`, Warning event
  contains `skipped fable`. Red under the scaffold. Mutation: take `Candidates[0]` without the walk → `ClaudeCode` →
  red.
- V-A15 `A_chain_read_failure_keeps_the_required_commit_pin` | I | Commit/Medium `[ClaudeCode/High]` present; Required
  stage Commit pin `Codex/Frontier`; the service is built with a `ComplexityRoutingService` over a second
  `AppDbContext` that the test disposes before the call (the `RoutingPinService` stays on the live context) →
  `Codex/Frontier`, `Complexity == null`, `RoutingPinId == null`, `Status Queued`, no `Blocked` event, a Warning event
  containing `Routing read failed`. Red under the scaffold (no warning). Mutation: catch into the RolePolicy pair
  instead of `legacy()` → `ClaudeCode/Medium` → red.

**S-B — `tests/Antiphon.Tests/Application/ComplexityEffectiveComposeTests.cs` (U; CP-10 red over a compile-only
stub returning an empty list and a `PolicyTarget` returning a resolution with null fields) and
`ComplexityEffectiveHttpTests.cs` (A)**

- V-B1 `A_required_pin_replaces_the_cell_and_sources_pin` | U | `Decision` hand-built as the
  `ComplexityRoutingComposeTests.Decision(pin, identifier)` helper builds one (`:358`; stage pin Required Human,
  candidates `[Codex/Frontier]`); chain `[ClaudeCode/Frontier]` with label `Plan/Hard`, resolvedFrom `role`; policy
  `PolicyResolution(ClaudeCode, Frontier, "fable", null)`; `ComplexityEffective.Compose(decision, chain, "Plan/Hard",
  "role", policy, isDefaultTier: false)` → `Source == "pin"`, `Candidates` is `[Codex/Frontier]`, `StagePin!.Id ==
  pin.Id`, `GovernsDefault == false`, `WithheldReason == null`. Mutation: return the chain → `Source "role"` → red.
- V-B2 `A_preferred_pin_prepends_and_sources_pin_plus_chain` | U | Preferred pin `[Grok/Frontier]`; chain
  `[ClaudeCode/Frontier, Grok/Frontier]` (`any`) → `Source == "pin+chain"`, candidates `[Grok/Frontier,
  ClaudeCode/Frontier]` (deduplicated), `StagePin` set. Mutation: ignore the pin → first candidate `ClaudeCode` → red.
- V-B3 `No_pin_and_no_cell_falls_back_to_role_policy` | U | `Decision.None`, chain `[]` resolvedFrom `none`, policy
  `(ClaudeCode, High, "opus", null)` → `Source == "rolePolicy"`, candidates `[ClaudeCode/High]`, `StagePin == null`;
  `Decision.None` with chain `[X]` resolvedFrom `any` → `Source == "any"`. Mutation: return an empty list for `none` →
  `Source` missing `rolePolicy` → red.
- V-B4 `Governs_default_is_true_only_on_the_default_tier` | U | same inputs with `isDefaultTier: true` → `true`;
  `false` → `false`. Mutation: `true` for both → red on the second.
- V-B10 `An_unlaunchable_policy_is_withheld_with_its_reason` | U | `[Arguments]` × 3 over
  `RolePolicyReadService.PolicyTarget(settings, Plan)` then `Compose(Decision.None, [], "Plan/Medium", "none", policy,
  true)`: `Raw/Frontier` (`RolePolicy["Plan"] = { Kind = Raw, Level = Frontier }`) → resolution `Kind null`, `Level
  null`, `Alias null`, `WithheldReason == "Plan policy names Raw/Frontier, which delegation cannot launch"`;
  `OpenCode/Medium` → `"Plan policy names OpenCode/Medium, which delegation cannot launch"`; `ClaudeCode/999` (`Level
  = (AgentModelLevel)999`, no kind) → `"Plan policy names ClaudeCode/999, which delegation cannot launch"`. Each →
  `Source == "rolePolicy"`, `Candidates` empty, `WithheldReason` equals the resolution's string. A fourth assertion in
  the first row: the shipped Plan policy → `(ClaudeCode, Frontier, "fable", null)`. Executed results: 3. Mutation:
  build the resolution through `ResolveAgainstRolePolicy` → candidates non-empty, reason null → red.
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
  `effective.candidates[0].alias == "fable"`, `effective.stagePin` null, `effective.withheldReason` null; `chains[0]`
  `effective.source == "role"`; GET `/api/complexity-chains` (list) → `defaultComplexity == "Medium"` and each entry's
  `effective` is JSON null. `finally` DELETE the cell, S and C. Mutation (PC-22): resolve the decision with the role's
  first active card pin's `CardId` instead of null → `stagePin.id == C` → red.
- V-B6 `Put_twice_is_last_writer_wins_without_a_409` | A | PUT `/api/complexity-chains/Debug/Easy`
  `{candidates:[{Codex,Frontier}], provenance:"Human", reason:"first"}` → 200, read `updatedAt` U1; PUT the same cell
  `{candidates:[{Grok,Frontier}], provenance:"Human", reason:"second"}` → 200; GET `?role=Debug` → Easy `candidates`
  is `[Grok/Frontier]`, `reason == "second"`, `updatedAt >= U1`. `finally` DELETE. Mutation: refuse a second Human
  write with 409 → red. (OD-4's statement as a test; the migration's PUTs depend on it.)

**S-C1 — `routingSettingsModel.test.ts` (M), `RoutingSettingsTab.test.tsx` (E), `complexityChains.test.tsx` (R)**

- V-C1 `C097M V-C1 <case>` `effectiveCellLabel` | M | rows: `pin` Required Human `[Codex/Frontier gpt-6-astra]` →
  `Required Human pin → Codex/Frontier (gpt-6-astra)`; `pin+chain` Preferred Human `[Grok/Frontier grok-4.7, …]` →
  `Preferred Human pin → Grok/Frontier (grok-4.7), then this row`; `rolePolicy` `[ClaudeCode/High opus]` → `Falls back
  to RolePolicy → ClaudeCode/High (opus)`; `rolePolicy` with `withheldReason: 'Plan policy names Raw/Frontier, which
  delegation cannot launch'` and no candidates → `Falls back to RolePolicy → not launchable: Plan policy names
  Raw/Frontier, which delegation cannot launch`; `role` → `Own rule`; `any` → `Inherits Any role`; `config` →
  `Configuration fallback`. Mutation: ignore `effective.source` → red on the first row.
- V-C2 `C097M V-C2 <case>` `unsetCellLabel(tier, governsDefault)` and `nonComplexityBoundary(tier)` | M | (`Easy`,
  false) → `Unset — a -Complexity Easy dispatch blocks`; (`Medium`, true) → the V-C1 `rolePolicy` label is used
  instead; `nonComplexityBoundary('Medium')` equals `An ordinary Worker dispatch without -Complexity, -Kind/-Level,
  -Agent or ignoreModelDisabled walks the Medium row; explicit asks, orchestrators, standing agents and specialist
  roles keep their existing routing path.` Mutation: drop the tier from the sentence → red.
- V-C3 `C097M V-C3 card-pin-count` `cardPinCountLabel(role, pins)` | M | 0 → `null`; 1 → `1 card pin`; 2 → `2 card
  pins`. Mutation: plural for one → red.
- V-C4 `C097M V-C4 pin-overlay-first` | E | `serveRouting` with Plan Hard `effective: { source: 'pin', stagePin:
  {…Required Human ClaudeCode/High}, candidates: [opus], governsDefault: false, withheldReason: null }` →
  `routing-matrix-cell-Plan-Hard` renders `Required Human pin → ClaudeCode/High (opus)` as its first text node and `Own
  rule` after it. Mutation: render the stored list first → red.
- V-C5 `C097M V-C5 default-column-and-fallback` | E | list `defaultComplexity: 'Medium'`; Plan Medium
  `effective: { source: 'rolePolicy', candidates: [fable], stagePin: null, governsDefault: true, withheldReason: null }`;
  Plan Easy `effective: { source: 'rolePolicy', …, governsDefault: false }` and `resolvedFrom 'none'` → column header
  `Medium (default for an ordinary Worker dispatch without -Complexity)`; `routing-matrix-cell-Plan-Medium` shows
  `Falls back to RolePolicy → ClaudeCode/Frontier (fable)`; `routing-matrix-cell-Plan-Easy` shows `Unset — a
  -Complexity Easy dispatch blocks`. Mutation: drop the header suffix → red.
- V-C6 `C097M V-C6 card-pin-count-in-row` | E | `defaultPins` (one Code card pin) → `routing-matrix-row-Code` contains
  `1 card pin`; `routing-matrix-row-Plan` does not. Mutation: count stage pins too → `2 card pins` → red.
- Rewritten with reason (the copy they assert is the copy S-C1 changes): `shows inheritance, configuration fallback,
  and unset text` (`:347`: Plan-Easy and any-Easy now read `Unset — a -Complexity Easy dispatch blocks`; the any-role
  row has no `effective`); `wording for Required bypass versus Preferred prepend, and names Code → Grok` (`:373`:
  `pinEffectCopy` becomes `Required Human pin: Code → Grok. Overrides each Code cell until cleared.` / `Preferred Human
  pin: Plan → ClaudeCode/Frontier. Tried before each Plan cell.`); `states the D6 propagation boundary and the D7
  non-complexity RolePolicy boundary` (`:447`: asserts the V-C2 sentence).
- R-C1 the `complexityChains.test.tsx` cases run unchanged (the DTO gains optional fields only).

**S-C2 (optional) — `ComplexityChainRowEditor.test.tsx` (E), `client/src/api/routingPins.test.ts` (E),
`routingSettingsModel.test.ts` (M)**

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
- V-C11 `C097M V-C11 no-pin-no-checkbox` | E | `stagePin: null` → no checkbox in the DOM; confirm copy does not contain
  `pin`. Mutation: render the checkbox regardless → red.
- V-C12 `useClearRoutingPin DELETEs /routing-pins/<id> and invalidates pins, role-policy and complexity-chains` | E |
  recipe from `complexityChains.test.tsx:166-190`; after the mutation msw records a second GET on `/api/routing-pins`,
  `/api/role-policy` and `/api/complexity-chains`. Mutation: invalidate pins only → no second complexity-chains GET →
  red.
- V-C13 `C097M V-C13 <case>` `rowCellRequests` and `rowReplaceCopy` | M | `rowCellRequests('Review', cands, 'r')` →
  three entries `{ role: 'Review', complexity: 'Hard'|'Medium'|'Easy', candidates, reason: 'r', notAfter: null }` in
  that order; `rowReplaceCopy('Review', pin)` contains `Hard, Medium and Easy`, `replaces`, `stage pin` and
  `Codex/Frontier`; `rowReplaceCopy('Review', null)` has no `pin`. Mutation: drop Easy → red.
- R-C2 the `ComplexityChainCellEditor.test.tsx` cases run unchanged; `ComplexityChainCellEditor.tsx` is not modified
  (the row editor builds its draft from the exported `routingSettingsModel.ts` helpers).

**S-D — `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs` (S; `ScriptedApi` added in its own file)**

- V-D1 `Set_All_puts_Hard_Medium_Easy_in_order` | S | `set -Role Review -Complexity All -Candidates Codex/Frontier
  -Reason r` → `Requests` is exactly `[PUT /api/complexity-chains/Review/Hard, PUT …/Medium, PUT …/Easy]`, each body
  `provenance Human`, `reason r`, one candidate `Codex/Frontier`; exit 0; output names the three tiers. Mutation: PUT
  Hard only → red.
- V-D2 `Set_All_stops_at_the_first_failing_tier_and_names_written_tiers` | S | `Respond(PUT, "/Review/Medium", 422,
  {"errors":{"Candidates":["boom"]}})` → exactly two PUTs; exit 1; output contains `Hard written; Medium refused:` and
  `Easy not written`. Mutation: continue after the 422 → three PUTs → red.
- V-D3 `ClearStagePin_requires_All_and_a_named_role` | S | `set -Role Review -Complexity Hard -Candidates
  Codex/Frontier -ClearStagePin` → exit 1, `RequestCount == 0`, output names `-Complexity All`; `set -Role Any
  -Complexity All … -ClearStagePin` → exit 1, `RequestCount == 0`; `set -Complexity All … -ClearStagePin` (no role) →
  exit 1, `RequestCount == 0`. Mutation: drop the refusal → requests recorded → red.
- V-D4 `ClearStagePin_deletes_the_stage_pin_only_after_three_2xx` | S | pins store holds a stage Review pin (id
  `stage-1`, `cardId null`) and a card Review pin (id `card-1`); `set -Role Review -Complexity All … -ClearStagePin` →
  `Requests` is `[PUT Hard, PUT Medium, PUT Easy, GET /api/routing-pins?role=Review, DELETE /api/routing-pins/stage-1]`;
  no DELETE of `card-1`; exit 0. Mutation: DELETE before the PUTs → order red.
- V-D5 `ClearStagePin_is_skipped_when_a_tier_failed` | S | 422 on Easy → no GET, no DELETE; exit 1; output says the
  pin was kept. Mutation (PC-28): DELETE anyway → red.
- V-D6 `Get_Role_prints_the_effective_line` | S | `GET ?role=Review` answered with a row view whose Medium has
  `effective { source: "pin", candidates: [Codex/Frontier gpt-6-astra] }` → output contains `Review/Medium` and
  `effective: pin Codex/Frontier (gpt-6-astra)`; a row with `effective` null prints no `effective:` line. Mutation:
  print the stored list as effective → red.
- V-D7 `Clear_All_deletes_three_cells` | S | `clear -Role Review -Complexity All` → three DELETEs in tier order; exit 0.
  Mutation: DELETE Hard only → red.
- R-D the nine existing methods (`C470_mutation_cell_round_trip` … `Set_without_candidates_is_refused_locally`,
  `:22-138`) run unchanged.

**S-E — `tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs` (S; same `ScriptedApi`, store seeded with 13
stage pins shaped as the live census plus one card pin; `-OutDir` under a temp directory)**

- V-E1 `Dry_run_writes_nothing_and_records_before_images` | S | default verb → `Requests` contains only GETs
  (`/api/routing-pins`, one `?role=<R>` per eligible role); exit 0; the snapshot JSON has `apiBase`, `createdAt`, 13
  `pins`, 39 `cellsBefore` entries each `present: false`, 39 `plan` entries with `provenance "Human"` and a `reason`
  ending `(migrated from stage pin <id>)`; the output table has 13 rows and the snapshot path. Mutation: PUT during
  the dry run → red.
- V-E2 `Manual_and_card_pins_are_listed_and_never_written` | S | store pins for Plan (forbid list), Code (`notAfter`),
  Review (`agentId`) plus the card pin → those three rows read `manual`; `-Apply` → no PUT for those roles; the card
  pin appears nowhere in the table or the snapshot's `plan`. Mutation (PC-29): migrate a forbid-list pin → PUT recorded
  → red.
- V-E3 `Apply_puts_three_cells_per_role_with_human_provenance_and_the_migration_reason` | S | `-Apply` → per eligible
  role PUT Hard, Medium, Easy in that order with `provenance Human`, `reason` = pin reason + ` (migrated from stage pin
  <id>)`, candidates equal to the pin's; the store holds 39 cells afterwards; exit 0. Mutation: write `provenance Auto`
  → red.
- V-E4 `A_non_2xx_stops_the_run_and_names_the_cells_written` | S | `[Arguments("apply")]`: `Respond(PUT,
  "/TestDesign/Medium", 500)` → requests stop at that PUT; output contains `written:` with `TestDesign/Hard` and `not
  written:` with `TestDesign/Easy`; exit 1; no DELETE. `[Arguments("restore")]`: after a full apply from a snapshot with
  one present before-image (Plan/Medium), `Respond(DELETE, "/TestDesign/Easy", 500)` → `-Restore` stops at that DELETE,
  output names the cells restored before it, exit 1. Executed results: 2. Mutation (PC-30): continue past the 500 → a
  request after the failure → red.
- V-E5 `A_second_apply_sends_the_same_puts_and_leaves_the_store_unchanged` | S | `-Apply` twice → the second run's
  PUTs equal the first run's (same paths, same bodies); the store's 39 cells are byte-equal before and after the
  second run; exit 0 both times. Mutation: skip cells whose stored body equals the plan → zero PUTs on the second run →
  red (the contract is a full overwrite, not a skip).
- V-E6 `Restore_re_puts_present_before_images_and_deletes_absent_ones` | S | store seeded with a prior Human
  Plan/Medium cell `[Grok/Frontier]`; dry run (records `present: true` with those candidates), `-Apply` (overwrites
  it), then `-Restore -Snapshot <that file>` → `Requests` after restore: PUT `/api/complexity-chains/Plan/Medium` with
  `candidates [Grok/Frontier]`, `provenance Human` and the seeded reason, and DELETE for each of the other 38 cells; no
  request on `/api/routing-pins`; the store's cells equal the initial seed; exit 0; a second `-Restore` sends the same
  requests and exits 0. Mutation (PC-31): DELETE Plan/Medium instead of PUT → red.

**S-F — `OrchestratorInstructionsRendererTests` (U), `OrchestratorInstructionsRefreshTests` (H),
`OrchestratorInstructionsDeltaTests` (U)**

- V-F1 `Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (edited with reason: the matrix
  section renumbers 5–7 to 6–8) | U | `Sample()` gains `Matrix: new MatrixSection("Medium", [new
  MatrixInstructionRow("Plan", Hard: "Codex/Frontier (gpt-6-astra)", Medium: "…", Easy: "unset", Effective:
  "ClaudeCode/High (opus)", Source: "stage pin")])`; body contains `## 5. Role × complexity matrix`, `default Medium`,
  `GET /api/complexity-chains`, `- Plan:`, `via stage pin`, `## 6. Routing pins`, `## 7. Model levels`, `## 8. Operator
  standing instructions`. Mutation: omit the section → red.
- V-F7 `The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` | U | `Sample()` plus a matrix of
  14 rows whose three tiers each list 8 candidates of the form `ClaudeCode/Frontier (fable)`. (a) `Render(snapshot)`
  at the default cap → the 14 `- <Role>:` lines are present, each `<= 160` UTF-8 bytes, and a tier with 8 candidates
  renders three aliases then `+5`. (b) `full = Encoding.UTF8.GetByteCount(Render(snapshot))`; `Render(snapshot, full -
  1)` → byte count `<= full - 1`, the body contains `… 14 rows: GET /api/complexity-chains` at an index greater than
  that of `## 5. Role × complexity matrix` and less than that of `## 6. Routing pins`, contains no `- Plan:` line, and
  contains no `more: GET /api/model-availability` (the holds step was not reached). No universal byte figure is
  asserted; `Sample()`'s few card pins are below the 20-per-board first step, so the matrix step is the one that
  fits. Mutation (PC-35): drop the per-line cap → a line longer than 160 bytes in (a) → red.
- V-F2 `Covered_writes_each_regenerate_and_notify("complexity-chain")` | H | new `[Arguments("complexity-chain")]`;
  `CoveredWriteAsync` arm: `scope.ServiceProvider.GetRequiredService<ComplexityChainService>().UpsertAsync(Plan,
  Medium, new PutComplexityChainRequest([new(Codex, Frontier)], Human, "cell"), null, ct)`, `WhenIdleAsync`, return
  `"matrix:"`; `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside
  `ModelAvailability` (`:173`). The existing assertions hold: `Revision > 0`, `SubmittedBodies.Count == 1`, the body
  contains `matrix:` and the written path. Mutation (PC-33): drop the signal from `UpsertAsync` →
  `SubmittedBodies.Count == 0` → red.
- V-F3 `An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` | U | two snapshots with
  equal `Matrix` → no line starts with `matrix:`; change Plan's Medium → one line `matrix: Plan Medium …`. Mutation
  (PC-34): `DiffMatrix` reports on equal input → first assertion red.

### Guards the regression

- R-A1 `ComplexityCreateTests` unchanged. `No_complexity_is_unchanged_when_fable_is_held` (`:230-245`) stays green
  **because** its seed is a Hard any-role chain and OD-1 is Medium; V-A2 is its explicit sibling. If OD-1 becomes
  `Hard`, that method's meaning changes and must be rewritten with the operator's decision as the reason.
- R-A2 `RoutingPinCreateTests` unchanged: pins without rows are untouched by S-A (D-2).
- R-A3 `RoutingPinCandidateCreateTests` unchanged: each method seeds a pin list or a Hard chain, none a Medium row.
- R-A4 `ComplexityDispatcherTests` unchanged: the re-walk is keyed on `task.Complexity`, which S-A sets to the same
  value a `-Complexity Medium` create would.
- R-A5 `CommitOnSettlePolicyTests` unchanged: `Merge_and_commit_children_are_created_with_Never` (`:173`) seeds no
  row, so both producers take the legacy arm (D-3 step 1).
- R-B1 `ComplexityChainRoleHttpTests` unchanged: `effective` is additive; the existing `JsonElement` reads name their
  properties.
- R-B2 `ComplexityChainServiceTests` unchanged: the optional constructor parameter defaults to null and
  `GetEffectiveAsync` without a pin service composes with `Decision.None`.
- R-B3 `RolePolicyReadServiceTests` unchanged: `PolicyTarget` is extracted from `Row` without changing its outputs;
  the withholding rows keep their literal expectations (15 `MethodDataSource` cases).
- R-C1, R-C2 as above. R-D as above.
- R-F `OrchestratorInstructionsRendererTests` other methods and `OrchestratorInstructionsRefreshTests` other methods
  unchanged (the renumbering touches one method's literals and the `Sample()` fixture; the ladder test's existing
  ordering assertions still hold because the matrix step sits between card pins and holds and its pointer is
  in-section, not a trailing notice). The notice path's behaviour stays bound by the existing
  `Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator`,
  `A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row` and
  `An_unchanged_write_neither_rewrites_nor_notifies` (`:30`, `:209`, `:179`).

### Guard inventory

| Guard | Where | Existing test |
|---|---|---|
| `-Complexity` + `-Kind/-Level` is 422 | `AgentTaskService.cs:737-746` | `ComplexityCreateTests.Explicit_kind_plus_complexity_is_422` |
| `-Complexity` + `ignoreModelDisabled` is 422 | `:748-755` | `ComplexityCreateTests.IgnoreModelDisabled_plus_complexity_is_422` |
| Required pin + chain → pin wins with bypass warning | `RoutingCandidates.cs:58-63`; `AgentTaskService.cs:934-941` | `ComplexityCreateTests.A_required_pin_plus_complexity_uses_the_pin_pair_and_warns_bypass` |
| Explicit kind vs Required pin → 409 | `RoutingPinService.cs:345-358` | `RoutingPinCreateTests.An_explicit_kind_against_a_required_pin_is_409_routing_pin_conflict` (`:80`) |
| Agent-only pin fills `AgentId`; a pool delegate cannot be pinned | `AgentTaskService.cs:831`; `RoutingPinService.cs:213-227` | `RoutingPinServiceTests.A_pool_delegate_cannot_be_pinned_to_a_stage` (`:228`) |
| Standing agent keeps its own kind at dispatch | `AgentTaskDispatcher.cs:7314-7331` | — (noted; V-A10 asserts the create side) |
| Exhausted chain → Blocked, resumes on hold clear | `AgentTaskService.cs:1448-1453`; `AgentTaskDispatcher.cs:1749-1779` | `ComplexityCreateTests.All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note`; `ComplexityDispatcherTests.Clearing_a_hold_requeues_a_routing_blocked_task` |
| Held single Required candidate stays Queued with Held | `AgentTaskDispatcher.cs:709-748` | `ComplexityDispatcherTests.A_required_pinned_chain_task_stays_Queued_with_Held_when_the_pin_is_held` |
| Reroute ends governance | `AgentTaskService.cs:2813-2814` | `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` |
| Auto never overwrites Human; empty/duplicate 422 | `ComplexityChainService.cs:176-201` | `ComplexityChainHttpTests.Auto_over_human_is_409_and_empty_or_duplicate_is_422` (`:81`) |
| `?role=` view resolves role → any → none | `ComplexityChainService.cs:111-134` | `ComplexityChainRoleHttpTests.Get_role_Plan_returns_effective_resolvedFrom` (`:91`) |
| RolePolicy read withholds unsupported kinds and undefined levels | `RolePolicyReadService.cs:51-69`, `:78-98` | `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` (`:231`) |
| Undefined policy level refuses the create | `AgentTaskService.cs:3983-3991` | `RolePolicyInvalidValuesCreateTests.post_agent_tasks_refuses_an_unusable_policy_value_with_a_named_422_and_writes_nothing` (`:84`) |
| Pin write regenerates instructions | `RoutingPinService.cs:48-49`, `:270` | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify("routing-pin")` |
| Unchanged write neither rewrites nor notifies | `OrchestratorInstructionsService.cs:156-157` | `OrchestratorInstructionsRefreshTests.An_unchanged_write_neither_rewrites_nor_notifies` (`:179`) |
| Oversized body truncates in ladder order | `OrchestratorInstructionsRenderer.cs:43-87` | `OrchestratorInstructionsRendererTests.Oversized_snapshot_truncates_in_priority_order_under_the_byte_cap` (`:82`) |
| Pin PUT rejects shorthand plus candidates | `RoutingPinService.cs:666-671` | — (noted; the migration writes no pin body, plan D-9) |
| Script refuses a naive `-NotAfter` before any request | `scripts/complexity-chain.ps1:209` | `ComplexityChainScriptTests.A_naive_NotAfter_is_refused_before_any_request` (`:63`) |

### Positive controls

The plan's PC-1–PC-38 table is the binding list (one per behaviour; exact method names above). PC-6 binds to V-A6's
`[Arguments]` row `Check`; PC-9 to V-A9; PC-10 to V-A8b's row `row-and-single-pin`; PC-13 to V-A12b's row
`stage-list-pin`; PC-21 to V-B10's row `Raw/Frontier`; PC-22 to the concrete wrong-grain query named in the plan;
PC-28 to V-D5 (R2-11); PC-30 to V-E4's row `apply`; PC-35 to V-F7's part (a); PC-24–PC-26 and PC-36–PC-38 name the
Vitest titles above. None has been run.

### Out of scope

- No transcript stands in for a test: S-D and S-E are bound through the script harness above. The one live action
  left to a human is the operator's dry run, `-Apply`, read-back and `routing-pin.ps1 clear` after §7 of the plan; the
  Code report for S-E may paste a dry run against the local stack as supplementary evidence, not as the verification
  of V-E1–V-E6.
- M-1 docs: `grep -n "Routing pins beat RolePolicy\|read-only for pins\|does not govern" docs/orchestration-loop.md`
  returns the rewritten lines; `docs/antiphon-api.md` names `effective`, `defaultComplexity`, `withheldReason`,
  last-writer-wins and, on the `/api/role-policy` block, the D-6 limitation sentence; `docs/ops-http.md:139` carries
  the same sentence; `.claude/skills/antiphon-delegate/SKILL.md:147` names the default tier with the ordinary-Worker
  qualification.
- No E2E (the Routing tab has none). No Mutation run in this card. No concurrency token (OD-4). No per-row strength
  (OD-2). No hub event (D-11). No change to `GET /api/role-policy`, the Role policy section's effective column or the
  delegate modal (D-6; CARD-1250). No new delivery, recovery or notice-ordering mechanism (D-10). The attention
  feed is covered by the existing `ComplexityAttentionTests` and is not re-tested for stamped tasks (their rows carry
  `Complexity`, the same key).

### Checkpoints

The closed list is the plan's `### Checkpoints` table (CP-1–CP-23); this amendment adds no row. Build reuse is written
as `CP-n` in the Build column with the same `After` group as row n (`PlanTableImporter.cs:200-211`). CP-3/CP-4 execute
17 methods and **31 results** (V-A6 carries 8 `[Arguments]`, V-A8b 4, V-A12b 5); `Min` is 17. V-A6 is the class's one
`[Category("Unit")]` method; the class filter runs it with the integration methods. CP-10/CP-11 execute 5 methods and
7 results (V-B10 carries 3). CP-18 executes 6 methods and 7 results (V-E4 carries 2). Regression `Min` values are the
`[Test]` method counts read from master `d317c3f59`, as the plan states; `[Arguments]` and `MethodDataSource` expansion
is recorded by Code from the runner's output.

### Cost

As the plan states: ordinary green rows 142 minutes, red-first rows 24 minutes, authoring 435 minutes across seven
Code dispatches (S-0 20, S-A 90, S-B 70, S-C1 60, S-D 60, S-E 60, S-F 75) plus 90 for the optional S-C2. No
production timeout or assertion is loosened. Edited existing tests, each with its reason recorded above: three
`RoutingSettingsTab.test.tsx` cases (copy change), one `OrchestratorInstructionsRendererTests` method and its
`Sample()` fixture (section renumbering), the `Covered_writes_each_regenerate_and_notify` argument list (one new write
kind).

## Appendix A — withdrawn: the 2026-10-13 Appendix A (S3 stage-pin editor)

V-11–V-15, R-3, PC-8–PC-11 and CP-5 of the 2026-10-13 design are withdrawn by plan D-7. `StagePinEditor.tsx`,
`usePutRoutingPin`, `pinRequestFrom` and `requiredConfirmCopy` are not built. The one pin write the client gains is
`useClearRoutingPin` (V-C12), used only from the optional row editor after its three cell writes succeeded (V-C9).
