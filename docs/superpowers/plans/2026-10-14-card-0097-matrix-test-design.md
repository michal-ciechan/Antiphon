# CARD-0097 verification design amendment — the matrix as the single routing surface

Plan task `c491a627` (Frontier) wrote this amendment with [the rework plan](2026-10-14-card-0097-matrix-plan.md);
task `785ac731` (Plan, Frontier) repaired both on 2026-10-14 for Final Review `2f218eeb` (F01–F12), from
`2f4d94fb0edc4b14758a613b4605a3ce2e1d3790` on `feat/card-task-785ac731`. Source census 2026-10-14, read from source
only: no build, no test run, no discovery execution. Citations hold for `origin/master` `15a9071fa` (the plan's header
says why). It amends [the 2026-10-13 test design](2026-10-13-card-0097-test-design.md): the read slices it froze
(V-1–V-10, V-16–V-30, PC-1–PC-27) stand and are landed; its Appendix A (the held S3 pin editor, V-11–V-15, R-3, CP-5)
is **withdrawn** and replaced by V-C7–V-C14 below. IDs here are prefixed by slice (V-A1, V-B1, V-C1, V-D1, V-E1, V-F1;
R-A1 …; PC-1 … PC-40 of the plan's table) and do not collide with the 2026-10-13 numbering.

**Operator decisions applied (plan §2).** OD-1 default tier `Medium`; OD-2 cells warn, pins refuse; OD-3 pins are
migrated and cleared only by the operator's command; OD-4 last-writer-wins stated and tested (V-B6, `updatedAt >= U1`);
OD-5 card pins read-only with a per-row count; OD-6 exhausted stamped create is Blocked (D-3 extends it to the Commit
and Merge children); OD-7 orchestrator creates are not stamped (consequence disclosed in plan §6.4). Post-land
Mutation is paused: PC-1–PC-40 are specified and pending; none is claimed run.

**Slice order and sizing.** CARD-1234 lands first (not this plan's work). S-0 (20 min) → S-A (90) ∥ S-B (70) → S-B2
(60) ∥ S-C1 (60) ∥ S-D (60) ∥ S-E (90) → S-C2 (90) → S-F (90). S-B reads S-0's `DefaultComplexity`; S-C1 depends on
S-B's wire contract (F-A); S-C2 on S-C1's `MatrixCell` and S-B2's `RolePolicySection` edit; S-D's `get` on F-A; S-F on
S-A, S-B, S-D and S-E. Each slice commits and pushes before its checkpoint rows run.

**Collisions (read 2026-10-14 ~08:40Z, repair time).** The census uses the Antiphon project id
(`GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, `taskScope: "project"`) and the
board task list (`GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=…`); `?unscoped=true`
reports `taskScope: "null"` and is not evidence of an idle pipeline. At repair time: CARD-1234 Review task `27930977`
Working on `feat/card-task-27930977` (that branch edits `AgentTaskService.cs:3975-4045`, `DelegationSettings.cs:1313-1340`,
`DelegateModal.tsx`, `RolePolicyEndpointTests.cs`, `docs/antiphon-api.md`, `docs/orchestration-loop.md`); CARD-0506
S5 Review task `1cb6b989` Dispatched (`tests/Antiphon.E2E` only); CARD-1213 in the Review column
(`docs/orchestration-loop.md`). S-0, S-B2 and S-F are dispatched after CARD-1234 lands; S-F also waits for CARD-1213.
The orchestrator re-reads the project-scoped routes before dispatching S-B2/S-C1/S-C2 (same-area rule for
`client/src/features/settings` and `client/src/features/delegations`).

**Ground truth confirmed from source (anchors for the recipes below).** `CreateAsync` resolves the pin decision at
`AgentTaskService.cs:770-781`, overlays it at `:803-837` (an agent-only pin fills `AgentId` at `:831`), binds a standing
agent at `:840-860` (`AgentKind = pinned.Kind` at `:859`), then opens the walk only on `request.Complexity`
(`:923-932`); the stored row carries `Complexity`, `RoutingPinId`, `ExplicitAgentKind`, `ExplicitModelLevel`
(`:1364-1367`); an exhausted walk Blocks (`:1448-1453`) with a Blocked event and parent note (`:1577-1581`,
`EnqueueBlockedParentNoteAsync :4589-4613`: `SourceTaskId`, `ContentDigest = DelegationNoteDigest.Compute(reason)`,
`NoteHeader`, `Status Pending`); the Created detail says `(explicit override)` or `(role policy)` (`:1556-1560`);
`routingPinId` is stamped only on a walked list (`:944-945`, `:1036-1037`). `CreateCommitTaskAsync(settled, reason,
dirtyPaths, headSha, stderr, ct)` (`:3811-3935`) and `CreateMergeTaskAsync(conflicted, conflictFiles, ct, …)`
(`:3706-3797`) are `internal`, already called from `CommitOnSettlePolicyTests.cs:187-199` (the server exposes
internals to `Antiphon.Tests`). `LoadChainAsync` returns `ResolvedFrom` `role`/`any`/`config`/`none` and does not
validate the role (`ComplexityRoutingService.cs:312-344`); `WalkAsync` composes the pin decision with the chain
through `RoutingCandidates.Compose` (`:211-219`; `RoutingCandidates.cs:44-97`); `ResolveAgainstRolePolicy` has no
orchestrator floor and no withholding (`:432-446`) while `ResolveLevel` clamps to `MinOrchestratorLevel` (default
`High`, `DelegationSettings.cs:967`). `RolePolicyReadService.Row`/`Effective` withhold unsupported kinds and undefined
levels (`RolePolicyReadService.cs:51-69`, `:78-98`, `:104-105`, `:133-137`). `ComplexityChainService` is constructed
with `(db, time, routing, options, logger)` in three test files (`ComplexityChainServiceTests.cs:303-312`,
`ComplexityChainRoleTests`, `MutationAdmissionTests`); the HTTP factory is `ComplexityChainApiWebAppFactory :
AntiphonWebAppFactory` (`ComplexityChainHttpTests.cs:142`), `[NotInParallel]`, `PerClass`.
`RoutingPinService.ResolveAsync(null, role, Ask(null,null,null,false))` yields the stage-pin decision
(`RoutingPinService.cs:303-404`); an agent-only stage pin is seeded as `RoutingPinServiceTests.cs:228-248` seeds a
standing `Agent` (set `IsPoolDelegate = false`) and `StagePin(role) with { AgentId }`; `SeedCardAsync` is
`RoutingPinServiceTests.cs:289`. The renderer asserts section headings literally
(`OrchestratorInstructionsRendererTests.cs:34-62`), its ladder test renders at the 16,384 default and checks cut-notice
order (`:82-135`); `Covered_writes_each_regenerate_and_notify` takes six `[Arguments]`
(`OrchestratorInstructionsRefreshTests.cs:60-78`) through `CoveredWriteAsync` (`:351-392`); the busy/turn-end recipe is
`:208-233` and the unchanged-write recipe `:190-206`; `BridgeQueueHarness` registers `ModelAvailability` but neither
chain service (`TestHelpers/BridgeQueueHarness.cs:173`), exposes `ConfigureDbContext`/`ConfigureServices` options,
`InsertTranscriptEntryAsync`, `MarkWorkingAsync`, `Queue.OnTurnEndAsync`, and its fake adapter inserts a stamped
`UserPrompt` row equal to the submitted body on each submission (`:322-349`); `OrchestratorInstructionsService`
writes the session's version column before `EnqueueAsync` (`:268-278`) and `Accept` skips a session whose column
equals the current version (`OrchestratorInstructionsRecipients.cs:106-108`). The complete-prompt oracle used
elsewhere is `TranscriptEntries.Where(t => t.AgentSessionId == … && t.Kind == TranscriptKinds.UserPrompt && t.Text ==
body)` with exactly one row (`AgentTaskLandReceiptWatermarkSafetyTests.cs:467-474`). Script harness:
`ComplexityChainScriptTests.cs` runs `scripts/complexity-chain.ps1` with `pwsh -NoProfile -NonInteractive -File`
against an `HttpListener` `StubApi` (`:150-235`; `EphemeralHttpListener.BindLoopback`), `[ParallelLimiter<ProcessSpawnLimit>]`.
Client: `MatrixCell` badge from `effectiveResolvedFrom` → `resolvedFromLabel` (`RoutingSettingsTab.tsx:504-531`;
`routingSettingsModel.ts:32-47`); pin banner `pinEffectCopy` (`:113-119`); `DraftCandidate`/`toDraft` are
module-private in `ComplexityChainCellEditor.tsx:59-75`; exported list helpers in `routingSettingsModel.ts`
(`CHAIN_KIND_OPTIONS :142`, `MAX_CHAIN_CANDIDATES :150`, `candidateListError :238`, `nextUnusedCandidate :258`);
`serveRouting`, `chain()`, `pin()`, `planEffective`, `codeEffective`, `defaultPins` fixtures
(`RoutingSettingsTab.test.tsx:50-190`); the three rewritten cases are at `:347`, `:373`, `:447`; `DelegateModal.serverRoleLevel`
(`DelegateModal.tsx:45-54`) and its C097 V16/V17/V18/V30/V27 cases (`DelegateModal.test.tsx:317-379`); msw
`onUnhandledRequest: 'error'`. Vitest case counts are read with `grep -c "^\s*it(" <file>` when Code reports; they
are not restated here.

## Verification design

### Inspection

| Bodies read | Boundary → V/R or exclusion |
|---|---|
| `AgentTaskService.cs:380-400`, `:480-490`, `:700-870`, `:900-1125`, `:1355-1372`, `:1440-1460`, `:1548-1585`, `:2805-2820`, `:3700-3935`, `:3970-4045`, `:4285-4300`, `:4580-4613` | pin decision, overlay, standing agent, stamp point, Blocked path, event text, commit and merge helpers, parent note → V-A1–V-A8, V-A10–V-A16, R-A1–R-A5 |
| `DelegationSettings.cs:340-390`, `:967`, `:1252-1365` | new setting and validator row → V-A9 |
| `ComplexityRoutingService.cs:20-100`, `:150-260`, `:300-350`, `:425-450`, `:480-530` | load/compose/walk, skips, sentences → V-A3, V-A13, V-B1–V-B4 |
| `RoutingCandidates.cs:1-120` | pure precedence reused by S-B → V-B1–V-B3 |
| `ComplexityChainService.cs:25-70`, `:105-140`, `:220-275`; `ComplexityChainDtos.cs:1-50`; `ComplexityChainEndpoints.cs:15-45` | DTO shape, `?role=` view, last-writer-wins → F-A, V-B5, V-B6, R-B1, R-B2 |
| `RolePolicyReadService.cs` (all); `RolePolicyDtos.cs:20-40`; `RolePolicyEndpoints.cs:13` | withholding contract, `Effective` projection → V-B7–V-B10, R-B3, R-B4 |
| `RoutingPinService.cs:30-60`, `:160-275`, `:300-420`, `:480-500`; `RoutingPin.cs:64`, `:96-105` | stage decision, agent-only pins, DELETE by id, signal → V-A5, V-A10, V-A11, V-B5, V-C9, V-F2 |
| `AgentTaskDispatcher.cs:705-750`, `:1749-1792`, `:7314-7331` | re-walk keyed on `task.Complexity`; standing agent keeps its kind → R-A4; V-A10 rationale |
| `OrchestratorInstructionsSnapshot.cs`, `SnapshotBuilder.cs:28-42`, `:203-247`, `Renderer.cs:1-262`, `Delta.cs:1-45`, `:159-181`, `Service.cs:40-170`, `:170-285`, `:330-360`, `Recipients.cs:37-110`, `Settings.cs:1-32`, `ChannelPreamble.cs:140-147` | section, bound, ladder, signal, delta, persistence and notice boundaries → V-F1–V-F8 |
| `tests/…/ComplexityCreateTests.cs:24-247`, `:466-500`; `RoutingPinServiceTests.cs:228-262`, `:289`; `CommitOnSettlePolicyTests.cs:180-199`; `ComplexityDispatcherTests.cs` titles; `ComplexityRoutingComposeTests.cs:15-205`; `OrchestratorInstructionsRendererTests.cs:30-135`; `OrchestratorInstructionsRefreshTests.cs:1-85`, `:190-235`, `:351-392`; `OrchestratorInstructionsDeltaTests.cs` titles; `AgentTaskLandReceiptWatermarkSafetyTests.cs:455-474`; `BridgeQueueHarness.cs:39-100`, `:165-180`, `:315-350`, `:378`, `:483` | fixtures and recipes → each V |
| `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs` (all); `TestHelpers/EphemeralHttpListener.cs` | script harness recipe → V-D1–V-D7, V-E1–V-E11, R-D |
| `client/src/api/complexityChains.ts`, `routingPins.ts`, `rolePolicy.ts`; `features/settings/RoutingSettingsTab.tsx:455-545`; `routingSettingsModel.ts`; `rolePolicyModel.ts`; `ComplexityChainCellEditor.tsx:55-100`; `RolePolicySection.tsx`; `features/delegations/DelegateModal.tsx:38-140`; `test/mocks/handlers.ts:72-84`; the test files above | types, cell, editor, section, modal, fixtures → F-A client side, F-B, V-C1–V-C17, R-C1, R-C2 |
| `scripts/complexity-chain.ps1` (all), `scripts/routing-pin.ps1:1-30`, `:240-300`, `scripts/delegate.ps1:1207-1212`, `:1240-1247` | script verbs, pin clear by grain, resolved-kind echo → V-D, V-E, M-1 |
| `docs/orchestration-loop.md:878-960`, `docs/antiphon-api.md:695-714`, `docs/ops-http.md:136-141`, `docs/testing-and-build.md:400-411`, `:470`; `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:80-100`, `:190-230`, `:379-390`; `Program.cs:185-205` | doc targets, manifest reuse syntax, `import` verb → M-1, Checkpoints |

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
                         withheldReason: string | null }         // non-null only with source rolePolicy and an unlaunchable policy target
```

`source` is a fail-closed whitelist (hand-written expected rows; V-B1–V-B5, V-B10):

| stage pin | chain (`LoadChainAsync`) | composed by `RoutingCandidates.Compose` | effective |
|---|---|---|---|
| Required with ≥1 candidate | any | pin slots only | `pin`, pin candidates, `stagePin` set |
| Preferred with ≥1 candidate | non-empty | pin slots then chain, deduplicated | `pin+chain`, that list, `stagePin` set |
| Preferred with ≥1 candidate | empty (`none`) | pin slots then the policy target (`Compose` appends it when `chain` is null; S-B passes null for `none`) | `pin+chain`, `stagePin` set |
| Required with no candidate (forbid-only / agent-only) | non-empty | chain only | chain's `resolvedFrom` (`role`/`any`/`config`), `stagePin` set |
| none | non-empty | chain | `role`/`any`/`config`, `stagePin` null |
| none | empty | empty → the policy target from `RolePolicyReadService.PolicyTarget(settings, role)` | `rolePolicy`, one candidate, `stagePin` null |
| none | empty | policy target withheld (kind outside `DelegatableKinds` or undefined level) | `rolePolicy`, `candidates []`, `withheldReason` set |

`governsDefault` is `complexity == settings.DefaultComplexity` and nothing else. Card pins do not populate `stagePin`
or `candidates` (the composition asks `ResolveAsync` with `cardId: null`). Availability on each candidate is the
server's verdict (`EvaluateAvailabilityNowAsync`), as the stored list already carries.

`GET /api/role-policy` (S-B2): `RolePolicyEffectiveDto { source: 'pin' | 'matrix' | 'rolePolicy', agentKind,
modelLevel, alias, strength, withheldReason }`, where `matrix` means the default-tier effective cell supplied the head
and `strength` is null unless `source` is `pin`.

#### F-B Default msw handler and fixtures (S-C1 adds; S-C2 and S-B2 reuse)

`client/src/test/mocks/handlers.ts:76-82`: the `/api/complexity-chains` handler body gains `defaultComplexity: 'Medium'`.
`RoutingSettingsTab.test.tsx`'s `chain()` helper gains `effective: null` in its base object; `listDto`, `planEffective`,
`codeEffective` gain `defaultComplexity: 'Medium'`. New per-test rows build `effective` by spreading
`{ source, candidates, stagePin, governsDefault, withheldReason }` with literal values; no production composer is
called to derive an expectation. `stagePin` literals use the `RoutingPinRefDto` shape (`id`, `cardId`,
`cardIdentifier`, `role`, `provenance`, `strength`, `agentKind`, `modelLevel`, `notBefore`, `reason`,
`candidateCount`). `ROLE_POLICY_FIXTURE` rows gain `withheldReason: null`.

#### F-C Recipes

`A` = real HTTP through `ComplexityChainApiWebAppFactory` (`PerClass`, `[NotInParallel]`, `[Category("Integration")]`),
`JsonElement` oracles, each row written is deleted in `finally`. `I` = `AgentTaskService` over an isolated schema
exactly as `ComplexityCreateTests.cs:469-487` builds it (`Options.Create(new DelegationSettings { AllowedRoots =
[workspace.Path] })`, `ModelAvailability`, `ComplexityRoutingService`, `RoutingPinService`), `[Category("Integration")]`.
`U` = pure static calls with hand-built records, `[Category("Unit")]`. `H` = `BridgeQueueHarness`
(`[NotInParallel("MessageQueue")]`, the `OrchestratorInstructionsRefreshTests` recipes). `S` = the script harness
(`pwsh` against an `HttpListener` stub, `[ParallelLimiter<ProcessSpawnLimit>]`): S-D extends `StubApi` into
`ScriptedApi` — `Requests` (ordered `(Method, Path, Body)`), `Respond(method, pathPrefix, status, body)` per path, and
an in-memory `Cells`/`Pins` store that `GET` answers from and `PUT`/`DELETE` mutate, so a read-back sees the writes.
`M` = direct calls into `routingSettingsModel.ts`/`rolePolicyModel.ts`, `it.each` rows with literal expected strings,
titles `C097M <id> <name>`. `E` = `renderWithProviders` + `server.use` + `userEvent`, notifications mocked as the touched
files already do. Test files and titles below are **binding required** until Code creates them and records their
execution.

### Delivery inventory

Two outcomes reach a session through the existing queue; both are inventoried here and bound below.

| Outcome | Producer | Recipient | Persistence and identity | Delivery | Receipt | Loss/recovery boundary | Bound by |
|---|---|---|---|---|---|---|---|
| Blocked default-tier create (OD-6) and Blocked Commit/Merge child (D-3) | `EnqueueBlockedParentNoteAsync` (`AgentTaskService.cs:4589-4613`) in the create/settle transaction | `task.ParentSessionId` (must exist in `AgentSessions`) | `SessionQueuedMessages` row: `SourceTaskId`, `ContentDigest = DelegationNoteDigest.Compute(reason)`, `NoteHeader`, `Origin Delegation`, `Status Pending`; committed with the task | existing turn-end flush / stranded flush (`SessionMessageQueueService.OnTurnEndAsync :1554`, `FlushStrandedQueuesAsync :1599`) | one complete `UserPrompt` transcript row whose `Text` equals the row's `Body` | the row and the task commit together; a crash after commit leaves a Pending row the stranded flush delivers; no new boundary is added | V-A3, V-A13 (row fields); V-A16 (receipt through the harness) |
| Instructions notice after a cell write (D-9) | `OrchestratorInstructionsService.QueueNoticesAsync` (`:212-285`) after the state row and incident commit (`:186-204`) | `OrchestratorInstructionsRecipients.Select` (`:37-90`): standing orchestrators and orchestrator task sessions whose column lags | `OrchestratorInstructionsStates` row (version = body hash), `SessionQueuedMessages` row with `NoteHeader`, body naming `oldVersion`/`newVersion` or `changedMoreThanOnceSince` | `EnqueueAsync(WhenIdle, deliverIfIdle: true)` → inline when idle, turn-end flush when busy | one complete `UserPrompt` row equal to the queued `Body`, containing `v<version>` | today the column is advanced before `EnqueueAsync` (`:268-278`); D-12 moves it after, so a failed enqueue leaves the column behind and the next covered change re-selects the session (`Accept :106-108`); recovery is that next change, not a retry | V-F2, V-F4 (idle), V-F5 (busy), V-F6 (failed enqueue), V-F8 (unchanged write) |

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
returning `false`, `ResolveServerChildRoutingAsync` returning today's resolution with `Complexity` null and
`BlockedReason` null, and no stamp; S-0 has landed, so `DefaultComplexity` exists. Rows marked **red under the
scaffold** must fail by the named assertion at CP-3; the others pass there because they assert today's path is kept.

- V-A1 `A_plain_create_walks_the_role_row_at_the_default_tier` | I | seed role cell Plan/Medium `[Codex/Frontier,
  ClaudeCode/High]` Human (direct `ComplexityChains` insert as `SeedHardChainAsync` does, with `Role = Plan`,
  `Complexity = Medium`); create `new CreateAgentTaskRequest("plan it", Role: Plan, Workspace: Shared)` →
  `Status Queued`, `AgentKind Codex`, `ModelLevel Frontier`, `Routing != null`, `Routing.Complexity == Medium`,
  `Routing.Source == "chain:Plan/Medium"`, `Routing.Walked == true`; stored `task.Complexity == Medium`,
  `task.ExplicitAgentKind == null`; Created event detail contains `(matrix default Medium)` and not `(role policy)`.
  Red under the scaffold. Mutation: skip the stamp → `ClaudeCode/Frontier`, `Routing null` → red.
- V-A2 `An_unset_row_keeps_the_role_policy_path` | I | no rows; create Plan as above → `Queued`,
  `ClaudeCode/Frontier`, `Routing == null`, `task.Complexity == null`, Created detail contains `(role policy)` and not
  `(matrix default`; `Blocked` event count 0. Mutation: stamp unconditionally → `Blocked` → red.
- V-A3 `An_exhausted_default_row_blocks_with_the_cell_name` | I | role cell Code/Medium `[ClaudeCode/Frontier]`;
  `SeedHoldAsync(db, ClaudeCode, "fable", until: null)`; session seeded (`SeedSessionAsync`) and the caller carries
  it; create Code → `Status Blocked`, `Warning` starts with `routing exhausted: ` and contains `Code/Medium chain` and
  `fable`; `task.Complexity == Medium`; exactly one `Blocked` event; exactly one `SessionQueuedMessages` row with
  `SourceTaskId == task.Id`, `AgentSessionId == the seeded session`, `Status Pending`, `NoteHeader != null`,
  `ContentDigest == DelegationNoteDigest.Compute(task.FailureReason)`, `Body` containing `Code/Medium`. Red under the
  scaffold. Mutation: force `RefuseIfExhausted` on stamped creates → `RoutingExhaustedException` → red.
- V-A4 `An_explicit_kind_is_not_stamped_and_warns` | I | Plan/Medium cell `[Codex/Frontier]`; create Plan with
  `AgentKind: ClaudeCode` → `Queued`, `ClaudeCode/Frontier` (RolePolicy level), `Routing == null`,
  `task.Complexity == null`, `task.ExplicitAgentKind == ClaudeCode`, `Warning` contains `explicit kind/level: the
  Plan/Medium row was not consulted`. Red under the scaffold (the warning). Mutation: stamp despite the explicit kind
  → 422 `complexity cannot be combined` → red.
- V-A5a `A_single_candidate_required_stage_pin_is_an_explicit_pair_and_is_not_stamped` | I | Plan/Medium cell
  `[Codex/Frontier]`; stage pin `PutRoutingPinRequest(Plan, Provenance: Human, Strength: Required, AgentKind:
  ClaudeCode, ModelLevel: High, Reason: "opus for Plan")`; create Plan → `ClaudeCode/High`, `Routing == null`,
  `task.Complexity == null`, `task.RoutingPinId == null`, `task.ExplicitAgentKind == null`, Created detail contains
  `(explicit override)` and `[pin=the human required stage-wide Plan routing pin`, and not `bypassed`. Passes under
  the scaffold. Mutation: evaluate the predicate before the overlay → stamped, `Routing != null` → red.
- V-A5b `A_required_candidate_list_plus_a_row_is_stamped_and_walks_the_pin` | I | Plan/Medium cell `[Codex/Frontier]`;
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
- V-A7 `An_orchestrator_create_keeps_role_policy_and_the_level_floor` | I | Plan/Medium cell `[ClaudeCode/Low]`;
  create `new CreateAgentTaskRequest("plan it", Kind: AgentTaskKind.Orchestrator, Role: Plan, Workspace: Shared)` →
  `ModelLevel == Frontier` (RolePolicy Plan), `Routing == null`, `task.Complexity == null`; a second create with
  `DelegationSettings.RolePolicy["Plan"].Level = Low` in the fixture → `ModelLevel == High` (`MinOrchestratorLevel`).
  If `Kind: Orchestrator` needs more setup in this fixture (an orchestrator bundle, a runner), Code extends the fixture,
  not the assertions. Mutation: stamp orchestrators → `Low` → red.
- V-A8 `The_settlement_commit_child_takes_the_commit_row` | I | Commit/Medium cell `[Codex/Frontier]`; a settled
  Worker task and its worktree seeded as `CommitOnSettlePolicyTests.cs:180-186` seeds `conflicted`;
  `service.CreateCommitTaskAsync(settled, "reason", ["a.txt"], null, null, ct)` then `db.SaveChangesAsync()` →
  returned task `AgentKind Codex`, `ModelLevel Frontier`, `Complexity == Medium`, `Status Queued`, `RoutingPinId ==
  null`; with no Commit row → `ClaudeCode/Medium` (RolePolicy Commit), `Complexity == null` (the absent-row
  compatibility case). Red under the scaffold (first half). Mutation: keep today's `Compose(chain: null)` only →
  `ClaudeCode` → red.
- V-A10 `A_stage_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` | I | standing `Agent` row (recipe
  `RoutingPinServiceTests.cs:233-248` with `IsPoolDelegate = false`, `Kind ClaudeCode`); stage pin
  `PutRoutingPinRequest(Plan, Human, Required, AgentId: agent.Id)`; Plan/Medium cell `[Codex/Frontier]`; create Plan
  with no `AgentId` → `AgentKind ClaudeCode`, `task.AgentId == agent.Id`, `task.Complexity == null`, `Routing ==
  null`, Created detail contains `[pin=`; the standing agent's `Kind` row is unchanged. If a standing-agent create
  needs more fixture (a runner row, a workspace), Code extends the fixture, not the assertions. Passes under the scaffold.
  Mutation: evaluate the predicate before the overlay → `Codex` → red.
- V-A11 `A_card_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` | I | as V-A10 with
  `SeedCardAsync(db, "CARD-9097A")` and a card pin (`Card: "CARD-9097A"`); create with `Card: "CARD-9097A"` → same
  assertions; a stage pin absent. Passes under the scaffold. Mutation: same as V-A10.
- V-A12 `The_server_created_merge_child_takes_the_merge_row` | I | Merge/Medium cell `[Codex/Frontier]`; `conflicted`
  seeded as `CommitOnSettlePolicyTests.cs:180-186`; `service.CreateMergeTaskAsync(conflicted, ["a.cs"], ct)` →
  `AgentKind Codex`, `ModelLevel Frontier`, `Complexity == Medium`, `Status Queued`; with no Merge row →
  `ClaudeCode/High` (RolePolicy Merge), `Complexity == null`. Red under the scaffold. Mutation: keep `ResolveLevel`
  only → `ClaudeCode/High` → red.
- V-A13 `An_exhausted_commit_row_blocks_the_commit_child` | I | Commit/Medium cell `[ClaudeCode/Frontier]`;
  `SeedHoldAsync(db, ClaudeCode, "fable", until: null)`; `settled.ParentSessionId` = a seeded session;
  `CreateCommitTaskAsync` → `Status Blocked`, `FailureReason` starts with `routing exhausted: ` and contains
  `Commit/Medium chain`, `Complexity == Medium`, exactly one `Blocked` event for the child, exactly one
  `SessionQueuedMessages` row with `SourceTaskId == child.Id` for the parent session, `Warning` does not contain `using
  role policy`. Red under the scaffold. Mutation: fall back to the RolePolicy pair → `Status Queued` → red.
- V-A14 `A_held_commit_row_head_falls_to_the_next_candidate_with_the_skipped_warning` | I | Commit/Medium cell
  `[ClaudeCode/Frontier, Codex/Frontier]`; hold `fable` → `Codex/Frontier`, `Complexity == Medium`, Warning event
  contains `skipped fable`. Red under the scaffold. Mutation: take `Candidates[0]` without the walk → `ClaudeCode` →
  red.
- V-A15 `A_commit_routing_read_failure_keeps_the_role_policy_pair_with_a_warning` | I | Commit/Medium cell present;
  the service is built with a `ComplexityRoutingService` over a second `AppDbContext` that the test disposes before
  the call → `ClaudeCode/Medium`, `Complexity == null`, `Status Queued`, a Warning event containing `Routing read
  failed`. Red under the scaffold (no warning). Mutation: let the exception escape → the call throws → red.
- V-A16 `A_blocked_default_tier_create_note_reaches_the_caller_as_one_complete_UserPrompt` | H | harness with
  `ConfigureServices` registering `ComplexityRoutingService`, `RoutingPinService` and `AgentTaskService` as
  `AgentTaskCommitChildChainTests` does; Code/Medium cell `[ClaudeCode/Frontier]`, hold `fable`; create Code with the
  harness session as caller → `Blocked`; `Queue.OnTurnEndAsync(h.SessionId)` after a `TurnEnd` transcript row → the
  Pending row is `Sent` and exactly one `TranscriptEntries` row has `Kind == UserPrompt && Text == row.Body` (whole-body
  equality). Red under the scaffold (no Blocked create). Mutation: deliver a truncated body (fake adapter override) →
  equality red.

**S-B — `tests/Antiphon.Tests/Application/ComplexityEffectiveComposeTests.cs` (U; CP-10 red over a compile-only
stub returning an empty list and a `PolicyTarget` returning null) and `ComplexityEffectiveHttpTests.cs` (A)**

- V-B1 `A_required_pin_replaces_the_cell_and_sources_pin` | U | `Decision` hand-built as
  `ComplexityRoutingComposeTests.Decision(pin)` builds one (`:61-81`; stage pin Required Human, candidates
  `[Codex/Frontier]`); chain `[ClaudeCode/Frontier]` with label `Plan/Hard`, resolvedFrom `role`; policy target
  `ClaudeCode/Frontier`; `ComplexityEffective.Compose(decision, chain, "Plan/Hard", "role", target, isDefaultTier:
  false)` → `Source == "pin"`, `Candidates` is `[Codex/Frontier]`, `StagePin!.Id == pin.Id`, `GovernsDefault ==
  false`, `WithheldReason == null`. Mutation: return the chain → `Source "role"` → red.
- V-B2 `A_preferred_pin_prepends_and_sources_pin_plus_chain` | U | Preferred pin `[Grok/Frontier]`; chain
  `[ClaudeCode/Frontier, Grok/Frontier]` (`any`) → `Source == "pin+chain"`, candidates `[Grok/Frontier,
  ClaudeCode/Frontier]` (deduplicated), `StagePin` set. Mutation: ignore the pin → first candidate `ClaudeCode` → red.
- V-B3 `No_pin_and_no_cell_falls_back_to_role_policy` | U | `Decision.None`, chain `[]` resolvedFrom `none`, target
  `ClaudeCode/High` → `Source == "rolePolicy"`, candidates `[ClaudeCode/High]`, `StagePin == null`; `Decision.None`
  with chain `[X]` resolvedFrom `any` → `Source == "any"`. Mutation: return an empty list for `none` → `Source` missing
  `rolePolicy` → red.
- V-B4 `Governs_default_is_true_only_on_the_default_tier` | U | same inputs with `isDefaultTier: true` → `true`;
  `false` → `false`. Mutation: `true` for both → red on the second.
- V-B10 `An_unsupported_policy_target_is_withheld` | U | `RolePolicyReadService.PolicyTarget(new DelegationSettings {
  RolePolicy = { ["Plan"] = new() { Kind = AgentKind.Raw, Level = Frontier } } }, Plan)` → null;
  `(TaskComplexity)` irrelevant; `Compose(Decision.None, [], "Plan/Medium", "none", target: null, true)` → `Source ==
  "rolePolicy"`, `Candidates` empty, `WithheldReason` contains `Plan` and `Raw`; level `(AgentModelLevel)999` → null
  target likewise; a shipped Plan policy → `(ClaudeCode, Frontier, "fable")`. Mutation: build the target through
  `ResolveAgainstRolePolicy` → candidates non-empty → red.
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
  `effective` is JSON null. `finally` DELETE the cell, S and C. Mutation (PC-14): resolve the decision with the role's
  first active card pin's `CardId` instead of null → `stagePin.id == C` → red.
- V-B6 `Put_twice_is_last_writer_wins_without_a_409` | A | PUT `/api/complexity-chains/Debug/Easy`
  `{candidates:[{Codex,Frontier}], provenance:"Human", reason:"first"}` → 200, read `updatedAt` U1; PUT the same cell
  `{candidates:[{Grok,Frontier}], provenance:"Human", reason:"second"}` → 200; GET `?role=Debug` → Easy `candidates`
  is `[Grok/Frontier]`, `reason == "second"`, `updatedAt >= U1`. `finally` DELETE. Mutation: refuse a second Human
  write with 409 → red. (OD-4's statement as a test; a future concurrency token flips it on purpose.)

**S-B2 — `tests/Antiphon.Tests/Application/RolePolicyMatrixPreviewTests.cs` (I over an isolated schema:
`RolePolicyReadService(settings, pins, chains)` built directly)**

- V-B7 `A_default_cell_sources_matrix_in_the_role_policy_effective` | I | Code/Medium cell `[ClaudeCode/High]`, no
  pin → row Code `Effective.Source == "matrix"`, `AgentKind ClaudeCode`, `ModelLevel High`, `Alias "opus"`,
  `Strength null`; row Plan (no cell) → `Source "rolePolicy"`, `Alias "fable"`. Mutation: keep today's `Effective(stagePin,
  kind, level, alias)` → `rolePolicy` → red.
- V-B8 `A_stage_pin_still_sources_pin_over_the_default_cell` | I | Code/Medium cell `[ClaudeCode/High]` and a
  Required stage pin `Codex/Frontier` → `Source "pin"`, `Alias "gpt-6-astra"`, `Strength Required`. Mutation: prefer the
  cell → `matrix` → red.
- V-B9 `An_unlaunchable_policy_target_stays_withheld_in_the_role_policy_effective` | I | `RolePolicy["Plan"].Kind =
  Raw`, no cell, no pin → `Source "rolePolicy"`, `AgentKind null`, `ModelLevel null`, `Alias null`, `WithheldReason`
  contains `Raw`. Mutation: fill the alias from `ResolveAgainstRolePolicy` → `Alias "fable"` → red.

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
  roles keep RolePolicy.` Mutation: drop the tier from the sentence → red.
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

**S-B2 client — `rolePolicyModel.test.ts` (M), `RolePolicySection.test.tsx` (E), `DelegateModal.test.tsx` (E)**

- V-C15 `C097M V-C15 <case>` `effectiveLabel` | M | `{ source: 'matrix', agentKind: 'ClaudeCode', modelLevel: 'High',
  alias: 'opus' }` → `Matrix Medium → ClaudeCode/High (opus)`; `{ source: 'rolePolicy', withheldReason: 'Plan policy
  names Raw/Frontier, which delegation cannot launch' }` → `not launchable: Plan policy names Raw/Frontier, which
  delegation cannot launch`; existing `pin` and `rolePolicy` rows unchanged. Mutation: treat `matrix` as unknown →
  `unknown` → red.
- V-C16 `C097M V-C16 effective-column-header` | E | the section header cell reads `Effective next ordinary Worker
  create`; a `matrix` row renders the V-C15 label in `role-policy-effective-Code`. Mutation: keep the old header → red.
- V-C17 `C097M V-C17 matrix-source-preselects` | E | mocked Code row with `level 'Frontier'`, `effective { source:
  'matrix', modelLevel: 'High' }` → `tier-High`, `delegate-tier-source-matrix`; create body `modelLevel: null`.
  Mutation: read `row.level` for `matrix` → `tier-Frontier` → red.

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
- V-C14 `C097M V-C14 configure-opens-row-editor` | E | render `RolePolicySection` with an `onConfigure` spy → click
  `Configure Plan` → called once with `'Plan'`; the section copy no longer contains `scripts/routing-pin.ps1`.
  Mutation: omit the callback → red.
- R-C2 the `ComplexityChainCellEditor.test.tsx` cases run unchanged; `ComplexityChainCellEditor.tsx` is not modified
  (the row editor builds its draft from the exported `routingSettingsModel.ts` helpers).

**S-D — `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs` (S; `ScriptedApi` added beside `StubApi`)**

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
  pin was kept. Mutation: DELETE anyway → red.
- V-D6 `Get_Role_prints_the_effective_line` | S | `GET ?role=Review` answered with a row view whose Medium has
  `effective { source: "pin", candidates: [Codex/Frontier gpt-6-astra] }` → output contains `Review/Medium` and
  `effective: pin Codex/Frontier (gpt-6-astra)`; a row with `effective` null prints no `effective:` line. Mutation:
  print the stored list as effective → red.
- V-D7 `Clear_All_deletes_three_cells` | S | `clear -Role Review -Complexity All` → three DELETEs in tier order; exit 0.
  Mutation: DELETE Hard only → red.
- R-D the nine existing methods (`C470_mutation_cell_round_trip` … `Set_without_candidates_is_refused_locally`) run
  unchanged.

**S-E — `tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs` (S; same `ScriptedApi`, store seeded with 13
stage pins shaped as the live census plus one card pin; `-Backup` under a temp directory)**

- V-E1 `Dry_run_writes_nothing_and_records_before_images` | S | default verb → `Requests` contains only GETs
  (`/api/routing-pins`, one `?role=<R>` per pinned role); exit 0; backup JSON has `migrationId`, `diffSha256`, 13
  `pins`, 39 `cellsBefore` entries each `null`, 39 `plan` entries, `ledger` empty; output table has 13 rows and the
  backup path. Mutation: PUT during the dry run → red.
- V-E2 `Pins_with_forbid_date_or_agent_are_listed_manual_and_never_written` | S | store pins for Plan (forbid list),
  Code (`notAfter`), Review (`agentId`) → those rows read `manual`; `-Apply` → no PUT for those roles; the card pin
  appears nowhere. Mutation: migrate a forbid-list pin → PUT recorded → red.
- V-E3 `Apply_refuses_drift_against_the_backup_diff` | S | dry run; then change the Code pin in the store; `-Apply
  -Backup` → exit 1, zero writes, output names `drift` and `Code`. Mutation: skip the hash comparison → PUTs recorded →
  red.
- V-E4 `Apply_refuses_a_nonempty_target_cell_without_Resume` | S | store has a Human Review/Medium cell → `-Apply` →
  exit 1 before any PUT, output names `Review/Medium`. Mutation: drop the precondition → PUT recorded → red.
- V-E5 `Apply_puts_three_cells_per_role_and_ledgers_each_result` | S | `-Apply` → per role PUT Hard, Medium, Easy with
  `provenance Human`, `reason` = pin reason + ` (migrated from stage pin <id> on <date>)`; backup `ledger` has 39 entries
  `{role, tier, status: "written", at, body}` in request order; exit 0. Mutation: ledger written only at the end → a
  failed run (V-E6) has an empty ledger → red there.
- V-E6 `Apply_stops_on_a_failed_put_and_leaves_the_ledger_partial` | S | `Respond(PUT, "/TestDesign/Medium", 500)` →
  requests stop at that PUT; ledger has the entries up to and including `TestDesign/Hard`; exit 1; no DELETE.
  Mutation: continue past the 500 → more PUTs → red.
- V-E7 `ClearPins_reads_back_each_cell_and_deletes_only_on_match` | S | after a full `-Apply`, alter the store's
  Code/Easy candidates; `-Apply -ClearPins` → GET `?role=` per role; DELETE for each role except Code; exit 1 naming
  `Code/Easy`; ledger has `pinCleared` for the others. Mutation: skip the comparison → DELETE for Code → red.
- V-E8 `ClearPins_without_Apply_is_a_usage_error` | S | `-ClearPins -Backup x` → exit 1, `RequestCount == 0`.
  Mutation: treat it as apply → requests → red.
- V-E9 `Restore_reinstates_before_images_and_pins` | S | store seeded with a prior Human Plan/Medium cell; dry run
  (records it), `-Apply` (overwrites it), `-Apply -ClearPins`, then `-Restore` → `Requests` after restore: for each
  ledgered cell a DELETE when `cellsBefore` is null, a PUT of the before body for Plan/Medium (candidates equal the
  seeded prior cell), then PUT `/api/routing-pins` once per cleared pin with the backed-up body; store state equals the
  initial seed; exit 0. Mutation: DELETE Plan/Medium instead of PUT → red.
- V-E10 `Resume_continues_from_the_ledger_without_rewriting_done_cells` | S | after V-E6's partial run, `-Apply
  -Resume -Backup` → no PUT for ledgered tiers; PUTs resume at `TestDesign/Medium`; exit 0. Mutation: rewrite ledgered
  tiers → extra PUTs → red.
- V-E11 `A_second_apply_with_the_same_backup_is_a_no_op` | S | after a complete `-Apply`, `-Apply -Backup` again → zero
  PUTs, exit 0, output `nothing to do`. Mutation: rewrite → PUTs → red.

**S-F — `OrchestratorInstructionsRendererTests` (U), `OrchestratorInstructionsRefreshTests` (H),
`OrchestratorInstructionsDeltaTests` (U)**

- V-F1 `Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (edited with reason: the matrix
  section renumbers 5–7 to 6–8) | U | `Sample()` gains `Matrix: new MatrixSection("Medium", [new
  MatrixInstructionRow("Plan", Hard: "Codex/Frontier (gpt-6-astra)", Medium: "…", Easy: "unset", Effective:
  "ClaudeCode/High (opus)", Source: "stage pin")])`; body contains `## 5. Role × complexity matrix`, `default Medium`,
  `GET /api/complexity-chains`, `- Plan:`, `via stage pin`, `## 6. Routing pins`, `## 7. Model levels`, `## 8. Operator
  standing instructions`. Mutation: omit the section → red.
- V-F7 `A_full_legal_matrix_stays_under_the_byte_cap_and_is_elided_before_holds` | U | snapshot as the existing
  ladder test builds it (`:84-120`: 200 card pins, 60 holds, 50 runners, 10 standing lines) plus a matrix of 14 rows
  whose three tiers each list 8 candidates of the form `ClaudeCode/Frontier (fable)`; `Render(snapshot, 4_096)` →
  `Encoding.UTF8.GetByteCount(body) <= 4096`, body contains `… 14 rows: GET /api/complexity-chains`, and that line's
  index is greater than the card-pin cut notice's and less than the holds cut notice's; `Render(snapshot with only the
  matrix large, 16_384)` → all 14 `- <Role>:` lines present, each line `<= 160` bytes, a tier with 8 candidates renders
  three aliases then `+5`. Mutation: drop the per-line cap and the ladder step → `> 4096` → red.
- V-F2 `Covered_writes_each_regenerate_and_notify("complexity-chain")` | H | new `[Arguments("complexity-chain")]`;
  `CoveredWriteAsync` arm: `scope.ServiceProvider.GetRequiredService<ComplexityChainService>().UpsertAsync(Plan,
  Medium, new PutComplexityChainRequest([new(Codex, Frontier)], Human, "cell"), null, ct)`, `WhenIdleAsync`, return
  `"matrix:"`; `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside
  `ModelAvailability` (`:173`). Mutation: drop the signal from `UpsertAsync` → `SubmittedBodies.Count == 0` → red.
- V-F4 `A_matrix_write_reaches_an_idle_standing_orchestrator_as_one_complete_UserPrompt` | H | `AttachBundleAsync`;
  cell upsert; `WhenIdleAsync` → exactly one `SessionQueuedMessages` row for `h.SessionId` with `NoteHeader ==
  OrchestratorInstructionsService.NoteHeader`, `Status Sent`, `SentAt != null`; exactly one `TranscriptEntries` row
  with `Kind == UserPrompt && Text == row.Body`; `row.Body` contains `v{state.Version}` and `matrix:`; the session's
  `OrchestratorInstructionsVersion == state.Version`. Mutation: insert the `UserPrompt` row with a truncated body (fake
  adapter override) → equality red.
- V-F5 `A_busy_session_gets_the_matrix_notice_after_turn_end_as_one_complete_UserPrompt` | H | recipe `:208-233`
  with a cell upsert instead of the budget write: after `MarkWorkingAsync` and the write, the row is `Pending`,
  `SubmittedBodies` empty, no `UserPrompt` row; after the `TurnEnd` row and `OnTurnEndAsync` → one submission, the row
  `Sent`, exactly one `UserPrompt` row with `Text == row.Body`. Mutation: deliver while busy → a `UserPrompt` row before
  turn end → red.
- V-F6 `A_failed_notice_enqueue_leaves_the_version_column_behind_and_the_next_write_notifies` | H | harness
  `ConfigureDbContext` adds a `SaveChangesInterceptor` that throws `InvalidOperationException("C097M enqueue")` on the
  first `SavingChanges` whose added entries include a `SessionQueuedMessage` with the instructions `NoteHeader`, then
  disarms itself; `AttachBundleAsync`; read the session's column V0; cell upsert; `WhenIdleAsync` → the state row has
  a new version V1, zero queued rows, `SubmittedBodies` empty, and the session's column still equals V0; a second cell
  upsert (different candidates) → one `Sent` row whose body names `oldVersion` V0 and `newVersion` V2, one `UserPrompt`
  row with `Text == row.Body`, column == V2. Mutation: restore the column write before `EnqueueAsync` → the body names
  V1 as old → red.
- V-F8 `An_identical_matrix_rewrite_keeps_the_file_bytes_version_and_sends_no_notice` | H | recipe `:190-206` with a
  cell upsert, then the same upsert again → file bytes and `LastWriteTimeUtc` equal, `Revision` equal,
  `SubmittedBodies.Count == 1`. Mutation: `DiffMatrix` reports on equal input → a second submission → red.
- V-F3 `An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` | U | two snapshots with
  equal `Matrix` → no line starts with `matrix:`; change Plan's Medium → one line `matrix: Plan Medium …`. Mutation:
  `DiffMatrix` reports on equal input → first assertion red.

### Guards the regression

- R-A1 `ComplexityCreateTests` unchanged. `No_complexity_is_unchanged_when_fable_is_held` (`:230-245`) stays green
  **because** its seed is a Hard any-role chain and OD-1 is Medium; V-A2 is its explicit sibling. If OD-1 becomes
  `Hard`, that method's meaning changes and must be rewritten with the operator's decision as the reason.
- R-A2 `RoutingPinCreateTests` unchanged: pins without rows are untouched by S-A (D-2).
- R-A3 `RoutingPinCandidateCreateTests` unchanged: each method seeds a pin list or a Hard chain, none a Medium row.
- R-A4 `ComplexityDispatcherTests` unchanged: the re-walk is keyed on `task.Complexity`, which S-A sets to the same
  value a `-Complexity Medium` create would.
- R-A5 `CommitOnSettlePolicyTests` unchanged: `Merge_and_commit_children_are_created_with_Never` seeds no row, so
  both helpers take the absent-row path.
- R-B1 `ComplexityChainRoleHttpTests` unchanged: `effective` is additive; the existing `JsonElement` reads name their
  properties.
- R-B2 `ComplexityChainServiceTests` unchanged: the optional constructor parameter defaults to null and
  `GetEffectiveAsync` without a pin service composes with `Decision.None`.
- R-B3 `RolePolicyReadServiceTests` unchanged: `PolicyTarget` is extracted from `Row`/`Effective` without changing
  their outputs; the withholding rows keep their literal expectations.
- R-B4 `RolePolicyEndpointTests` unchanged after CARD-1234 lands: `source` values already serialised stay; `matrix`
  appears only when a default cell exists, which those tests do not seed.
- R-C1, R-C2 as above. R-D as above.
- R-F `OrchestratorInstructionsRendererTests` other methods and `OrchestratorInstructionsRefreshTests` other methods
  unchanged (the renumbering touches one method's literals and the `Sample()` fixture; the ladder test's existing
  ordering assertions still hold because the matrix step sits between card pins and holds).

### Guard inventory

| Guard | Where | Existing test |
|---|---|---|
| `-Complexity` + `-Kind/-Level` is 422 | `AgentTaskService.cs:737-746` | `ComplexityCreateTests.Explicit_kind_plus_complexity_is_422` |
| `-Complexity` + `ignoreModelDisabled` is 422 | `:748-755` | `ComplexityCreateTests.IgnoreModelDisabled_plus_complexity_is_422` |
| Required pin + chain → pin wins with bypass warning | `RoutingCandidates.cs:58-63`; `AgentTaskService.cs:933-941` | `ComplexityCreateTests.A_required_pin_plus_complexity_uses_the_pin_pair_and_warns_bypass` |
| Explicit kind vs Required pin → 409 | `RoutingPinService.cs:345-358` | `RoutingPinCreateTests.An_explicit_kind_against_a_required_pin_is_409_routing_pin_conflict` |
| Agent-only pin fills `AgentId`; a pool delegate cannot be pinned | `AgentTaskService.cs:831`; `RoutingPinService.cs:213-227` | `RoutingPinServiceTests.A_pool_delegate_cannot_be_pinned_to_a_stage` |
| Standing agent keeps its own kind at dispatch | `AgentTaskDispatcher.cs:7314-7331` | — (noted; V-A10 asserts the create side) |
| Exhausted chain → Blocked, resumes on hold clear | `AgentTaskService.cs:1448-1453`; `AgentTaskDispatcher.cs:1749-1778` | `ComplexityCreateTests.All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note`; `ComplexityDispatcherTests.Clearing_a_hold_requeues_a_routing_blocked_task` |
| Held single Required candidate stays Queued with Held | `AgentTaskDispatcher.cs:1791-1792` | `ComplexityDispatcherTests.A_required_pinned_chain_task_stays_Queued_with_Held_when_the_pin_is_held` |
| Reroute ends governance | `AgentTaskService.cs:2813-2816` | `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` |
| Auto never overwrites Human; empty/duplicate 422 | `ComplexityChainService.cs:176-187`, `:279-334` | `ComplexityChainHttpTests.Auto_over_human_is_409_and_empty_or_duplicate_is_422` |
| `?role=` view resolves role → any → none | `ComplexityChainService.cs:111-134` | `ComplexityChainRoleHttpTests.Get_role_Plan_returns_effective_resolvedFrom` |
| RolePolicy read withholds unsupported kinds and undefined levels | `RolePolicyReadService.cs:51-69`, `:78-98` | `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` |
| Pin write regenerates instructions | `RoutingPinService.cs:48-49`, `:270` | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify("routing-pin")` |
| Unchanged write neither rewrites nor notifies | `OrchestratorInstructionsDelta.cs` | `OrchestratorInstructionsRefreshTests.An_unchanged_write_neither_rewrites_nor_notifies` |
| Oversized body truncates in ladder order | `OrchestratorInstructionsRenderer.cs:43-87` | `OrchestratorInstructionsRendererTests.Oversized_snapshot_truncates_in_priority_order_under_the_byte_cap` |
| Script refuses a naive `-NotAfter` before any request | `scripts/complexity-chain.ps1:209` | `ComplexityChainScriptTests.A_naive_NotAfter_is_refused_before_any_request` |

### Positive controls

The plan's PC-1–PC-40 table is the binding list (one per behaviour; exact method names above). PC-6 binds to V-A6's
`[Arguments]` row `Check`; PC-9 to V-A9; PC-14 to the concrete wrong-grain query named in the plan; PC-16–PC-24 name
the Vitest titles above. None has been run.

### Out of scope

- No transcript stands in for a test: S-D and S-E are bound through the script harness above. The one live action
  left to a human is the operator's dry run and `-Apply` after §7 of the plan; the Code report for S-E may paste a dry
  run against the local stack as supplementary evidence, not as the verification of V-E1–V-E11.
- M-1 docs: `grep -n "Routing pins beat RolePolicy\|read-only for pins\|does not govern" docs/orchestration-loop.md`
  returns the rewritten lines; `docs/antiphon-api.md` names `effective`, `defaultComplexity`, `withheldReason`,
  the `matrix` source and last-writer-wins; `docs/ops-http.md:139` names the matrix source;
  `.claude/skills/antiphon-delegate/SKILL.md:147` names the default tier with the ordinary-Worker qualification.
- No E2E (the Routing tab has none). No Mutation run in this card. No concurrency token (OD-4). No per-row strength
  (OD-2). No hub event (D-10). The attention feed is covered by the existing `ComplexityAttentionTests` and is not
  re-tested for stamped tasks (their rows carry `Complexity`, the same key).

### Checkpoints

The closed list is the plan's `### Checkpoints` table (CP-1–CP-26); this amendment adds no row. Build reuse is written
as `CP-n` in the Build column with the same `After` group as row n (`PlanTableImporter.cs:200-211`). CP-3/CP-4 execute
16 methods and **23 results** (V-A6 carries 8 `[Arguments]`); `Min` is 16. V-A6 is the class's one `[Category("Unit")]`
method; the class filter runs it with the integration methods. Regression `Min` values are reproduced with
`grep -c "^\s*\[Test\]"` on the class file, as the plan states.

### Cost

As the plan states: ordinary green rows 160 minutes, red-first rows 24 minutes, authoring 630 minutes across nine Code
dispatches (S-0 20, S-A 90, S-B 70, S-B2 60, S-C1 60, S-C2 90, S-D 60, S-E 90, S-F 90). No production timeout or
assertion is loosened. Edited existing tests, each with its reason recorded above: three `RoutingSettingsTab.test.tsx`
cases (copy change), one `OrchestratorInstructionsRendererTests` method and its `Sample()` fixture (section
renumbering), the `Covered_writes_each_regenerate_and_notify` argument list (one new write kind).

## Appendix A — withdrawn: the 2026-10-13 Appendix A (S3 stage-pin editor)

V-11–V-15, R-3, PC-8–PC-11 and CP-5 of the 2026-10-13 design are withdrawn by plan D-6. `StagePinEditor.tsx`,
`usePutRoutingPin`, `pinRequestFrom` and `requiredConfirmCopy` are not built. The one pin write the client gains is
`useClearRoutingPin` (V-C12), used only from the row editor after its three cell writes succeeded (V-C9).
