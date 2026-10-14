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
V-C14–V-C17, V-E7–V-E11, V-F4–V-F6, V-F8 and the delivery flow they described. Round 3 (task `71e453db`, Plan,
Frontier; Final Review `f406ced5`, R3-01–R3-06; from `f62858af96321dbd02d0e0140a6cef600dcf65a3` on
`feat/card-task-71e453db`) adds V-A15b, V-B11, V-B12, V-E12, V-F2b, V-G1–V-G5 and PC-39–PC-45, widens V-E6 (three
rows) and V-F7 (part b), and adds CP-24; the withdrawn ids are not reused. Round-3 citations are against master
`ff4d84df522eaf483059ee11187fe68cc955d44c`; the bodies cited are unchanged from `d317c3f59`. Round 4 (task
`c733486f`, Plan, Frontier; Final Review `ce3b2bda`, R4-01–R4-03; from `7065ed10aee8b9f111ddc904370c7cb5f9119f6e` on
`feat/card-task-c733486f`) keeps V-F2b and V-G1 and rewrites both onto the fixtures the review traced, adds V-A12c and
PC-46, and **withdraws V-G2, V-G3 and V-G5** (ids not reused; reasons in plan §12). Round-4 citations are against
master `38a100f583f2a5f0568e007b6f066cc8fec7a693`; the routing, queue and fixture bodies cited are unchanged from
`ff4d84df5`.

Round 5 (task `a620633a`, Plan, Frontier; Final Review `2ec3e207`, R5-01; from
`66aaa500be8b36561ed53c7ea0758aac86b724a7`) **restores V-G2, V-G3 and V-G5**, superseding the withdrawal above.
These are exact methods in the standalone `MatrixChildReceiptTests`, selected with V-G1 by CP-24. PC-47–PC-49
bind their removal witnesses. V-A12c/PC-46 remain construction checks. New receipt coordinates are from master
`c24ad5682315a0c9fb6d4a123d0cf8bce4e538c0`; the two-flush V-G1 and Off/startup/seeded-floor/All V-F2b mechanics
remain required. The new methods are specifications pending Code execution.

**Operator decisions applied (plan §2).** OD-1 default tier `Medium`; OD-2 cells warn, pins refuse; OD-3 pins are
migrated by the script and cleared only by the operator's `routing-pin.ps1 clear`; OD-4 last-writer-wins stated and
tested (V-B6, `updatedAt >= U1`); OD-5 card pins read-only with a per-row count; OD-6 exhausted stamped create is
Blocked (D-3 extends it to the Commit and Merge children when a row resolved and no Required pin with a candidate
exists); OD-7 orchestrator creates are not stamped (consequence disclosed in plan §6.4). Post-land Mutation is paused:
PC-1–PC-49 are specified and pending; none is claimed run.

**Slice order and sizing.** S-0 (20 min) → S-A (110) ∥ S-B (75) → S-C1 (60) ∥ S-D (60) → S-E (65) → S-F (80) → S-C2
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

**Round-3 anchors (master `ff4d84df5`).** `CreateCommitTaskAsync` resolves kind/level from RolePolicy
(`AgentTaskService.cs:3835-3836`), reads the pin inside a `try` (`:3843-3847`), composes with `chain: null` and takes
the head (`:3848-3858`), stamps `RoutingPinId` when walked (`:3861-3862`), enforces forbids (`:3863`) and catches a
pin-service throw into `"Routing pin service failed ({type}); using role policy."` (`:3865-3868`); with no pin
service the warning is `"Routing pin service is not available; using role policy."` (`:3870-3873`). `ResolveAsync`
issues one `RoutingPins` query for a stage pin when `cardId` is null (`RoutingPinService.cs:306-307`).
`PutComplexityChainRequest.Provenance` defaults to Auto (`ComplexityChainDtos.cs:6-10`); `UpsertAsync` refuses a past
`notAfter` with 422 (`ComplexityChainService.cs:163-173`), an Auto body over a Human cell with 409
`complexity_chain_human` (`:176-187`) and an Auto role cell under a Human any-role row (`:191-203`).
`Decision.Pin.AgentId` is the agent an agent-only pin binds (`RoutingPinService.cs:65-75`); a pool delegate is refused
at pin time (`:213-227`); the create fills `AgentId` from the decision (`AgentTaskService.cs:831`) and takes the
standing agent's kind (`:858`). `Fit` decrements `cardPerBoard` from 20 to 0 while over `cap`
(`OrchestratorInstructionsRenderer.cs:54-60`); `SelectPins` keeps stage rows and trims card rows only (`:228-245`);
`Sample()` has one stage pin and one card pin, `Antiphon CARD-0822 Code` (`OrchestratorInstructionsRendererTests.cs:168-172`).
The chain fixture `CommitEndpointWebAppFactory` (`AgentTaskCommitEndpointTests.cs:319-340`: hosted services removed,
`RecordingGitWorkspaceService`, `CommitChainBoundary`; each instance clones its own database,
`AntiphonWebAppFactory.cs:52-56`) drives a real settle through `AgentTaskReplyService.OnTurnEndAsync(workerSession)`
(`AgentTaskCommitChildChainTests.cs:96`), which spawns the Commit child (`AgentTaskReplyService.cs:4702-4718`). The
chain test's helpers `ChainAdapter` (an adapter whose `OnSubmitted` writes a `UserPrompt` then a `TurnEnd` transcript
row), `ChainEntryAsync` and `ChainConfirmAsync` are private instance methods of its nested, non-partial
`CommitChildChain` class (`:32`, `:217-250`), so a second file of `AgentTaskCommitEndpointTests` does not reach them
(R4-01); only `SeedRepoAsync` is on the outer class (`AgentTaskCommitEndpointTests.cs:283-289`). The receipt
predicate is `TranscriptEntries.SingleAsync(p => p.AgentSessionId == X && p.Kind == TranscriptKinds.UserPrompt &&
p.Sequence > row.LastDeliveryBaselineSequence)` with `prompt.Text.ShouldBe(row.Body)` (`:155-157`, `:204-206`). A
Shared worker with `ReplyTo Session` whose settle spawns a Commit child gets a git note containing `commit task`
(`AgentTaskReplyService.cs:4715-4717`), so `PersistDeliverThenReleaseAsync` also mints the worker's own completion
obligation (`:2573-2582`) and, after the settlement save, `ReconcileAsync` enqueues it for the parent keyed
`task:<root>` (`:2658-2671`; `AgentTaskLandNotificationService.cs:138-148`). `FlushSessionAsync`
(`SessionMessageQueueService.cs:1741-1763`) runs `DeliverNextLockedAsync` once per call when the session reads idle;
the head is `pending[0]`, and a head with a null `ConversationKey` is not batched (`:2410-2421`), so the Blocked
child's note (queued first, no key) and the keyed completion take two flushes — the chain test's two parent flushes
(`AgentTaskCommitChildChainTests.cs:141`, `:192`) and its `parentAdapter.SubmittedBodies.Count.ShouldBe(2)` (`:212`)
are that shape (R4-02). A delivered row is `Sent` with `LastDeliveryBaselineSequence` equal to the session's
transcript max when one exists, else null (`:1051-1059`, `:2538-2543`, `:3567-3575`). `BridgeQueueHarness`'s adapter
writes a stamped `UserPrompt` and a `TurnEnd` on submit (`BridgeQueueHarness.cs:327-349`), so a harness `Sent` row
has a transcript receipt by construction; a harness session starts with zero transcript rows, so its first delivery
has a null baseline unless the test seeds rows first (`InsertTranscriptEntryAsync`, `:378-391`; R4-03).
`OrchestratorInstructionsSettings.Notify` defaults to `All` (`OrchestratorInstructionsSettings.cs:28`), so a startup
reconcile over a default `DelegationSettings` notifies the harness session; the refresh tests set `Off` before the
startup reconcile and `All` after it (`OrchestratorInstructionsRefreshTests.cs:353`, `:362-363`); under `Off` the
recipient selector returns an empty list and `QueueNoticesAsync` returns before its per-session loop
(`OrchestratorInstructionsRecipients.cs:44-45`; `OrchestratorInstructionsService.cs:233-234`). `QueueNoticesAsync`
saves `session.OrchestratorInstructionsVersion` before `EnqueueAsync` (`:268-278`). The Merge producer is called from
the settle site at `AgentTaskReplyService.cs:2479-2480` and the land service at `AgentTaskLandService.cs:654`;
`CommitOnSettlePolicyTests.cs:187` and `AgentTaskServiceIntegrationTests.cs:671` call it directly.

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
ComplexityEffectiveDto { source: 'pin' | 'pin+chain' | 'role' | 'any' | 'config' | 'rolePolicy' | 'agent',
                         candidates: ComplexityCandidateDto[],   // ordered; what WalkAsync would try, with availability; [] for agent
                         stagePin: RoutingPinRefDto | null,      // the active STAGE pin composed in (not a card pin)
                         governsDefault: boolean,                // complexity == defaultComplexity && source != 'agent'
                         withheldReason: string | null }         // non-null when no default target is projected:
                                                                 // source rolePolicy with an unlaunchable policy, or source agent
```

`source` is a fail-closed whitelist (hand-written expected rows; V-B1–V-B5, V-B10):

| stage pin | chain (`LoadChainAsync`) | composed by `RoutingCandidates.Compose` | effective |
|---|---|---|---|
| Required with ≥1 candidate | any | pin slots only | `pin`, pin candidates, `stagePin` set |
| Preferred with ≥1 candidate | non-empty | pin slots then chain, deduplicated | `pin+chain`, that list, `stagePin` set |
| Preferred with ≥1 candidate | empty (`none`) | pin slots then the policy pair (`Compose` appends it when `chain` is null; S-B passes null for `none`) | `pin+chain`, `stagePin` set |
| Required with no candidate and no `agentId` (forbid-only) | non-empty | chain only | chain's `resolvedFrom` (`role`/`any`/`config`), `stagePin` set |
| any strength with `agentId` (agent-only; R3-04) | any, including `none` | not composed for the default create: the overlay fills `AgentId` (`AgentTaskService.cs:831`) and the standing-agent block takes the agent's kind (`:858`) before the walk point, so D-1 does not stamp (V-A10/V-A11) | `agent`, `candidates []`, `stagePin` set, `governsDefault false` on each tier, `withheldReason` = `stage pin binds standing agent {Name} ({Kind}); an ordinary dispatch runs on that agent and does not walk this row` |
| none | non-empty | chain | `role`/`any`/`config`, `stagePin` null |
| none | empty | empty → the policy pair from `RolePolicyReadService.PolicyTarget(settings, role)` | `rolePolicy`, one candidate, `stagePin` null |
| none | empty | `PolicyTarget` withheld (`WithheldReason` non-null) | `rolePolicy`, `candidates []`, `withheldReason` = that string |

`PolicyResolution(AgentKind? Kind, AgentModelLevel? Level, string? Alias, string? WithheldReason)` is the typed input
(plan D-4): `WithheldReason` is `"{role} policy names {kind ?? "ClaudeCode"}/{level}, which delegation cannot launch"`
with the raw configured values when the kind is outside `DelegatableKinds` or the level is undefined; null otherwise.
`governsDefault` is `complexity == settings.DefaultComplexity && source != "agent"` (R3-04: an agent-bound stage pin
binds the default create to the agent, V-A10, so no tier of that row governs it). The agent label is read by
`GetEffectiveAsync` from `_db.Agents` for `decision.Pin.AgentId` and passed to `Compose` as its seventh argument
`agentLabel`; `RoutingPinRefDto` is not widened. Card pins do not populate `stagePin` or `candidates` (the
composition asks `ResolveAsync` with `cardId: null`); a card pin of any shape is the per-row count (OD-5). Availability on each candidate is the
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
`E` = `renderWithProviders` + `server.use` + `userEvent`, notifications mocked as the touched files already do. `K` =
the Commit child chain (R3-06, R4-01): a standalone class `MatrixChildReceiptTests` (`[NotInParallel]`,
`[ParallelLimiter<ProcessSpawnLimit>]`, `[Category("Integration")]`) whose four methods each construct their own
`CommitEndpointWebAppFactory` (`await using var factory = new CommitEndpointWebAppFactory()`, as the chain test does
at `AgentTaskCommitChildChainTests.cs:28`; `AgentTaskCommitEndpointTests.cs:319-340`; hosted services removed, so a
flush runs only when the test calls it) and carries three private static helpers written in the new file — no
existing test file is edited and no private member of another class is referenced: `SeedRepoAsync()` (the body of
`AgentTaskCommitEndpointTests.cs:283-289` with prefix `"c097m-receipt"`), `EntryAsync(factory, session, kind, body)`
(the body of `AgentTaskCommitChildChainTests.cs:231-242`: a fresh scope, `Sequence` = the session's max + 1,
`Timestamp` and `CreatedAt` now, `SaveChangesAsync`; return that sequence as `Task<long>` for the floor assertions)
and `AdapterFor(factory, session)` (the body of `:217-229`: a
`FakeAgentProtocolAdapter` whose `OnSubmitted` calls `EntryAsync` for a `UserPrompt` carrying the body and then a
`TurnEnd`, registered through `factory.Services.GetRequiredService<AgentSessionRuntime>().Register(session,
adapter)`). For these local copies, wrap `EntryAsync`'s insert/save in
`await using var mutation = await factory.Services.GetRequiredService<SessionStateStore>().BeginMutationAsync([session], ct)`
and after the save await `mutation.PublishCommittedAsync(ct)` before returning. This keeps the busy → idle
projection in step with the committed fixture transcript: enabled state reads use the cache
(`SessionMessageQueueService.cs:5253-5254`), which retains a loaded entry (`SessionStateStore.cs:186-196`);
the existing mutation lease reloads it after commit (`:127-165`; test usage
`AgentTaskLandReceiptWatermarkTests.cs:198-204`). These coordinates are from master `c24ad5682`; no existing
helper is edited. There is no `ChainConfirmAsync` equivalent: the row selects each receipt by session, baseline floor and
complete body. A receipt is the chain test's predicate (`TranscriptEntries` row with `Kind == UserPrompt`, `Sequence
> row.LastDeliveryBaselineSequence`, `Text == row.Body`, `:155-157`). Test files and titles below are **binding
required** until Code creates them and records their execution.

### Delivery inventory

This card adds no delivery mechanism. The one outcome that reaches a session is inventoried so Review can see the
existing path it reuses; the instructions notice is listed because a cell write now enters its existing path.

| Outcome | Producer | Recipient | Persistence and identity | Delivery | Receipt | Loss/recovery boundary | Bound by |
|---|---|---|---|---|---|---|---|
| Blocked default-tier create (OD-6) and Blocked Commit/Merge child (D-3) | `EnqueueBlockedParentNoteAsync` (`AgentTaskService.cs:4610-4634`) adds the existing row type to the caller's unit of work | `task.ParentSessionId` (must exist in `AgentSessions`) | `SessionQueuedMessages` row: `SourceTaskId`, `ContentDigest = DelegationNoteDigest.Compute(reason)`, `NoteHeader`, `Origin Delegation`, `Status Pending`; settlement saves child and note at `AgentTaskReplyService.cs:2629`; the direct Merge fixture explicitly saves its context | existing turn-end flush / stranded sweep / idle flush (`SessionMessageQueueService.cs:1554-1568`, `:1630-1713`, `:1749-1763`) | complete parent `UserPrompt` selected by session, `Sequence > LastDeliveryBaselineSequence` and full captured `Body`; per-body count 1, queue-order `SubmittedBodies`, same counts after repeat trigger | the helper adds directly to `_db` without an enqueue call (`AgentTaskService.cs:4617-4633`). V-G3 interrupts after the settlement commit and before worker completion enqueue (`AgentTaskReplyService.cs:2651-2671`), then a fresh queue instance discovers the aged Delegation row. This covers committed-row stranding, not a process restart. The no-parent branch returns before adding a row (`:4612-4615`, V-G4) | V-A3, V-A13, V-A12c, V-G4 (construction/absence); CP-24: V-G1 idle Commit plus worker completion, V-G2 busy parent then two turn ends, V-G3 committed Blocked row via sweep, V-G5 direct Merge through recipient receipt. The existing successful child chain (`AgentTaskCommitChildChainTests.cs:24-29`, `:117-142`) supplies fixture precedent, not the Blocked-child receipt selection. Current coordinates: master `c24ad5682` |
| Instructions notice after a cell write (D-10) | `OrchestratorInstructionsService.QueueNoticesAsync` (`:212-285`), unchanged: a cell write only fires `OrchestratorInstructionsSignal` | `OrchestratorInstructionsRecipients.Select` | `OrchestratorInstructionsStates` row, `SessionQueuedMessages` row with `NoteHeader` | `EnqueueAsync(WhenIdle, deliverIfIdle: true)` (`:271-278`) | one `UserPrompt` row with `Sequence > row.LastDeliveryBaselineSequence` and `Text == row.Body` for the `Sent` row whose `NoteHeader == OrchestratorInstructionsService.NoteHeader`; identity: the session's `OrchestratorInstructionsVersion` equals the state's `Version` | unchanged and disclosed: the version column and the state are saved before `EnqueueAsync` (`:268-278`); a crash or enqueue failure between them, followed by the unchanged-body early return (`:156-157`), loses that notice; this card does not recover it (plan D-10) | V-F2 (a cell write enters the path), V-F2b (the complete receipt and the version identity); existing: `Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator` (`:30`; `SubmittedBodies` and the version column), `A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row` (`:209`) |

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
returning `false`, `ResolveServerChildRoutingAsync` reading the pin once and returning `legacy(pinRead)`
unconditionally, and no stamp; S-0 has landed,
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
  (first half). Mutation: return `legacy(pinRead)` unconditionally → `ClaudeCode` → red.
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
- V-A12c `An_exhausted_merge_row_blocks_the_merge_child` | I | Merge/Medium `[ClaudeCode/Frontier]`; hold `fable`;
  `conflicted` seeded as `CommitOnSettlePolicyTests.cs:176-184` (`WorktreePath`, `WorktreeBranch`, `MergeTargetRef`)
  with `ParentSessionId` = a seeded session; `service.CreateMergeTaskAsync(conflicted, ["a.cs"], ct)` then
  `db.SaveChangesAsync()` (the settle site's call, `AgentTaskReplyService.cs:2479-2480`, before its own save) →
  returned child `Status Blocked`, `FailureReason` starts with `routing exhausted: ` and contains `Merge/Medium chain`
  and `fable`, `Complexity == Medium`, `RoutingPinId == null`, exactly one `Blocked` event for the child, exactly one
  `SessionQueuedMessages` row with `SourceTaskId == child.Id`: `AgentSessionId == conflicted.ParentSessionId`, `Status
  Pending`, `Origin Delegation`, `NoteHeader != null`, `ContentDigest == DelegationNoteDigest.Compute(child.FailureReason)`.
  The Merge mirror of V-A13; V-G5 below separately binds its recipient receipt (R5). Red under the scaffold (the
  scaffold's Merge `legacy` returns `ResolveLevel(Worker, Merge, null)`, so the child is `Queued`). Mutation (PC-46):
  fall back to the RolePolicy pair in the Merge arm instead of Blocking → `Status Queued` → red.
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
  instead of `legacy(pinRead)` → `ClaudeCode/Medium` → red.
- V-A15b `The_commit_child_reads_the_pin_once_and_keeps_a_resolved_required_pin` | I | Commit/Medium
  `[ClaudeCode/High]` present; Required stage Commit pin `Codex/Frontier` (one candidate); `settled.CardId == null`;
  the `AppDbContext` is built from `TestDbFixture.CreateDbContextOptions(schema.ConnectionString)` with
  `.AddInterceptors(new PinReadGuard())` — a `DbCommandInterceptor` whose `ReaderExecuting`/`ReaderExecutingAsync`
  count commands whose `CommandText` contains `"RoutingPins"` and throw `InvalidOperationException("second
  RoutingPins read")` on the second; `ResolveAsync` issues one such query for a stage pin when `cardId` is null
  (`RoutingPinService.cs:306-307`); the guard is armed after seeding → `CreateCommitTaskAsync` returns `Codex/Frontier`,
  `Complexity == null`, `RoutingPinId == null`, `Status Queued`, no `Blocked` event, no Warning event containing
  `Routing pin service failed`, `guard.PinReads == 1`. Passes under the scaffold (the scaffold helper reads once and
  returns `legacy(pinRead)`). Mutation (PC-39): read the pin again inside Commit's legacy selection → the guard throws,
  the catch yields `ClaudeCode/Medium` with the warning → red.
- V-G4 `A_blocked_commit_child_without_a_parent_session_writes_no_note` | I | Commit/Medium `[ClaudeCode/Frontier]`;
  hold `fable`; `settled.ParentSessionId == null` → child `Status Blocked`, `FailureReason` starts `routing
  exhausted: `, `Complexity == Medium`, one `Blocked` event, zero `SessionQueuedMessages` rows with `SourceTaskId ==
  child.Id`, no exception; a second arm in the same method with `ParentSessionId` = a Guid that has no `AgentSessions`
  row → the same (`EnqueueBlockedParentNoteAsync :4612-4615`). Red under the scaffold (the scaffold returns
  `legacy(pinRead)` and does not Block). Mutation: add the row without the session check → the FK save fails or a
  row appears → red.

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
- V-B11 `An_agent_only_stage_pin_withholds_the_default_target_and_sources_agent` | U | `[Arguments]` × 2: `with-row`
  (a `Decision` whose `Pin.AgentId` is set and whose `PinCandidates` is empty, built as the `Decision(pin, identifier)`
  helper builds one over a pin seeded like `RoutingPinServiceTests.cs:233-248`; chain `[Codex/Frontier]` resolvedFrom
  `role`) and `no-row` (chain `[]`, `none`); policy `(ClaudeCode, Frontier, "fable", null)`; `Compose(decision, chain,
  "Plan/Medium", resolvedFrom, policy, isDefaultTier: true, agentLabel: "astra-reviewer (Codex)")` → `Source ==
  "agent"`, `Candidates` empty, `StagePin!.Id == pin.Id`, `GovernsDefault == false`, `WithheldReason == "stage pin
  binds standing agent astra-reviewer (Codex); an ordinary dispatch runs on that agent and does not walk this row"`.
  Executed results: 2. Mutation (PC-41): ignore `agentLabel` → row `with-row` gives `Source "role"`, `GovernsDefault
  true` → red.
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
- V-B12 `Get_role_view_reports_an_agent_only_stage_pin_as_agent_bound` | A | seed a standing agent through a factory
  `AppDbContext` scope (recipe `RoutingPinServiceTests.cs:233-248`, `IsPoolDelegate = false`, `Kind ClaudeCode`, `Name
  "c097m-standing"`); PUT `/api/complexity-chains/Plan/Medium` `{candidates:[{Codex,Frontier}], provenance:"Human"}` →
  200; PUT `/api/routing-pins` `{role:"Plan", provenance:"Human", strength:"Required", agentId:"<agent>",
  reason:"C097M V-B12"}` → 200, keep `id` P; GET `/api/complexity-chains?role=Plan` → `chains[1]` (Medium)
  `effective.source == "agent"`, `effective.candidates` empty, `effective.stagePin.id == P`, `effective.governsDefault
  == false`, `effective.withheldReason` contains `c097m-standing (ClaudeCode)`; the stored `candidates[0].agentKind ==
  "Codex"` beneath it; `chains[0]` (Hard, no cell) `effective.source == "agent"` as well. `finally` DELETE the cell, P
  and the agent row. Mutation: fold the agent pin into the chain (PC-41 at the HTTP boundary) → `source "role"` → red.

**S-C1 — `routingSettingsModel.test.ts` (M), `RoutingSettingsTab.test.tsx` (E), `complexityChains.test.tsx` (R)**

- V-C1 `C097M V-C1 <case>` `effectiveCellLabel` | M | rows: `pin` Required Human `[Codex/Frontier gpt-6-astra]` →
  `Required Human pin → Codex/Frontier (gpt-6-astra)`; `pin+chain` Preferred Human `[Grok/Frontier grok-4.7, …]` →
  `Preferred Human pin → Grok/Frontier (grok-4.7), then this row`; `rolePolicy` `[ClaudeCode/High opus]` → `Falls back
  to RolePolicy → ClaudeCode/High (opus)`; `rolePolicy` with `withheldReason: 'Plan policy names Raw/Frontier, which
  delegation cannot launch'` and no candidates → `Falls back to RolePolicy → not launchable: Plan policy names
  Raw/Frontier, which delegation cannot launch`; `agent` with `withheldReason: 'stage pin binds standing agent
  c097m-standing (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row'` and no candidates
  → `Pinned to standing agent c097m-standing (ClaudeCode) — an ordinary dispatch runs there; this row is not walked`
  (R3-04); `role` → `Own rule`; `any` → `Inherits Any role`; `config` → `Configuration fallback`. Mutation: ignore
  `effective.source` → red on the first row.
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
- V-E6 `Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones` | S | `[Arguments]` × 3 over
  the seeded Plan/Medium before-image, the 13 census pins in the pins store throughout (precondition 1 holds):
  `human` (Human `[Grok/Frontier]`, `notAfter` null): dry run records `present: true, provenance: "Human"` with those
  candidates, `-Apply` overwrites it, `-Restore -Snapshot <that file>` → `Requests` after the restore: one GET
  `/api/routing-pins?role=<R>` per migrated role, then PUT `/api/complexity-chains/Plan/Medium` with `candidates
  [Grok/Frontier]`, `provenance Human` and the seeded reason, and DELETE for each of the other 38 cells, in snapshot
  order; the store's cells equal the initial seed. `human-expired` (Human, `notAfter` 2099-01-01Z at seed time; after
  `-Apply` the test rewrites that entry's `notAfter` in the snapshot file to `2026-01-01T00:00:00Z`, the state a clock
  that passed it would leave) → DELETE for Plan/Medium, no PUT, output contains `expired before restore; cleared`.
  `auto` (Auto `[Grok/Frontier]`): the dry-run table row carries `(restore clears)`; `-Restore` → DELETE for
  Plan/Medium, no PUT, output contains `Auto before-image not re-created; cleared`. In each row: no PUT or DELETE on
  `/api/routing-pins`, the pins store unchanged, exit 0, and a second `-Restore` sends the same requests and exits 0;
  the store's cells are empty after `human-expired` and `auto`. Executed results: 3. Mutation (PC-31, row `human`):
  DELETE Plan/Medium instead of PUT → red. Mutation (PC-45, row `auto`): PUT the saved Auto body after the DELETE → a
  PUT on Plan/Medium → red.
- V-E12 `Restore_refuses_until_each_migrated_roles_required_stage_pin_is_back` | S | dry run and `-Apply` over the
  13-pin store; the pins store is then emptied (the outcome of the operator's `routing-pin.ps1 clear` per role);
  `-Restore -Snapshot <file>` → the requests after the apply are GETs on `/api/routing-pins?role=<R>` and nothing
  else — no PUT, no DELETE; exit 1; output contains `restore refused: Plan has no Required stage pin` and
  `routing-pin.ps1 set -Role Plan -Provenance Human -Strength Required -Candidates ClaudeCode/Frontier`; the store's
  39 cells are unchanged. Second arm in the same method: twelve pins seeded back as Required and the Review pin seeded
  back as `Preferred` → refused, naming `Review`, zero writes. Third arm: the Review pin seeded back as Required
  `Codex/Frontier` → `-Restore` proceeds: 39 DELETEs, exit 0. Mutation (PC-40): skip precondition 1 → a DELETE is
  recorded with the pins store empty → red.

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
  that of `## 5. Role × complexity matrix` and less than that of `## 6. Routing pins`, contains no `- Plan:` line,
  still contains `Antiphon CARD-0822 Code` (`Sample()`'s one card pin, `OrchestratorInstructionsRendererTests.cs:171`,
  not cut), and contains neither `more: GET /api/routing-pins` nor `more: GET /api/model-availability` (neither the
  card-pin loop nor the holds step was reached). Derivation: the matrix step is the first in `Fit` (plan D-10), so
  the first body tried under `full` is the one with the section collapsed, and collapsing 14 lines to one saves more
  than the one byte asked for; round 2 placed the step after the card-pin loop, which at this cap cut `Sample()`'s
  card pin first (`Fit` decrements `cardPerBoard` to zero, `:54-60`) and left the matrix expanded (R3-05). No
  universal byte figure is asserted. Mutation (PC-35): drop the per-line cap → a line longer than 160 bytes in (a) →
  red. Mutation (PC-42): move the matrix step after the card-pin loop → `Antiphon CARD-0822 Code` disappears, `more:
  GET /api/routing-pins` appears and `- Plan:` remains in (b) → red.
- V-F2 `Covered_writes_each_regenerate_and_notify("complexity-chain")` | H | new `[Arguments("complexity-chain")]`;
  `CoveredWriteAsync` arm: `scope.ServiceProvider.GetRequiredService<ComplexityChainService>().UpsertAsync(Plan,
  Medium, new PutComplexityChainRequest([new(Codex, Frontier)], Human, "cell"), null, ct)`, `WhenIdleAsync`, return
  `"matrix:"`; `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside
  `ModelAvailability` (`:173`). The existing assertions hold: `Revision > 0`, `SubmittedBodies.Count == 1`, the body
  contains `matrix:` and the written path. Mutation (PC-33): drop the signal from `UpsertAsync` →
  `SubmittedBodies.Count == 0` → red.
- V-F2b `A_cell_write_notice_is_received_as_one_complete_user_prompt_equal_to_its_queued_body` | H | the
  `CoveredWriteAsync` order with a seeded transcript floor (R4-03): `CreateHarnessAsync`, `AttachBundleAsync`;
  `h.Delegation.OrchestratorInstructions.Notify = Off`; `Instructions(h).ReconcileNowAsync("startup")` (writes the
  file and the state row and queues no note: `Select` returns an empty list under `Off`,
  `OrchestratorInstructionsRecipients.cs:44-45`, and `QueueNoticesAsync` returns at `:233-234`); `startup =
  ReadStateAsync(h)`; `await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "Dispatch work.")` then `floor
  = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn")` (`BridgeQueueHarness.cs:378-391`;
  the session reads idle after the `TurnEnd`, as `:229-230` of the refresh tests relies on); `Notify = All`; in a
  scope `ComplexityChainService.UpsertAsync(Plan, Medium, new PutComplexityChainRequest([new(Codex, Frontier)], Human,
  "cell"), null, ct)`; `Instructions(h).WhenIdleAsync()`; `state = ReadStateAsync(h)` → `state.Revision ==
  startup.Revision + 1` and `state.Version != startup.Version`; in `NewDb(h)`: the `SessionQueuedMessages` rows for
  `h.SessionId` with `NoteHeader == OrchestratorInstructionsService.NoteHeader` number exactly one (the startup
  reconcile ran under `Off`); that `row` has `Status == Sent` and `LastDeliveryBaselineSequence == floor` (the baseline
  is the transcript max captured before typing, `SessionMessageQueueService.cs:1051-1059`, `:3567-3575`;
  `TranscriptConfirmEnabled` defaults to true, `SupervisionSettings.cs:346`, and the harness's
  `DeliveryVerificationSettings` leaves it, `BridgeQueueHarness.cs:129-145`); the
  `TranscriptEntries` rows with `AgentSessionId == h.SessionId && Kind == TranscriptKinds.UserPrompt` number exactly
  two (the seeded one and the receipt); exactly one of them has `Sequence > floor`, and its `Text == row.Body`;
  `row.Body` contains `matrix:` and `state.WrittenPath`; `h.Adapter.SubmittedBodies` equals `[row.Body]`;
  `SessionVersionAsync(h) == state.Version` (the version the notice announces is the column the recipient now
  carries; the column was null before the write because `Off` skipped the per-session loop at startup). The harness
  adapter writes the `UserPrompt` on submit (`BridgeQueueHarness.cs:344`), so the row binds the queue's `Sent`
  transition to that transcript row rather than to the adapter's acceptance. Mutation (PC-44): drop the signal from
  `UpsertAsync` → zero `SessionQueuedMessages` rows with the header for `h.SessionId` and `SubmittedBodies` empty →
  red (the startup reconcile contributed no row, so the count has no other source).
- V-F3 `An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` | U | two snapshots with
  equal `Matrix` → no line starts with `matrix:`; change Plan's Medium → one line `matrix: Plan Medium …`. Mutation
  (PC-34): `DiffMatrix` reports on equal input → first assertion red.

**Delivery receipts (R3-06, R4, R5) — `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs` (K; a standalone
class with four methods and its own helpers, F-C; CP-24 selects it with the class filter; committed with S-A)**

The producer under test is the real settle: `AgentTaskReplyService.OnTurnEndAsync(workerSession)` reads the worker's
`done` report, finds `x.md` dirty under `CommitOnSettle = Agent` and calls `CreateCommitTaskAsync`
(`AgentTaskReplyService.cs:4702-4718`), which under D-3 Blocks the child and writes the parent note in the same
`SaveChanges`. The same settle mints the worker's own completion obligation and enqueues it for the parent after the
save (`:2573-2582`, `:2658-2671`), so the parent owns two `Pending` rows after one settle; the row below drives both
deliveries and identifies each receipt by its complete body and the row's baseline floor (R4-02). V-G2 uses that
producer with a busy parent; V-G3 interrupts it after the save; V-G5 calls the Merge producer directly. Seeds follow the chain test's
`:47-85` with the differences the row names; `parent` and `workerSession` are two `AgentSessions` rows (no
`childSession` and no pool `Agents` row: the Blocked child is not dispatched); `parentAdapter = AdapterFor(factory,
parent)`. Source coordinates in this receipt section are against master `c24ad5682`; service filenames without a
directory are under `server/Application/Services`, test filenames under `tests/Antiphon.Tests/Application`.

**Common receipt oracle (V-G1, V-G2, V-G3, V-G5).** At the first fresh-scope read, capture the producer's Blocked
row `Id`, `Sequence`, `Body`, `NoteHeader`, `SourceTaskId` and `ContentDigest`. Require the complete body to contain
the child's note header and failure reason; require the digest of that reason and `SourceTaskId == child.Id`
(`AgentTaskService.cs:4617-4633`). These short notes use the factory's modern profile
(`AgentTaskCommitEndpointTests.cs:332-336`). After delivery, reload by the captured id, require `Sent`,
`DeliveryAttempts == 1`, a non-null `LastDeliveryBaselineSequence` equal to the specified floor and `Body` equal
to the captured complete body. Select `SingleAsync(p => p.AgentSessionId == parent && p.Kind == UserPrompt &&
p.Sequence > row.LastDeliveryBaselineSequence && p.Text == expectedBody)` and separately count parent
`UserPrompt` rows with `Text == expectedBody`: exactly 1. Compare the complete receipt text, not a prefix, digest
or adapter acknowledgment. For two notes require receipt sequence order to agree with queue sequence order and
`parentAdapter.SubmittedBodies == [blocked.Body, completion.Body]`; for the one-note cases require `[blocked.Body]`.
Repeat the specified trigger and recheck these per-body counts, row ids and attempts without resetting the adapter.
The attempt floor is captured from the persisted transcript before typing
(`SessionMessageQueueService.cs:2527-2543`, `:3567-3574`); the local fake persists the submitted prompt then TurnEnd
(`AgentTaskCommitChildChainTests.cs:217-242`). This oracle is in addition to the producer/status assertions below.

**CP-24 roster and minutes:** V-G1 2, V-G2 3, V-G3 4, V-G5 3 = 12 minutes, four unparameterized methods/results.
Each estimate includes its factory setup; CP-4 supplies the build. Lane: serial portable integration on dynamically
resolved default placement, no host or OS pin. Class helpers carry no `[Test]` attribute. PC runs select the exact
method with `--treenode-filter "/*/*/MatrixChildReceiptTests/<method>"`, not the CP-24 class filter.

- V-G1 `A_blocked_commit_child_note_and_the_settled_tasks_completion_each_reach_the_parent_once_in_queue_order` | K |
  `await using var factory = new CommitEndpointWebAppFactory()`; `repo = await SeedRepoAsync()`,
  `repo.AddBareOriginAsync()`, write `x.md` (`:38-42`); in one scope: the two sessions (`:53-58` shape), the Shared
  worker task (`:60-68`: `ParentSessionId = parent`, `AgentSessionId = workerSession`, `ReplyTo Session`,
  `CommitOnSettle Agent`, `Status Dispatched`), no `RoutingPins` row, a `ComplexityChains` row `Role Commit`,
  `Complexity Medium`, `CandidatesJson` for `[ClaudeCode/Frontier]`, `Provenance Human`; in a factory scope
  `ModelAvailability.UpsertManualAsync("ClaudeCode", "fable", DateTimeOffset.UtcNow.AddHours(4), "hold fable", ct)`;
  `await EntryAsync(factory, parent, UserPrompt, "Dispatch work.")`, `floor = await EntryAsync(factory, parent, TurnEnd, null)` (idle
  parent with an observable baseline, `:86-87`); `parentAdapter = AdapterFor(factory, parent)`; worker `UserPrompt`
  marker, `done` report, `TurnEnd` (`:92-94`); `AgentTaskReplyService.OnTurnEndAsync(workerSession)` (`:96`).
  **Stage 1, fresh scope:** the child (`Single(ParentTaskId == taskId)`) has `Status Blocked`, `FailureReason`
  starting `routing exhausted: ` and containing `Commit/Medium chain` and `fable`, `Complexity == Medium`,
  `RoutingPinId == null`, one `Blocked` event; `blocked = Single(SessionQueuedMessages where SourceTaskId ==
  child.Id)`: `AgentSessionId == parent`, `Status Pending`, `Origin Delegation`, `ConversationKey == null`,
  `NoteHeader != null`, `ContentDigest == DelegationNoteDigest.Compute(child.FailureReason)`; `completion =
  Single(SessionQueuedMessages where AgentSessionId == parent && SourceLandNotificationId != null)`: `Status Pending`,
  `SourceTaskId == taskId`, `Sequence > blocked.Sequence`, `Body != blocked.Body`; the `SessionQueuedMessages` rows
  for `parent` number exactly two; `parentAdapter.SubmittedBodies` is empty (hosted services removed; no flush yet).
  **Stage 2:** `queue.FlushSessionAsync(parent)` → fresh scope: `blocked` is `Sent` with `LastDeliveryBaselineSequence
  == floor`; `completion` is still `Pending` (one `DeliverNextLockedAsync` per flush, `SessionMessageQueueService.cs:1749-1763`;
  the null-key head is not batched, `:2410-2421`); `receipt1 = Single(TranscriptEntries where AgentSessionId ==
  parent && Kind == TranscriptKinds.UserPrompt && Sequence > blocked.LastDeliveryBaselineSequence && Text == blocked.Body)` with
  `receipt1.Text == blocked.Body`; the `UserPrompt` rows for `parent` with `Text == completion.Body` number zero;
  `parentAdapter.SubmittedBodies` equals `[blocked.Body]`. **Stage 3:** `queue.FlushSessionAsync(parent)` again (the
  adapter wrote `UserPrompt` then `TurnEnd` on submit, so the parent reads idle, as the chain test's second parent
  flush at `:192` relies on) → fresh scope: `completion` is `Sent` with `LastDeliveryBaselineSequence >=
  receipt1.Sequence`; `receipt2 = Single(TranscriptEntries where AgentSessionId == parent && Kind == UserPrompt &&
  Sequence > completion.LastDeliveryBaselineSequence && Text == completion.Body)` with `receipt2.Text == completion.Body` and `receipt2.Sequence
  > receipt1.Sequence`; the `UserPrompt` rows for `parent` with `Text == blocked.Body` still number exactly one; the
  `Pending` rows for `parent` number zero; `parentAdapter.SubmittedBodies` equals `[blocked.Body, completion.Body]`.
  The completion floor equals the persisted transcript max read immediately before stage 3 (the adapter's first
  TurnEnd). A third `FlushSessionAsync(parent)` leaves the common oracle's per-body counts and attempts at 1.
  Red under the scaffold: the settle spawns a `Queued` child (the chain test's path), so `Status Blocked` fails at
  stage 1 and `Single(SourceTaskId == child.Id)` has no row. Mutation (PC-43): skip `EnqueueBlockedParentNoteAsync`
  in the Blocked arm → no row with `SourceTaskId == child.Id` at stage 1 → red.

- V-G2 `A_blocked_commit_child_note_reaches_a_busy_parent_once_after_turn_end` | K | **3 min; CP-24; PC-47.**
  1. Own factory/repo and V-G1's worker, held Commit/Medium cell and two-session seed. Parent gets only
     `EntryAsync(parent, UserPrompt, "Dispatch work.")`, with no closing TurnEnd; register `AdapterFor`. Append
     the worker marker, done report and TurnEnd, then await `AgentTaskReplyService.OnTurnEndAsync(workerSession)`
     (the chain fixture's order at `AgentTaskCommitChildChainTests.cs:86-96`, with the parent end omitted).
  2. Fresh scope: assert V-G1 stage 1's Blocked child identity and two Pending rows in queue sequence order;
     `SessionStateStore.IsWorkingAsync(parent)` is true. Call `queue.FlushSessionAsync(parent)` while busy;
     fresh scope still has both Pending, attempts 0, null attempt floors, zero parent UserPrompt matches for
     either body and empty `SubmittedBodies`. The guard is `SessionMessageQueueService.cs:1761-1763`; the
     transcript activity/end classification is `server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:16-43`.
  3. Append parent TurnEnd through `EntryAsync`, retain its returned `floor`, and assert state reads idle after
     its committed publication (F-C). Await **queue** `OnTurnEndAsync(parent)`, not the reply service and not a
     direct flush (`SessionMessageQueueService.cs:1554-1568`; the existing notice fixture's order is
     `OrchestratorInstructionsRefreshTests.cs:229-230`). Fresh scope: Blocked row Sent with baseline `floor`, one
     complete-body receipt by the common oracle; completion still Pending/attempts 0, zero matching prompts;
     `SubmittedBodies == [blocked.Body]`.
  4. The adapter has persisted that receipt and its TurnEnd (`AgentTaskCommitChildChainTests.cs:221-224`); read the
     new transcript max as `completionFloor`, then call queue `OnTurnEndAsync(parent)` a second time. Require
     the completion Sent, baseline `completionFloor`, one complete matching receipt after the Blocked receipt;
     `SubmittedBodies == [blocked.Body, completion.Body]`, no Pending rows. A third turn-end call adds no prompt
     or attempt; recheck the common oracle. No completion row is canceled or removed to simplify the count.
  Red under the scaffold: child is Queued at step 2. PC-47 replaces the turn-end `DeliverNextLockedAsync` assignment
  (`SessionMessageQueueService.cs:1568`) with `flush = FlushResult.Nothing`: step 3's complete-body receipt count is
  0 and the child row stays Pending. The busy guard assertion is also binding ordinary evidence.

- V-G3 `A_committed_blocked_child_note_is_recovered_once_by_the_stranded_sweep` | K | **4 min; CP-24; PC-48.**
  1. Own factory/repo and V-G1's seed, including idle parent UserPrompt/TurnEnd and registered adapter; retain the
     TurnEnd sequence as `floor`. Set `factory.Boundary.InterruptedTask = taskId` before the worker settle, as
     `AgentTaskCommitChildChainTests.cs:95-96` does. Await the reply service's `OnTurnEndAsync(workerSession)`.
     `CommitChainBoundary` throws at `settlement-saved` (`AgentTaskCommitEndpointTests.cs:342-353`), after the
     settlement save (`AgentTaskReplyService.cs:2629`) and before `ReconcileAsync` enqueues the worker completion
     (`:2651-2671`); the turn-end wrapper catches the interruption (`:102-125`). Assert `Boundary.Interrupted` true.
  2. Fresh scope: worker Succeeded, child Blocked with V-G1's reason/event/complexity assertions; one parent queue
     row, the child-owned Pending note with the common oracle's captured identity, attempts 0 and null floor.
     The worker's `AgentTaskLandNotifications` obligation exists with `QueueMessageId == null`, and there is no
     queue row with its `SourceLandNotificationId` (the chain fixture checks this boundary at `:113-119`).
     `SubmittedBodies` and matching parent UserPrompt counts are zero. Dispose this scope; retain only ids, bodies
     and scalar expected fields, not tracked entities.
  3. In another fresh scope set **only** that existing Blocked row's `CreatedAt` to
     `factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.AddSeconds(-(verification.StrandedAgeSeconds + 1))`,
     where `verification` is `IOptions<SupervisionSettings>.Value.DeliveryVerification`; save and dispose. This
     models the committed row aged past the sweep's cutoff (`SessionMessageQueueService.cs:1630-1655`;
     `QueueAttention.cs:72-79`), with no wall-clock sleep or production threshold change. The original producer
     supplies the body, origin, task id and Pending status; the test does not synthesize a replacement queue row.
  4. Construct `recoveredQueue = ActivatorUtilities.CreateInstance<SessionMessageQueueService>(factory.Services)`
     (public constructor `SessionMessageQueueService.cs:77-109`) so queue-local locks/state start fresh, using
     the same factory scope factory, runtime, options and delivery profiles. This is a stranding/reconstruction
     fixture, not a host restart: a second factory would clone a different database
     (`AntiphonWebAppFactory.cs:52-56`). No Agents row is seeded. The registered adapter is local liveness
     (`AgentSessionRuntime.cs:1670-1677`), the session is Running with null RunnerId, and Delegation origin enters
     the machine-origin arm (`QueueAttention.cs:95-100`; `SessionMessageQueueService.cs:1672-1713`).
  5. Call **only** `recoveredQueue.FlushStrandedQueuesAsync(ct)` → 1 delivered session. Fresh scope: the same
     Blocked queue id is Sent with baseline `floor`, expected body unchanged, exactly one complete UserPrompt
     receipt by the common oracle, `SubmittedBodies == [blocked.Body]`. Call the sweep again → 0; require
     per-body count 1, attempts 1 and the same one queue row. Do not call direct flush/turn-end, replay the worker
     settle, start a hosted scanner or reconcile its separate completion obligation: those would add an
     independent trigger or second queue row. Its null `QueueMessageId` remains an asserted fixture boundary.
  Red under the scaffold: the child is Queued at step 2. PC-48 removes `.Union(machineOriginSessionIds)` at
  `SessionMessageQueueService.cs:1681`: with no AlwaysOn agent, step 5 returns 0 and the complete receipt count is
  0. This case covers the committed Blocked note after interruption; the worker completion outbox's recovery is
  separately exercised by `Spawned_child_complete_brief_gated_operation_and_parent_receipt(true)` (`:117-142`).

- V-G5 `A_blocked_merge_child_note_reaches_the_parent_once_as_a_complete_user_prompt` | K | **3 min; CP-24; PC-49.**
  1. Own factory and `SeedRepoAsync`; add bare origin. Seed the Running parent session as the chain fixture at
     `AgentTaskCommitChildChainTests.cs:53-58`, then its UserPrompt and TurnEnd through `EntryAsync`, retaining
     the TurnEnd `floor`; register `AdapterFor`. Seed one Worker/Custom task with `Id == RootTaskId`, depth 0,
     `ParentSessionId = parent`, `ReplyTo Session`, repo/working directory = repo.Path, `Status Blocked`,
     `WorktreePath = repo.Path`, `WorktreeBranch = "feat/card-task-c097m-merge"`, `MergeTargetRef = "master"`.
     These are the Merge producer's required fields (`AgentTaskService.cs:3710-3725`;
     `CommitOnSettlePolicyTests.cs:176-187`); no Git rebase runs. Seed Human Merge/Medium `[ClaudeCode/Frontier]`,
     no pin, and hold `fable` through `ModelAvailability` as V-G1 does.
  2. Fresh scope: load `conflicted`; await the resolved `AgentTaskService.CreateMergeTaskAsync(conflicted,
     ["a.cs"], ct)`, require non-null, then `db.SaveChangesAsync()` and dispose. This is the direct producer
     boundary of V-A12c (`AgentTaskService.cs:3706-3803`), not a manual call to the note helper.
  3. Fresh scope: `child.ParentTaskId == conflicted.Id`, `child.ParentSessionId == parent`, Role Merge, Status
     Blocked, Complexity Medium, RoutingPinId null, reason contains `Merge/Medium chain` and `fable`, one Blocked
     event; exactly one parent queue row with `SourceTaskId == child.Id`, Origin Delegation, ConversationKey null,
     SourceLandNotificationId null, Pending, attempts 0, null floor and the reason digest/header. Capture its
     complete body/identity with the common oracle. There is no worker settlement in this method and no worker
     completion obligation; `SubmittedBodies` and parent prompt matches for this body are empty.
  4. `queue.FlushSessionAsync(parent)` → reload that row, baseline `floor`, Sent, unchanged complete body and one
     matching parent UserPrompt above the floor; `SubmittedBodies == [blocked.Body]`. A second flush retains
     exactly one body match and attempt, with zero Pending rows. Require the child still Blocked, undispatched
     (`AgentSessionId == null`). The receipt and adapter ordering use the common oracle.
  Red under the scaffold: child is Queued at step 3. PC-49 removes D-3's Merge Blocked-arm call to
  `EnqueueBlockedParentNoteAsync` (new call within `AgentTaskService.cs:3789-3803`, existing helper `:4610-4634`):
  the child remains Blocked but step 3 has zero child-owned queue rows and no recipient receipt. V-A12c/PC-46
  separately guard the construction outcome; this test supplies the complete receipt they do not select.

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
  ordering assertions still hold because the matrix step is the first in the ladder and its pointer is in-section,
  not a trailing notice; the trailing cut-notice order pins → holds → standing → features is untouched). The notice path's behaviour stays bound by the existing
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

The plan's PC-1–PC-49 table is the binding list (one per behaviour; exact method names above). PC-6 binds to V-A6's
`[Arguments]` row `Check`; PC-9 to V-A9; PC-10 to V-A8b's row `row-and-single-pin`; PC-13 to V-A12b's row
`stage-list-pin`; PC-21 to V-B10's row `Raw/Frontier`; PC-22 to the concrete wrong-grain query named in the plan;
PC-28 to V-D5 (R2-11); PC-30 to V-E4's row `apply`; PC-31 to V-E6's row `human`; PC-35 to V-F7's part (a); PC-24–PC-26
and PC-36–PC-38 name the Vitest titles above. Round 3: PC-39 to V-A15b; PC-40 to V-E12; PC-41 to V-B11's row
`with-row`; PC-42 to V-F7's part (b); PC-43 to V-G1; PC-44 to V-F2b; PC-45 to V-E6's row `auto`. Round 4: PC-46 to
V-A12c; PC-43's witness is the stage-1 row count and PC-44's the header-row count after a startup reconcile under
`Off`. Round 5: PC-47 to V-G2 (remove turn-end delivery), PC-48 to V-G3 (remove machine-origin recovery candidates),
PC-49 to V-G5 (remove the Merge Blocked note enqueue). Their exact production anchors and red assertions are in
the receipt recipes and plan PC table. None has been run; execute these controls method-scoped after land.

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
- The rebase-conflict trigger of the Merge producer is not re-driven (V-A12c and V-G5 call the producer as the settle site
  does). The notice path's window between the version write and the enqueue
  (`OrchestratorInstructionsService.cs:268-278`) is inherited and disclosed in the inventory, not recovered (R3-06).
  CP-24's ordinary class filter selects four receipt methods. PC-43 and PC-47–PC-49 select their individual exact
  method filters; the ordinary class filter is not a mutation selection.
- R5-01 restores busy-parent, stranded-row and Merge receipt evidence despite unchanged queue code
  (`server/Bundles/stage-review.md:6`; `docs/testing-and-build.md:1059-1064`). No slice adds a production queue file.
  V-G3's injected post-save interruption and queue-service reconstruction do not claim host restart coverage.

### Checkpoints

The closed list is the plan's `### Checkpoints` table (CP-1–CP-24); round 5 restores CP-24's four-method roster on
round 4's standalone class filter. It adds no checkpoint row. Build reuse is written as `CP-n` in the Build column with the same
`After` group as row n (`PlanTableImporter.cs:200-211`). CP-3/CP-4 execute 20 methods and **34 results** (V-A6
carries 8 `[Arguments]`, V-A8b 4, V-A12b 5; V-A12c, V-A15b and V-G4 are single); `Min` is 20. V-A6 is the class's one
`[Category("Unit")]` method; the class filter runs it with the integration methods. CP-10/CP-11 execute 6 methods and
9 results (V-B10 carries 3, V-B11 2). CP-12 executes 3 methods. CP-18 executes 7 methods and 10 results (V-E4 carries
2, V-E6 3). CP-20's roster is 12 methods (V-F2b added). CP-24 runs the four methods V-G1, V-G2, V-G3 and V-G5 of `MatrixChildReceiptTests`
through the ordinary class filter `/*/*/MatrixChildReceiptTests/*` on CP-4's build, serial (a real host and the
message queue), `Min = 4`, estimate 12 minutes, imported timeout 36 minutes; the round-3 method-segment wildcard
is gone with the partial. Code records the four exact identities and four executed results, zero failed/skipped;
import acceptance alone is not discovery evidence. Regression `Min` values are the
`[Test]` method counts read from master `d317c3f59`, as the plan states; `[Arguments]` and `MethodDataSource`
expansion is recorded by Code from the runner's output.

### Cost

As the plan states: ordinary green rows 155 minutes, red-first rows 25 minutes, authoring 470 minutes across seven
Code dispatches (S-0 20, S-A 110, S-B 75, S-C1 60, S-D 60, S-E 65, S-F 80) plus 90 for the optional S-C2. No
production timeout or assertion is loosened. Edited existing tests, each with its reason recorded above: three
`RoutingSettingsTab.test.tsx` cases (copy change), one `OrchestratorInstructionsRendererTests` method and its
`Sample()` fixture (section renumbering), the `Covered_writes_each_regenerate_and_notify` argument list (one new write
kind). The four receipt methods live in their own new class with its own helpers; `AgentTaskCommitEndpointTests.cs` and
`AgentTaskCommitChildChainTests.cs` are not edited (R4-01).

## Appendix A — withdrawn: the 2026-10-13 Appendix A (S3 stage-pin editor)

V-11–V-15, R-3, PC-8–PC-11 and CP-5 of the 2026-10-13 design are withdrawn by plan D-7. `StagePinEditor.tsx`,
`usePutRoutingPin`, `pinRequestFrom` and `requiredConfirmCopy` are not built. The one pin write the client gains is
`useClearRoutingPin` (V-C12), used only from the optional row editor after its three cell writes succeeded (V-C9).
