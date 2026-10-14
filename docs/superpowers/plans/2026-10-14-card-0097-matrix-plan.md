# CARD-0097 plan (rework) — the Role × complexity matrix as the single routing surface

Task `c491a627` (Plan, Frontier) wrote this plan at base `054795510c1249ffbf65c552ac4e6d3e87defb99`; task `785ac731`
(Plan, Frontier) repaired it for Final Review `2f218eeb` (F01–F12); task `9e5cdef3` (Plan, Frontier) **reduced** it on
2026-10-14 for Final Review `f39ef633` (R2-01–R2-11), continuing from `6356c5d6851f97c1fcbadc1a0abcbe007abe333d` on
branch `feat/card-task-9e5cdef3` (landing owner stays `c491a627`). Round 2 cut the mechanisms round 1 had grown
(ledgered migration, restore of pin bodies, notice reordering, a preview slice) and kept the smallest design that
holds the safety properties; the per-finding disposition is §10. Task `71e453db` (Plan, Frontier) repaired both
documents a third time on 2026-10-14 for Final Review `f406ced5` (R3-01–R3-06), continuing from
`f62858af96321dbd02d0e0140a6cef600dcf65a3` on `feat/card-task-71e453db`; the round-3 disposition is §11 and the
round-2 design is retained (nothing deleted in round 2 comes back). Round-3 citations were re-read from master
`ff4d84df522eaf483059ee11187fe68cc955d44c`, which contains `d317c3f59`; the routing, chain, renderer and queue
bodies cited below are unchanged between the two. Task `c733486f` (Plan, Frontier) repaired both documents a fourth
time on 2026-10-14 for Final Review `ce3b2bda` (R4-01–R4-03), continuing from
`7065ed10aee8b9f111ddc904370c7cb5f9119f6e` on `feat/card-task-c733486f`; the round-4 disposition is §12. Round 4
touched only the receipt rows round 3 added: it kept one producer receipt (V-G1) and the notice receipt (V-F2b),
rewrote both onto fixtures the new test file owns, moved the Merge Blocked check to the service recipe (V-A12c) and
withdrew V-G2, V-G3 and V-G5. Round-4 citations were re-read from master `38a100f583f2a5f0568e007b6f066cc8fec7a693`;
the routing, queue and fixture bodies cited are unchanged from `ff4d84df5`.

Task `a620633a` (Plan, Frontier) repairs R5-01 from Final Review `2ec3e207`, continuing from
`66aaa500be8b36561ed53c7ea0758aac86b724a7` without rebasing or merging. **Round 5 supersedes the round-4 receipt
withdrawals:** V-G2, V-G3 and V-G5 return as three exact methods beside V-G1 in the standalone
`MatrixChildReceiptTests`; V-A12c remains its service-level construction check. Current receipt anchors in §13 and
the amendment are against fetched master `c24ad5682315a0c9fb6d4a123d0cf8bce4e538c0`. No production change is part
of this repair. Landing owner remains `c491a627-6e46-47ee-8035-e682776f3aa4`.

`origin/master` at the initial reduction was `d317c3f593112e8da01a046694db2e5671594969`. CARD-1234 (`ba40230d9`, `d317c3f59`) and
CARD-1213 (`6d49673fa`) are on it, so the collision waits of the previous rounds are over; the file:line citations
below were re-read from that master (`git show origin/master:<path>`), not from this branch. Live reads at repair time
(~08:35Z, server2 mirror, injected API base): `GET /api/version` `893ddfcee` (capabilities `land-v2`,
`operator-shutdown-v1`). The pin and cell census (13 stage-wide Human Required pins, no card pins, three empty
any-role rows) was re-confirmed by Final Review `f39ef633` and is not re-derived here. Round-5 reads of
`GET /api/runner-defaults` and `GET /api/session-runners` returned HTTP 200 (revision 2; catalogue read at
10:20Z). Placement is resolved from those routes at dispatch. Lane: default placement, portable TUnit/Vitest;
omit `-Runner` and `-Platform` (no host or OS requirement for these checkpoints).

This plan supersedes CARD-0097 S3 (the `StagePinEditor` over pins, held in
[the 2026-10-13 test design](2026-10-13-card-0097-test-design.md) Appendix A). S1, S2, S4 and S5 of the 2026-10-13
plan are landed on master (`acfb1c325`, `7549067d6`, `47d807c14`, `00ec864ba`) and are built on, not changed. The
investigation is [the matrix-vs-pins investigation](../../investigations/2026-10-14-card-0097-role-x-complexity-matrix-vs-pins.md);
this plan is its successor, not a second mandate for its recommendations. The verification companion is
[the test-design amendment](2026-10-14-card-0097-matrix-test-design.md).

## 0. Outcome

**The operator instruction (2026-10-14) is: "we should make sure the pins are set on Role x complexity matrix …
whenever we ask you to change routing we should be changing these."** The investigation confirmed three gaps:

1. **The matrix governs no dispatch today.** A cell is loaded only when a create carries `-Complexity`
   (`server/Application/Services/AgentTaskService.cs:923-932`); without it the create resolves through
   `ResolveLevel`/`ResolveAgentKind` (`:1071-1072`, `:3976-3996`, `:4019-4055`) after the pin overlay (`:803-836`).
2. **The 13 operator routing changes are pins, not cells.** `RoutingPins` is a separate table the chain loader does not
   read (`server/Application/Services/ComplexityRoutingService.cs:312-343` reads the cell, the any-role row and config).
3. **A Required pin with a candidate replaces the cell outright** (`server/Application/Services/RoutingCandidates.cs:58-63`),
   so cells written today would still be bypassed, and the matrix UI says so only in a row banner
   (`client/src/features/settings/RoutingSettingsTab.tsx:471`) while an empty cell reads `Unset — dispatch blocks`
   (`client/src/features/settings/routingSettingsModel.ts:46`).

The fix is seven slices plus one optional: the default tier setting (S-0); ordinary Worker creates and the two
server-created children walk the role row at that tier when a row resolves (S-A); the effective cell says what will
run and why (S-B); the matrix shows that overlay (S-C1); the script gets a row verb (S-D); an additive, idempotent
migration writes the 13 pins into cells behind a snapshot, and the operator clears pins with the existing verb
afterwards (S-E); the generated instructions and the docs learn the new precedence (S-F); a whole-row editor is the
last, optional slice (S-C2). Nothing here is active on the desktop server until it is restarted onto a build that
contains these commits (§7).

## 1. Ground truth: what the brief and the card assume versus what the code does

| Assumed | Code today (master `d317c3f59`) | Evidence |
|---|---|---|
| A cell is consulted only when a create passes `-Complexity` | True. `if (request.Complexity is { } complexity)` opens the walk; the final `else` is `ResolveLevel`/`ResolveAgentKind` | `AgentTaskService.cs:923-932`, `:1070-1073` |
| Precedence card pin > stage pin > chain (role cell, any-role, config) > Blocked | True for `-Complexity`. `ResolveAsync` picks `cardPin ?? stagePin`; `LoadChainAsync` reads cell, any-role, config, else `([], null, "config", null, "none")` | `RoutingPinService.cs:303-309`; `ComplexityRoutingService.cs:312-343` |
| `LoadChainAsync` refuses specialist roles | False. It does not validate the role. Only `ComplexityChainService.ValidateCellRole` refuses specialists, and only on the chain API | `ComplexityRoutingService.cs:312-343`; `ComplexityChainService.cs:255-269` |
| A Required pin with a candidate bypasses the chain; Preferred prepends | True. `pin.Applied && Required && pinSlots.Count > 0` returns the pin slots; otherwise pin slots first, then the chain, deduplicated; a Preferred pin with `chain: null` appends one role-policy candidate | `RoutingCandidates.cs:58-86`; `RoutingPinCandidateCreateTests.Required_list_plus_complexity_bypasses_the_chain` (`:363`) |
| An unset cell under `-Complexity` Blocks the task | True: `routingExhausted` → `Status = Blocked`, `FailureReason` = the exhausted sentence | `AgentTaskService.cs:1004-1016`, `:1448-1453`; `ComplexityCreateTests.All_cells_empty_blocks_with_the_D3_sentence_and_a_parent_note` (`:161`) |
| A routing pin only narrows what the caller asked | False. The overlay fills `AgentId` from an agent-only pin **after** the pin decision (`:831`) and the standing-agent block then sets `AgentKind = pinned.Kind` (`:858`) | `AgentTaskService.cs:770-781`, `:803-836`, `:839-860` |
| A single-candidate Required pin stamps `RoutingPinId` | False. `routingPinId` is set only when `routingWalk.Walked` (`:944-945`) or the composed pin list is walked (`:1036-1037`); a single-candidate pin arrives at the role-policy branch as the overlaid explicit pair | `AgentTaskService.cs:944-945`, `:1036-1037`, `:1070-1073`, `:1364-1367` |
| No `-Complexity` falls to RolePolicy with `DefaultLevel = High` | True; each shipped entry leaves `Kind` unset; Mutation/Test/Deploy are `Low`, Docs/Commit `Medium`, Investigate/Debug/Coverage/Merge `High`, Plan/TestDesign/Code/Review `Frontier` | `DelegationSettings.cs:349-373`, `:375` |
| `ResolveLevel` tolerates an undefined policy level | False since CARD-1234: a no-explicit-level create whose policy or `DefaultLevel` is undefined is 422 `role_policy_level_invalid`; a policy kind outside `DelegatableKinds` is 422 `role_policy_kind_unsupported` | `AgentTaskService.cs:3983-3991`, `:4025-4036`; `RolePolicyInvalidValuesCreateTests` |
| `ResolveAgainstRolePolicy` withholds unusable policy values | False. It composes `kind ?? policy.Kind ?? ClaudeCode` and `level ?? policy.Level ?? DefaultLevel` with no check. `RolePolicyReadService` does withhold (`Supported`, `Usable`, `Defined`) and is tested for it | `ComplexityRoutingService.cs:432-446`; `RolePolicyReadService.cs:51-69`, `:78-98`, `:104-105`, `:133-137`; `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` |
| The settlement Commit delegate goes through `CreateAsync` | False: `CreateCommitTaskAsync` resolves kind/level from RolePolicy plus the Commit pin via `RoutingCandidates.Compose` with `chain: null`, takes `Candidates[0]` without a hold check, stamps `RoutingPinId` when the list was walked, catches a pin-service throw into `"Routing pin service failed ({type}); using role policy."`, and constructs `Status = Queued` | `AgentTaskService.cs:3836-3869`, `:3900-3901` |
| The conflict resolver is routed like other Merge work | False: `CreateMergeTaskAsync` builds a Worker/Merge task with `ModelLevel = ResolveLevel(Worker, Merge, null)`, no pin decision, no `Complexity`, no `AgentKind` assignment (the entity default is `ClaudeCode`, `AgentTask.cs:97`) | `AgentTaskService.cs:3706-3797` (`:3770`) |
| `ComplexityChainService` composes nothing from pins and fires no signal | True: its constructor takes `AppDbContext`, `TimeProvider`, `ComplexityRoutingService`, settings, logger; `RoutingPinService` fires `OrchestratorInstructionsSignal` through `SignalPin` on upsert | `ComplexityChainService.cs:28-40`, `:228-233`, `:248-252`; `RoutingPinService.cs:48-49`, `:270` |
| `PUT /api/complexity-chains/{role}/{complexity}` has no concurrency token | True: body is `{candidates[{agentKind, modelLevel}], provenance, reason?, notAfter?}`; `DELETE` is a no-op when already clear | `ComplexityChainDtos.cs:6-12`; `ComplexityChainService.cs:239-252`; `server/Api/Endpoints/ComplexityChainEndpoints.cs:25-45` |
| `PUT /api/routing-pins` accepts a `GET` body | False. The request is `PutRoutingPinRequest` (`Role, Card, Provenance, Strength, AgentKind, ModelLevel, AgentId, ForbiddenAliases, NotBefore, NotAfter, Reason, Candidates`); a body carrying both the shorthand pair and `candidates` is 422 `Send either the agentKind/modelLevel shorthand or candidates, not both.` Each of the 13 live `GET` bodies carries both. This plan writes no pin bodies (S-E) | `RoutingPinDtos.cs:13-31`, `:37-57`; `RoutingPinService.cs:653-674` |
| The instructions snapshot has no matrix section | True: sections 1–7 are caps, hosts, defaults, holds, pins, levels, standing; the builder reads `RoutingPins` and nothing from `ComplexityChains` | `OrchestratorInstructionsRenderer.cs:111-182`; `OrchestratorInstructionsSnapshotBuilder.cs:30-40`, `:203` |
| The instructions body is bounded by a byte cap for any snapshot | Partly. `Fit` drops card pins, then holds, then standing lines, then runner features, and finally returns `Compose` even when still over `cap` (`:85-86`). `MaxBytes` is 4,096–65,536, default 16,384 | `OrchestratorInstructionsRenderer.cs:43-87`; `server/Application/Settings/OrchestratorInstructionsSettings.cs:9-11`, `:24` |
| The notice path is: cancel pending rows, advance the column, save, enqueue | True, and this plan leaves it as it is: `QueueNoticesAsync` marks prior Pending instructions rows `Canceled`, writes `session.OrchestratorInstructionsVersion`, saves, then `queue.EnqueueAsync(WhenIdle, deliverIfIdle: true)`; `ReconcileCoreAsync` returns early on an unchanged body | `OrchestratorInstructionsService.cs:156-157`, `:241-278` |
| `complexity-chain.ps1` has no test runner | False. `ComplexityChainScriptTests` runs the script with `pwsh` against an `HttpListener` stub (`StubApi`: `LastMethod`, `LastPath`, `LastBody`, `RequestCount`) | `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs:148-240` |
| `complexity-chain.ps1 set` writes one cell per call; `routing-pin.ps1 clear` deletes a stage pin by grain | True: `-Complexity` is `ValidateSet('Hard','Medium','Easy')`; one PUT per call; `clear -Role <R>` looks the stage row up (`cardId` null) and DELETEs by id | `scripts/complexity-chain.ps1:30-31`, `:204-226`; `scripts/routing-pin.ps1:273-291` |
| A checkpoint row can reuse a build by writing "(reused)" | False. The importer accepts `<project> -> bin-x/`, `CP-n`, `CP-n (-NoBuild)` or `n/a`; `CP-n` requires the same `After` group as row n | `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:200-229`; `docs/testing-and-build.md:400`, `:411` |
| The row editor can import the cell editor's draft helpers | False. `DraftCandidate` and `toDraft` are module-private (`ComplexityChainCellEditor.tsx:59-75`). The exported helpers are in `routingSettingsModel.ts` (`candidateListError :238`, `nextUnusedCandidate :258`, `CHAIN_KIND_OPTIONS :142`, `MAX_CHAIN_CANDIDATES :150`) | those files |
| `delegate.ps1` predicts routing before the create | False for routing: it echoes the resolved kind from the create response (`:1208-1212`). The two predictors are the Role policy section's `Effective next create` column and the delegate modal's tier preview, both fed by `GET /api/role-policy`, which projects pin-or-policy and reads no cell | `scripts/delegate.ps1:1208-1212`; `RolePolicySection.tsx:59`, `:93-95`; `DelegateModal.tsx:46-55`, `:109-110`; `RolePolicyReadService.cs:78-98` |
| The 13 live pins are Plan/TestDesign ClaudeCode/Frontier, Code ClaudeCode/High, Review Codex/Frontier, nine others ClaudeCode/Frontier then ClaudeCode/High | Confirmed by Final Review `f39ef633` (ids in §6). Custom has no pin. Each is `Required`, `Human`, with empty `forbiddenAliases`, null dates and no agent | Final Review `f39ef633`, evidence bullet 8 |
| Queued chain tasks re-walk the live matrix when their alias is held | True for list-governed tasks (`Complexity` or `RoutingPinId` set); a task with neither sits Queued with a model-hold event | `AgentTaskDispatcher.cs:709-748`, `:1749-1779`; `ComplexityRoutingService.cs:31-32` |
| A reroute ends matrix governance | True: `task.Complexity = null; task.RoutingPinId = null` | `AgentTaskService.cs:2813-2814`; `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` |
| CARD-1234 and CARD-1213 are still in flight | False at this repair: both are on master (`ba40230d9`, `d317c3f59`; `6d49673fa`). `DelegationSettingsValidator`'s RolePolicy loop is `:1322-1341` and the `ComplexityChains` loop `:1344-1375`; `DelegateModal.serverRoleLevel` is `:46-55` | `git log 15a9071fa..origin/master` |
| The live server runs master | False: `893ddfcee`; S1/S2 of CARD-0097 and this plan are inactive until a restart | live `GET /api/version` |
| A saved cell before-image can be `PUT` back as saved | False for two kinds: an Auto body over the migrated Human cell is 409 `complexity_chain_human`, and after a `DELETE` an Auto body is still refused while the any-role row at that tier is Human; a `notAfter` at or before now is 422. The request's default provenance is Auto | `ComplexityChainService.cs:163-173`, `:176-187`, `:191-203`; `ComplexityChainDtos.cs:6-10` |
| The card-pin truncation step keeps twenty card pins per board | False: `Fit` starts at twenty and decrements `cardPerBoard` to zero while the body is over `cap`, cutting `Sample()`'s one card pin before any later step | `OrchestratorInstructionsRenderer.cs:54-60`; `OrchestratorInstructionsRendererTests.cs:168-172` |
| The Blocked child's parent note has a receipt test | False: `All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note` asserts the queued row count; the chain test asserts a successful child's brief and the land notes with the receipt predicate `Kind == UserPrompt && Sequence > LastDeliveryBaselineSequence && Text == Body`, through `CommitEndpointWebAppFactory` and `queue.FlushSessionAsync` | `ComplexityCreateTests.cs:81`; `AgentTaskCommitChildChainTests.cs:140-157`, `:204-206`, `:217-250`; `AgentTaskCommitEndpointTests.cs:319-340` |
| The chain test's adapter and transcript helpers are reachable from a second `partial` of `AgentTaskCommitEndpointTests` | False: `ChainAdapter`, `ChainEntryAsync` and `ChainConfirmAsync` are private instance methods of the nested, non-partial `CommitChildChain` class; only `SeedRepoAsync` is on the outer class. The new test file owns its own copies (R4-01) | `AgentTaskCommitChildChainTests.cs:32`, `:217-250`; `AgentTaskCommitEndpointTests.cs:283-289` |
| One `FlushSessionAsync` on the parent delivers both notes a Shared worker's settle queues | False: the settle also mints the worker's own completion (the git note contains `commit task`; `ReplyTo Session`, Shared) and enqueues it keyed `task:<root>` after the save; a flush runs `DeliverNextLockedAsync` once and a null-key head is not batched, so the Blocked child's note goes on the first flush and the completion on a second one, as the chain test's two parent flushes do (R4-02) | `AgentTaskReplyService.cs:2573-2582`, `:2658-2671`, `:4715-4717`; `AgentTaskLandNotificationService.cs:138-148`; `SessionMessageQueueService.cs:1749-1763`, `:2410-2421`; `AgentTaskCommitChildChainTests.cs:141`, `:192`, `:212` |
| A fresh refresh harness delivers only the notice the test's write produces, with an observable baseline | False: `Notify` defaults to `All`, so a startup reconcile already notifies the idle harness session, and the session has zero transcript rows, so that first delivery has a null `LastDeliveryBaselineSequence`; the refresh tests set `Off` before the startup reconcile and `All` after it (R4-03) | `OrchestratorInstructionsSettings.cs:28`; `OrchestratorInstructionsRecipients.cs:44-45`; `OrchestratorInstructionsService.cs:233-234`; `SessionMessageQueueService.cs:3567-3575`; `OrchestratorInstructionsRefreshTests.cs:353`, `:362-363`; `BridgeQueueHarness.cs:378-391` |
| `RoutingPinRefDto` names the agent an agent-only pin binds | False: it carries id, card, role, provenance, strength, kind, level, `notBefore`, reason and a count; the resolved `Decision.Pin.AgentId` does, and the standing-agent block takes the agent's kind | `RoutingPinDtos.cs:77-88`; `RoutingPinService.cs:65-75`; `AgentTaskService.cs:831`, `:858` |
| A Pending queue row is typed by the stranded sweep once aged | True: `FlushStrandedQueuesAsync` discovers `Pending` rows with `CreatedAt <= now − StrandedAgeSeconds` (default 60); `FlushSessionAsync` types an idle session's row at once; both read the committed row from a fresh scope | `SessionMessageQueueService.cs:1599-1640`, `:1741-1752`; `QueueAttention.cs:72-76`; `SupervisionSettings.cs:338` |
| Unchanged queue code makes Blocked-child receipt tests optional | R5-01 rejects this premise: the review rule requires complete recipient evidence. The direct producer adds a row without calling queue enqueue; its committed row must be discovered by turn-end or recovery | `server/Bundles/stage-review.md:6`; `docs/testing-and-build.md:1059-1064`; `AgentTaskService.cs:4617-4633` (master `c24ad5682`); V-G1–V-G3, V-G5 |
| A stranded Blocked note needs an AlwaysOn agent to be discovered | Delegation origin is a separate candidate arm. With a Running local session and a registered adapter, an aged, unattempted note qualifies without an Agents row | `QueueAttention.cs:72-100`; `SessionMessageQueueService.cs:1650-1713`; `AgentSessionRuntime.cs:1670-1677`, `:1733-1735` (master `c24ad5682`); V-G3 |
| A direct transcript insert updates a previously loaded working-state projection | The queue reads the enabled `SessionStateStore`; an existing cached entry is retained until publication/reset. V-G2's local helper publishes its committed insert under the existing mutation lease | `SessionMessageQueueService.cs:5253-5254`; `SessionStateStore.cs:127-165`, `:186-196`; `AgentTaskLandReceiptWatermarkTests.cs:198-204` (master `c24ad5682`) |

## 2. Decisions

Design decisions (D-n) are the plan's; operator decisions (OD-n) are recorded under the brief's recommended defaults so
the operator can overrule any one before Code is dispatched. Round-2 changes are marked (R2-nn).

- **D-1 Stamp `Complexity` at create, after the pin overlay and the standing-agent block.** The stamp point is
  immediately before `var id = Guid.NewGuid()` (`AgentTaskService.cs:917`), after `:770-781` (pin decision), `:803-836`
  (pin overlay) and `:839-860` (standing agent). The predicate `MatrixDefaultApplies(request, liveFollowUp)` is a
  `static` method over the **post-overlay** request: `Complexity is null && AgentKind is null && ModelLevel is null
  && AgentId is null && !IgnoreModelDisabled && Kind == Worker && !AgentTaskRoles.IsSpecialist(Role) && !liveFollowUp`.
  When it holds, `_complexityRouting is not null` and `LoadChainAsync(role, DefaultComplexity).ResolvedFrom != "none"`,
  S-A sets `request = request with { Complexity = _settings.DefaultComplexity }` and `complexityDefaulted = true`;
  the existing `:923` branch then walks with the already-resolved `pinDecision`. By construction: a single-candidate
  Required pin has been overlaid into `AgentKind`/`ModelLevel` at `:813-826`, so it is an explicit pair and is **not**
  stamped (today's path, `RoutingPinId` null, Created detail `(explicit override)`); an agent-only pin has filled
  `AgentId` at `:831` and the standing agent's kind at `:858`, so it is not stamped either; a Required candidate list
  of two or more (`pinComposed.Walked`) skips the overlay, is stamped, and the walk composes the pin slots
  (`RoutingCandidates.cs:58-63`) with the bypass warning (`:934-941`) and `RoutingPinId` stamped (`:944-945`).
  Rejected: a stamp before `:736`; a predicate that re-reads the pin decision; reading cells inside `ResolveLevel`.
- **D-2 The fail-open guard is the resolved source, not the cell's existence.** The stamp happens only when
  `LoadChainAsync` returns `ResolvedFrom` ≠ `none` (role cell, any-role row or a non-empty config default). An unset
  row keeps today's RolePolicy path. `LoadChainAsync` does not validate the role (§1), so the specialist clause in D-1
  is the only thing keeping Check/Distill/Diagnose off the matrix. Cost: one or two `FindActiveAsync` queries per
  ordinary create.
- **D-3 Server-created children: the row governs only when it resolves and no Required pin with a candidate exists;
  otherwise each producer keeps its own existing selection, over a pin that is read once per producer call (R2-01,
  R2-02, R3-01).** `ResolveServerChildRoutingAsync(AgentTaskRole role, Guid? cardId, Func<PinRead, Task<ChildRouting>>
  legacy, CancellationToken ct)` returns `ChildRouting(AgentKind Kind, AgentModelLevel Level, TaskComplexity?
  Complexity, Guid? RoutingPinId, string? BlockedReason, string? Warning)` and is called by `CreateCommitTaskAsync` and
  `CreateMergeTaskAsync`, each passing its own `legacy` delegate. `PinRead(RoutingPinService.Decision? Decision,
  string? FailureType)` is the helper's single pin read handed down: `Decision` when `ResolveAsync` returned, else
  `FailureType` = the exception type name. The helper is the only place in a producer call that calls
  `ResolveAsync`; neither `legacy` delegate reads a pin (round 2's Commit delegate re-read it, `:3841-3868`, so a
  second read that threw after a successful first one fell into the `"using role policy"` arm and discarded a
  resolved Required pin — R3-01). Commit's `legacy(pin)` is today's selection split by `pin`: `pin.Decision` →
  `:3848-3863` (head of `Compose(decision, chain: null, …)`, `RoutingPinId` when `Walked && Applied`,
  `EnforceForbiddenAliases`); `pin.FailureType` → today's catch arm `:3865-3868` (`"Routing pin service failed
  ({type}); using role policy."` with `ResolveAgentKind`/`ResolveLevel(Worker, Commit, null)`). The `_routingPins is
  null` arm (`:3870-3873`) stays in the producer and does not call the helper. Merge's `legacy(_)` ignores `pin`:
  `ResolveLevel(Worker, Merge, null)` (`:3770`), kind left at the entity default. The delegate is async-compatible
  (`Task<ChildRouting>`) so Commit's `EnforceForbiddenAliases` and the row walk share one awaited shape. Steps, in
  this order:
  1. **Pin once.** `_routingPins.ResolveAsync(cardId, role, Ask(null, null, null, false))` inside a `try`; a throw
     returns `legacy(new PinRead(null, type))` at once — nothing else is read, and Commit's existing warning applies.
  2. **Row.** `LoadChainAsync(role, DefaultComplexity)` inside a `try`; a throw returns `legacy(new PinRead(decision,
     null))` with `Warning = "Routing read failed ({type}); using today's path."`; `ResolvedFrom == "none"` returns
     the same with no warning. The decision from step 1 travels with it, so a chain-read failure keeps the pin it
     has already resolved (V-A15).
  3. **A Required pin with one or more candidates is the exception and returns `legacy(new PinRead(decision, null))`**
     — the pin keeps today's producer behaviour: Commit takes the head of `Compose(decision, chain: null, …)`
     (`:3848-3858`), `RoutingPinId` when that list was walked (`:3861-3862`), `Complexity` null; Merge keeps
     `ResolveLevel(Worker, Merge, null)` with the pin not consulted (`:3770`), `Complexity` null, `RoutingPinId`
     null. The matrix UI shows the pin above the cell (S-B/S-C1); for Merge that display names a pin the producer
     does not read, as today, until the operator clears the pins (§6.3, §6.4).
  4. **Otherwise walk the row** with the same `decision`: `WalkAsync(DefaultComplexity, Worker, role, decision,
     cardId, null, false, ct)` inside a `try`; a throw returns the pin-or-policy pair — the head of
     `Compose(decision, chain: null, …)` when non-empty, else `ResolveRoutingPair(Worker, role, null, null)` — with
     `Complexity` null, `RoutingPinId` when that list was walked, `Warning = "Routing walk failed ({type}); using the
     pin or role policy."`. `Chosen` → that pair, `Complexity = DefaultComplexity`, `RoutingPinId = decision.Pin.Id`
     when `walk.Walked && decision.Applied`, `Warning = walk.SkippedWarning()` when non-empty. No `Chosen` →
     `BlockedReason = walk.ExhaustedSentence(decision) + " A human must choose; do not pick a kind yourself."`,
     `Complexity = DefaultComplexity`: the caller constructs the child with `Status = Blocked`, `FailureReason =
     BlockedReason`, adds a `Blocked` event and calls `EnqueueBlockedParentNoteAsync(task, reason)` (`:4610-4634`; the
     child's `ParentSessionId` is the settled/conflicted task's; with no parent session the method returns without a
     row, `:4612-4615`, V-G4). A resolved row with no survivor is not replaced by the RolePolicy pair (V-A13 for
     Commit, V-A12c for Merge).
  Consequences: with an empty matrix both producers select as today's code does (V-A8b, V-A12b bind the live-shaped
  pin list, a single pin, a card pin and a configured policy kind); a resolved Required pin is kept in each failure
  arm because the one decision travels into `legacy` and a walk failure falls to its head (V-A15, V-A15b); the
  `RoutingPins` table is read once per producer call — V-A15b counts the `RoutingPins` SELECTs through a
  `DbCommandInterceptor` and throws on a second one, so a re-read fails the test instead of silently changing the
  program; exhaustion Blocks only when a row resolved and the walk found no survivor (V-A13). Rejected: a helper that
  stamps under a Required pin (round 1); a `Blocked` outcome on any read failure; substituting the RolePolicy pair for
  a known Required pin; a `legacy` delegate that reads the pin itself (round 2 — R3-01); a row-first order whose
  stated reason ("no pin to discard") stopped being needed once the decision is handed down.
- **D-4 The effective cell DTO is computed as create would compute it, and withholds what RolePolicy would withhold
  (R2-03).** S-B calls `RoutingPinService.ResolveAsync(null, role, Ask(null, null, null, false))` for the stage
  decision, then `RoutingCandidates.Compose(decision, chain, label, null, null, resolve)` — the two calls `WalkAsync`
  makes (`ComplexityRoutingService.cs:211-219`). The RolePolicy fallback used when the composed list is empty comes from
  a new `RolePolicyReadService.PolicyTarget(DelegationSettings, AgentTaskRole)` returning the typed record
  `PolicyResolution(AgentKind? Kind, AgentModelLevel? Level, string? Alias, string? WithheldReason)`: `WithheldReason`
  is null with the usable pair and alias when the kind is null or in `DelegatableKinds` and the level is defined
  (the `Row` rules, `RolePolicyReadService.cs:53-57`); otherwise `Kind`/`Level`/`Alias` are null and `WithheldReason`
  is `"{role} policy names {kind ?? "ClaudeCode"}/{level}, which delegation cannot launch"` with the raw configured
  values (`Raw/Frontier`, `OpenCode/Medium`, `ClaudeCode/999`). `ComplexityEffective.Compose(decision, chain, label,
  resolvedFrom, policy, isDefaultTier)` is pure: an empty composed list with `policy.WithheldReason == null` gives
  `source: "rolePolicy"` and one candidate; with a reason it gives `source: "rolePolicy"`, `candidates: []` and that
  reason verbatim. **An agent-bound stage pin is an exception row, not a cell (R3-04).** When the stage decision's
  `Pin.AgentId` is non-null (an agent-only pin names a standing agent — `RoutingPinService.cs:213-227` refuses pool
  delegates), the ordinary default create fills `AgentId` at `AgentTaskService.cs:831`, takes the agent's kind at
  `:858` and is not stamped (D-1, V-A10/V-A11), so no row governs it. `Compose` therefore takes a seventh argument
  `string? agentLabel` — `"{Name} ({Kind})"` read by `GetEffectiveAsync` from `_db.Agents` for that id, null otherwise
  — and when it is non-null returns `source: "agent"`, `candidates: []`, `stagePin` set, `governsDefault: false` on
  each tier including the default one, `withheldReason: "stage pin binds standing agent {agentLabel}; an ordinary
  dispatch runs on that agent and does not walk this row"`. The row's stored list stays beneath it as today; the
  default target is withheld rather than guessed, with or without a row, for Required and Preferred alike (the
  overlay at `:831` does not look at strength). `withheldReason` is thus "why no default target is projected": an
  unlaunchable policy (`rolePolicy`) or an agent-bound stage pin (`agent`); `RoutingPinRefDto` (`RoutingPinDtos.cs:77-88`)
  is not widened. Card pins of any shape stay the per-row count (OD-5) and are not composed. V-A10's fixture and
  V-B11/V-B12 state one truth: the create binds the agent, and the cell says so. `ComplexityChainService` gains an
  optional `RoutingPinService? pins` constructor parameter (DI resolves it; the three test files that construct the
  service directly keep compiling). No cycle: `RoutingPinService` depends on `ComplexityRoutingService` only
  (`RoutingPinService.cs:34-46`). Rejected: calling `ResolveAgainstRolePolicy` for the fallback (it fabricates an alias
  for Raw/999); a nullable tuple (the reason is lost); folding an agent-only pin into the chain as `role` with
  `governsDefault` true (round 2 — R3-04, contradicted by V-A10); adding `agentId` to `RoutingPinRefDto` (a shared
  DTO on the 409 extension and the pipeline summary, widened for one label).
- **D-5 Effective data rides on the existing `?role=` view; the any-role list view is unchanged.** `ComplexityChainDto`
  gains an optional `effective` (null on the list view and on any-role rows) and `ComplexityChainListDto` gains
  `defaultComplexity`. Rejected: a new route.
- **D-6 The default-create previews are not changed by this card; the divergence is documented (R2-04).** The Role
  policy section's `Effective next create` column (`RolePolicySection.tsx:59`, `:93-95`) and the delegate modal's tier
  preview (`DelegateModal.tsx:46-55`) read `GET /api/role-policy`, whose `effective` projects the stage pin head or the
  policy and reads no cell (`RolePolicyReadService.cs:78-98`). After S-A an ordinary Worker dispatch with a resolved
  default-tier row runs on that row, so those two previews can disagree with the dispatch whenever a row exists for
  the role and no Required pin bypasses it. S-F's docs name that limitation beside both consumers, and follow-up card CARD-1250
  carries the rework: a preview must carry the create's context (kind, agent, explicit level, held heads,
  the configured tier) before it can claim to be a next-create verdict. Rejected (round 1's D-4b): teaching
  `serverRoleLevel` a `matrix` source without that context — the review showed it projecting a candidate head as a
  verdict for Orchestrator kinds, held heads and agent-only pins.
- **D-7 The row editor is the last, optional slice and edits nothing in the Role policy section.** S-C2's
  `ComplexityChainRowEditor` keeps its own draft list built from the exported `routingSettingsModel.ts` helpers
  (`CHAIN_KIND_OPTIONS`, `MAX_CHAIN_CANDIDATES`, `candidateListError`, `nextUnusedCandidate`); `ComplexityChainCellEditor.tsx`
  is not modified. It writes Hard, Medium and Easy with three sequential `PUT`s through `usePutComplexityChain` and,
  when a stage pin exists and the "Clear the stage pin" checkbox is on (default on), sends `DELETE /api/routing-pins/{id}`
  after the third PUT succeeds. A PUT failure stops the sequence and reports which tiers were written. The only pin
  write the client gains is that DELETE (`useClearRoutingPin`). The "Configure row" button lives on `RoleMatrixRow`
  (`RoutingSettingsTab.tsx:409`); `RolePolicySection.tsx` is untouched (its copy at `:47-50` is rewritten by S-F's docs
  pass only if S-C2 lands; see S-F). Rejected: a Strength radio or Required confirm (a cell has no strength; OD-2); a
  Configure button in the Role policy section (round 1's shared-file collision).
- **D-8 The script gets `-Complexity All` and `-ClearStagePin`; `-ClearStagePin` requires `All`.**
  `complexity-chain.ps1 set -Role Review -Complexity All -Candidates Codex/Frontier [-ClearStagePin]` issues the
  three PUTs (Hard, Medium, Easy; stop at the first non-2xx and print `Hard written; Medium refused: <error>; Easy not
  written`, exit 1), then with the switch GETs `/api/routing-pins?role=<Role>` and DELETEs the row whose `cardId` is
  null. `-ClearStagePin` with `-Complexity` other than `All`, or with `-Role Any`, or without `-Role`, exits 1 before
  any request. `clear -Complexity All` clears the three cells. `get -Role X` prints `effective: <source> <list>` from
  S-B beside the stored line. `routing-pin.ps1` stays for card pins, forbid lists, dates and agents. Verification is
  the existing script harness, extended with a scripted stub (`ScriptedApi`, F-C).
- **D-9 Migration is additive and idempotent: it writes cells behind a snapshot and clears no pin (R2-05, R2-06,
  R2-07).** `scripts/migrate-stage-pins-to-cells.ps1 [-Role <R>] [-Apply] [-Restore -Snapshot <file>] [-OutDir <dir>]`:
  - **Dry run (default).** `GET /api/routing-pins`; stage rows (`cardId` null) with one or more `candidates` and no
    `forbiddenAliases`, `notBefore`, `notAfter` or `agentId` are eligible; the others print as `manual`; card pins are
    ignored. For each eligible role `GET /api/complexity-chains?role=<R>` records the three cells' before-images: a
    cell whose `resolvedFrom` is `role` is `present` with its `candidates`, `provenance`, `reason` and `notAfter`;
    `any`, `config` and `none` are `absent`. Prints the table `role | pin id | pin candidates | Hard/Medium/Easy before
    | to write` and writes `<OutDir>/migrate-stage-pins-<yyyyMMdd-HHmmss>.json` with `apiBase`, `createdAt`, `pins[]`
    (the GET bodies, read evidence only, not re-sent), `cellsBefore[]` (`{role, complexity, present, candidates[{
    agentKind, modelLevel}], provenance, reason, notAfter}`) and `plan[]` (`{role, complexity, body: {candidates,
    provenance: "Human", reason: "<pin reason> (migrated from stage pin <id>)"}}` — the `PUT` shape,
    `ComplexityChainDtos.cs:6-12`). Prints the snapshot path. Exit 0, zero writes.
  - **`-Apply`.** Runs the dry run (a fresh snapshot and a fresh pre-write read), then `PUT`s each `plan[]` body in
    role then tier order. Each `PUT` is a full overwrite of that cell (OD-4), so a rerun after a crash or a partial
    run rewrites the same cells to the same values. The first non-2xx stops the run: prints `written: <list>; failed:
    <role>/<tier> <status>; not written: <list>`, exit 1. A rerun's snapshot shows the migrated values as
    before-images; the runbook says to restore from the first snapshot (§6.5).
  - **`-Restore -Snapshot <file>`.** Two preconditions before any write, then one action per `cellsBefore[]` entry,
    chosen by what the real `PUT` accepts (R3-02, R3-03):
    - *Precondition 1 (pins back first, R3-03).* For each snapshot `pins[]` entry that was migrated (eligible), `GET
      /api/routing-pins?role=<R>` must return an active stage row (`cardId` null) with `strength` `Required` and
      `candidateCount >= 1`. Otherwise exit 1 with zero writes and the line `restore refused: <R> has no Required
      stage pin; run routing-pin.ps1 set -Role <R> -Provenance Human -Strength Required -Candidates <pin
      candidates> -Reason <text> first`. A Required pin with candidates bypasses the row (`RoutingCandidates.cs:58-63`)
      and D-3 step 3, so once this holds the cells no longer decide a create and their removal opens no gap. An
      operator who has not cleared the pins passes this trivially.
    - *Precondition 2.* The snapshot's `apiBase` equals the base in use; otherwise exit 1, zero writes.
    - *Action per before-image* (`ComplexityChainService.cs:163-203` is the acceptance rule):

      | `cellsBefore[]` entry | Restore sends | Why |
      |---|---|---|
      | `present: false` | `DELETE /api/complexity-chains/{role}/{complexity}` (204 when already clear, `:239-252`) | the migrated cell is removed; the tier returns to any-role/config/none |
      | `present`, `provenance` `Human`, `notAfter` null or later than now | `PUT` the saved body (`candidates`, `provenance: Human`, `reason`, `notAfter`) | a Human body is accepted over the migrated Human cell (last-writer-wins, OD-4) |
      | `present`, `provenance` `Human`, `notAfter` at or before now | `DELETE`; printed `expired before restore; cleared` | the endpoint refuses a past `notAfter` with 422 (`:163-173`); the rule had lapsed on its own |
      | `present`, `provenance` `Auto` | `DELETE`; printed `Auto before-image not re-created; cleared` | an Auto body over the migrated Human cell is 409 `complexity_chain_human` (`:176-187`), and after a `DELETE` an Auto `PUT` is still refused while the any-role row at that tier is Human (`:191-203`); the restore does not re-create Auto cells |

      The dry-run table prints each `present` before-image with its provenance and suffixes an Auto one with
      `(restore clears)`, so the operator sees that outcome before `-Apply`; `-Role <R>` excludes a role from the
      run. The first non-2xx stops the run and prints the restored list, exit 1. Rerunnable. Pins are not written:
      this script clears none and creates none.
  - **Clearing pins is a separate, explicit operator command**: `scripts/routing-pin.ps1 clear -Role <R>` per role
    (`:273-291`), run only after the operator has compared `complexity-chain.ps1 get -Role <R>` (the S-D `effective:`
    line) before and after the apply (§6.3). No wrapper is added.
  Deleted from round 1: the ledger, `diffSha256`, drift refusals, the non-empty-target refusal, `-Resume`, `-ClearPins`,
  pin re-`PUT` on restore, and the S-B-active precondition. Rejected: migrating in `Program.cs` on start.
- **D-10 The instructions file gets a bounded matrix section before the pins section, and cell writes fire the
  existing signal (R2-08, R2-09).** Section 5 `## 5. Role × complexity matrix` (`Owner: GET /api/complexity-chains`;
  `default <tier>`), one line per routable role: `- Plan: Hard a, b; Medium (default) c; Easy unset; effective c via
  stage pin`. Bounds: at most three aliases per tier then `+N`; a line longer than 160 bytes is cut at 157 and ends
  with `…`; 14 lines. The section enters the truncation ladder as its own **first** step, before the card-pin loop
  (R3-05): `Fit` (`OrchestratorInstructionsRenderer.cs:43-87`) tries the full body, then the body with the section
  collapsed to the single line `… 14 rows: GET /api/complexity-chains` (rendered in place under the heading, not
  among the trailing cut notices, which are unchanged), and only then the existing steps — the card-pin loop
  (`cardPerBoard` 20 → 0, `:54-60`), holds, standing lines, runner features. Order of truncation: matrix → card pins →
  holds → standing → features. Reason: the section is a bounded summary of one route its heading already names,
  while a card-pin line is a per-card exception with no other line in the file; and the first step is reachable from
  `Sample()` at a cap one byte under the full render, which round 2's position between card pins and holds was not —
  `Sample()` carries one card pin (`OrchestratorInstructionsRendererTests.cs:171`) whose line the card-pin loop cut
  first, so the matrix stayed expanded (V-F7 part b, PC-42). Pins → 6, levels → 7, standing → 8; the literal
  headings asserted in `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines`
  (`:33-63`) are edited with that reason. `OrchestratorInstructionsDelta` gains `DiffMatrix` beside `DiffPins`
  (`:159`) so an unchanged write produces no delta line; the body-hash early return (`OrchestratorInstructionsService.cs:156-157`)
  already keeps an unchanged body from rewriting or notifying. `ComplexityChainService` gains an optional
  `IServiceProvider? services` and fires `OrchestratorInstructionsSignal.Fire(services, $"complexity-chain {role}/{tier}")`
  after the save on upsert and clear, as `RoutingPinService.SignalPin` does (`:48-49`, `:270`). **The notice path is
  not changed**: cancellation of prior pending rows, the column write, the save and the enqueue stay in their current
  order (`OrchestratorInstructionsService.cs:241-278`); a cell write enters that path at the signal and nothing else
  about delivery, recovery or ordering is added. Deleted from round 1: its D-12 (moving the column write after the
  enqueue), the busy/idle/failed-enqueue/unchanged harness rows V-F4–V-F6 and V-F8, and the "delivery flow" they
  described. Rejected: a per-recipient durable notice obligation (no safety property of this card depends on it;
  the existing path's behaviour is bound by `OrchestratorInstructionsRefreshTests` already, §5 R-F).
- **D-11 No new hub event.** Cells and pins poll at 15 s (`complexityChains.ts:66`, `routingPins.ts:52`) and each
  client write invalidates both query families.
- **D-12 Lane and tests.** Narrow class filters for TUnit, file lists for Vitest, no whole-`Antiphon.Tests` lane, no
  E2E. Script behaviour is bound through the `pwsh`-plus-`HttpListener` harness. Checkpoints are the closed list in §5.
- **D-13 Complete Blocked-child receipt evidence is required (R5-01).** Restore V-G2 (busy parent → turn end),
  V-G3 (committed-row stranding → fresh queue sweep) and V-G5 (direct Merge producer → parent receipt), retaining
  V-G1's two flushes and V-F2b's Off/startup/seeded-floor/All ordering. Reason: the ordinary Review contract at
  `server/Bundles/stage-review.md:6` and `docs/testing-and-build.md:1059-1064` requires these boundaries even when
  queue code is unchanged. Rejected: substituting V-A12c's Pending row, adapter counts, or the System-origin
  instructions test at `OrchestratorInstructionsRefreshTests.cs:209-233` for the Blocked child's complete prompt.
  Keep the standalone class and its local helper copies; the small committed-state refresh in `EntryAsync` uses
  `SessionStateStore.BeginMutationAsync`/`PublishCommittedAsync` (`:127-165`) around the existing database write.
  V-G3 uses `CommitChainBoundary`'s existing post-save interruption (`AgentTaskCommitEndpointTests.cs:342-353`)
  and a new `SessionMessageQueueService` instance over the factory's same dependencies (`:77-109`), not a second
  factory/database. This is a committed-row stranding/reconstruction test, not process-restart acceptance.
  Rejected: adding production delivery machinery or changing existing test files. Current source anchors in this
  decision are against master `c24ad5682`; exact recipes and method-scoped PC-47–PC-49 are in the amendment.

Operator decisions, written under the recommended default (unchanged by this repair):

- **OD-1 Default tier for a dispatch without `-Complexity`: `Delegation:DefaultComplexity = Medium`.** New setting,
  validated as a defined `TaskComplexity`. Alternatives: `Hard` (the existing
  `ComplexityCreateTests.No_complexity_is_unchanged_when_fable_is_held` (`:230`) seeds a Hard any-role chain and would
  change meaning); require `-Complexity` on each dispatch; a fourth "default" column per role.
- **OD-2 A cell does not refuse a disagreeing explicit `-Kind`/`-Level`; it is not consulted (explicit path), and the
  create carries a warning naming the cell it bypassed.** Required pins remain the refusal mechanism
  (`RoutingPinService.cs:345-358`, 409 `routing_pin_conflict`). Cost: after the operator clears the 13 pins,
  `delegate.ps1 -Role Code -Kind Codex` runs Codex with a warning instead of today's 409.
- **OD-3 The 13 pins are migrated by the script and cleared only by an explicit operator command after a read-back.**
  Nothing in S-0..S-F clears a pin on its own; until `routing-pin.ps1 clear` runs per role, each cell is displayed
  beneath the pin that overrides it (S-B/S-C1).
- **OD-4 `PUT /api/complexity-chains/{role}/{tier}` stays last-writer-wins, stated and tested.** V-B6 writes the same
  cell twice and asserts the second body is what `GET` returns with `updatedAt >= U1` and no 409. The migration's
  `PUT`s rely on it (D-9).
- **OD-5 Card pins stay card-level exceptions, read-only in the matrix.** Listed beneath the table as today
  (`RoutingSettingsTab.tsx:578-600`); S-C1 adds a per-row count in the role name cell.
- **OD-6 A stamped create whose row is exhausted is Blocked with the chain sentence, not refused.** The CARD-0090
  contract for a chain walk (`AgentTaskService.cs:1448-1453`); the dispatcher requeues it when a hold clears
  (`ComplexityDispatcherTests.Clearing_a_hold_requeues_a_routing_blocked_task`). Cost: a plain dispatch whose only
  listed alias is held becomes a Blocked task instead of today's 409 `model_disabled`. D-3 applies the same outcome to
  the Commit and Merge children when a row resolved and no Required pin with a candidate exists.
- **OD-7 Orchestrator-kind creates are not stamped.** They keep RolePolicy plus the `MinOrchestratorLevel` clamp.
  Disclosed consequence: an Orchestrator/Code create takes the Code pin's High today and, after the pins are cleared,
  RolePolicy's Frontier; a bare `ignoreModelDisabled` Review create likewise follows RolePolicy once its pin is gone.
  V-A7 and the V-A6 `IgnoreModelDisabled` row bind the exclusion; §6.4 states the consequence.

## 3. Slices

| Slice | What | Files (closed list) | Est. |
|---|---|---|---|
| **S-0 Setting** | `DelegationSettings.DefaultComplexity` (`TaskComplexity`, default `Medium`) and one validator line (`Delegation:DefaultComplexity must be Hard, Medium or Easy`) placed before the `ComplexityChains` loop (`:1344`). | `server/Application/Settings/DelegationSettings.cs` (property beside `:375`; validator beside `:1344`). Tests: new `tests/Antiphon.Tests/Application/MatrixDefaultComplexitySettingsTests.cs` (V-A9, two methods). | 20 min |
| **S-A Server: the role row governs ordinary dispatch and the two server-created children** | `MatrixDefaultApplies` (D-1) and the stamp before `:917`; Created event detail `"{Kind}/{Role} at {level} (matrix default {tier}) in …"` when stamped (`:1556-1560`); OD-2 warning `explicit kind/level: the {Role}/{tier} row was not consulted` when a row resolves and the request is explicit; `ResolveServerChildRoutingAsync` (D-3) called from `CreateCommitTaskAsync` (`:3843-3847` moves into the helper as the single pin read; `:3848-3863` and `:3865-3868` become its `legacy(PinRead)` selection; `:3870-3873` stays) and `CreateMergeTaskAsync` (`:3770` becomes its `legacy` delegate; the task gains `AgentKind`, `Complexity`, `RoutingPinId` from the result and a Warning event when the result carries one), including the Blocked construction and parent note. Scaffold commit first (CP-3): property-free predicate returning `false`, helper that reads the pin once and returns `legacy(pinRead)`. | `server/Application/Services/AgentTaskService.cs` (`:917`, `:1556-1560`, `:3706-3797`, `:3811-3935`). Tests: new `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` (V-A1–V-A8b, V-A10–V-A15b including V-A12c, V-G4; fixture from `ComplexityCreateTests.cs:466-490`, seeds from `RoutingPinServiceTests.cs:228-256`, `:289` and `CommitOnSettlePolicyTests.cs:176-184`); new `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs` (a standalone class; its four methods each construct their own `CommitEndpointWebAppFactory` as `AgentTaskCommitChildChainTests.cs:28` does and the file carries its own `SeedRepoAsync`, `EntryAsync` and `AdapterFor` helpers copied from `AgentTaskCommitEndpointTests.cs:283-289` and `AgentTaskCommitChildChainTests.cs:217-242`; no existing test file is edited; V-G1, V-G2, V-G3, V-G5; EntryAsync also publishes committed state per amendment F-C); `ComplexityCreateTests`, `RoutingPinCreateTests`, `RoutingPinCandidateCreateTests`, `ComplexityDispatcherTests`, `CommitOnSettlePolicyTests` run unchanged (R-A1–R-A5). Needs S-0. | 110 min |
| **S-B Server: effective cell DTO carries the overlay** | `ComplexityEffectiveDto { source, candidates, stagePin, governsDefault, withheldReason }` on `ComplexityChainDto.Effective` (nullable, `?role=` view only); `ComplexityChainListDto.DefaultComplexity`. `ComplexityChainService` takes `RoutingPinService? pins = null`; `GetEffectiveAsync` composes per D-4 through the pure static `ComplexityEffective.Compose(decision, chain, chainLabel, chainResolvedFrom, policy, isDefaultTier, agentLabel)`; the service reads the agent label from `_db.Agents` when `decision.Pin?.AgentId` is set. `RolePolicyReadService.PolicyTarget` and the `PolicyResolution` record extracted (behaviour-preserving for `Row`). | `server/Application/Dtos/ComplexityChainDtos.cs` (`:26-46`), `server/Application/Services/ComplexityChainService.cs` (`:28-40`, `:111-134`), new `server/Application/Services/ComplexityEffective.cs`, `server/Application/Services/RolePolicyReadService.cs` (`:51-69`, new static and record). Tests: new `ComplexityEffectiveComposeTests.cs` (V-B1–V-B4, V-B10, V-B11), new `ComplexityEffectiveHttpTests.cs` (factory `ComplexityChainApiWebAppFactory`, `ComplexityChainHttpTests.cs:142`; V-B5, V-B6, V-B12); `ComplexityChainRoleHttpTests`, `ComplexityChainServiceTests`, `RolePolicyReadServiceTests` unchanged (R-B1–R-B3). Needs S-0; parallel with S-A (no shared files). | 75 min |
| **S-C1 Client: cells show the overlay** | `ComplexityChainDto.effective?`, `ComplexityChainListDto.defaultComplexity?` types. `MatrixCell` renders the effective list first with a source badge (`Required Human pin → Codex/Frontier (gpt-6-astra)` red; `Falls back to RolePolicy → ClaudeCode/High (opus)` grey; `Falls back to RolePolicy → not launchable: <withheldReason>` red; `Own rule`/`Inherits Any role`/`Configuration fallback` as today) and the stored cell beneath it; the default-tier column header reads `Medium (default for an ordinary Worker dispatch without -Complexity)`; an unset non-default cell reads `Unset — a -Complexity <tier> dispatch blocks`. `NON_COMPLEXITY_BOUNDARY` and `pinEffectCopy` rewritten; `cardPinCountLabel(role, pins)` (OD-5). msw default handler gains `defaultComplexity: 'Medium'`. | `client/src/api/complexityChains.ts` (`:19-38`), `client/src/features/settings/routingSettingsModel.ts` (`:25`, `:39-46`, `:113`), `client/src/features/settings/RoutingSettingsTab.tsx` (`:409-531`), `client/src/test/mocks/handlers.ts` (`:76-82`). Tests: `routingSettingsModel.test.ts` (V-C1–V-C3), `RoutingSettingsTab.test.tsx` (V-C4–V-C6; the three cases at `:347`, `:373`, `:447` rewritten to the new copy with that reason), `complexityChains.test.tsx` (R-C1). Needs S-B. | 60 min |
| **S-D Script: one verb for a role-wide change** | D-8 in `complexity-chain.ps1` (`-Complexity` accepts `All`; `-ClearStagePin` switch with its three refusals; `get -Role X` prints `effective:`). `routing-pin.ps1` header points role-wide changes at this script. ASCII-only. No docs in this slice (S-F owns them). | `scripts/complexity-chain.ps1` (`:9-13`, `:30-34`, `:155-183`, `:186-237`), `scripts/routing-pin.ps1` (`:1-28` header). Tests: new `tests/Antiphon.Tests/Scripts/ScriptedApi.cs` (F-C: ordered `Requests`, per-path `Respond`, in-memory `Cells`/`Pins` store) and V-D1–V-D7 in `ComplexityChainScriptTests.cs`; its nine existing methods run unchanged (R-D). Needs S-B for `get`. | 60 min |
| **S-E Migration script (operator-run, additive, snapshot + restore)** | D-9: new `scripts/migrate-stage-pins-to-cells.ps1`. ASCII-only. Not run against the live server by Code; the operator runs it after §7. | new `scripts/migrate-stage-pins-to-cells.ps1`. Tests: new `tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs` (`ScriptedApi` from S-D; V-E1–V-E6, V-E12). Needs S-D (the stub). | 65 min |
| **S-F Instructions and docs** | Snapshot: `MatrixSection(DefaultComplexity, Rows)` with one `MatrixInstructionRow(Role, Hard, Medium, Easy, Effective, Source)` per routable role from `ComplexityChainService.ListAsync(role)` plus `GetEffectiveAsync`; renderer section and ladder step per D-10; `DiffMatrix`; `ComplexityChainService` fires the signal (D-10); `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside `ModelAvailability` (`:173`). Docs: `docs/orchestration-loop.md:887-921` ("Routing pins beat RolePolicy" becomes "The matrix governs an ordinary Worker dispatch; a pin is the exception"), `:943` (the row saying the section has no editor stays true unless S-C2 lands; it then says the matrix row editor can clear a stage pin), `:952-957` (default tier; the preview limitation of D-6 beside the Role policy section and the delegate modal rows), the S-E runbook paragraph (§6.3–6.5 verbatim), `docs/antiphon-api.md:705-715` (`effective`, `defaultComplexity`, `withheldReason`, last-writer-wins) and `:666-679` (the D-6 limitation sentence on `/api/role-policy`), `docs/ops-http.md:139` (the same sentence), `.claude/skills/antiphon-delegate/SKILL.md:147` (omitted `-Complexity` on an ordinary Worker dispatch means the default tier). | `server/Application/Services/OrchestratorInstructionsSnapshot.cs`, `OrchestratorInstructionsSnapshotBuilder.cs` (`:30-40`, new `ReadMatrixAsync` beside `:203`), `OrchestratorInstructionsRenderer.cs` (`:43-87`, `:149-182`), `OrchestratorInstructionsDelta.cs` (`:20-27`, new `DiffMatrix`), `ComplexityChainService.cs` (`:28-40`, `:228-233`, `:248-252`), `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (`:173`), the docs above. Tests: `OrchestratorInstructionsRendererTests` (V-F1 literal renumbering in `:33-63` and `Sample()`; V-F7 bound and first-step elision), `OrchestratorInstructionsRefreshTests` (V-F2 `[Arguments("complexity-chain")]` and a `CoveredWriteAsync` arm at `:351-395`; V-F2b receipt method), `OrchestratorInstructionsDeltaTests` (V-F3). Needs S-A, S-B, S-D, S-E. | 80 min |
| **S-C2 Client: whole-row editor that can clear the stage pin (optional, last)** | New `ComplexityChainRowEditor.tsx` (Modal; own draft list from the exported model helpers; Reason; "Clear the stage pin <X>" checkbox shown only when a stage pin exists, default on; confirm copy `rowReplaceCopy(role, pin)`; three sequential PUTs then optional DELETE; partial-failure report). `useClearRoutingPin()` in `routingPins.ts` (invalidates `routingPinKeys.all`, `rolePolicyKeys.all`, `complexityChainKeys.all`). `RoutingSettingsTab` holds the row-editor target; `RoleMatrixRow` gets a "Configure row" button. | new `client/src/features/settings/ComplexityChainRowEditor.tsx`, `client/src/api/routingPins.ts` (`:44-54`), `client/src/features/settings/routingSettingsModel.ts` (`rowReplaceCopy`, `rowCellRequests`), `client/src/features/settings/RoutingSettingsTab.tsx` (`:409-460`). Tests: new `ComplexityChainRowEditor.test.tsx` (V-C7–V-C11), new `client/src/api/routingPins.test.ts` (V-C12), cases in `routingSettingsModel.test.ts` (V-C13); `ComplexityChainCellEditor.test.tsx` unchanged and its source untouched (R-C2). Needs S-C1. | 90 min |

Order: S-0 → S-A ∥ S-B → S-C1 ∥ S-D → S-E (after S-D) → S-F (after S-A, S-B, S-D, S-E) → S-C2 (optional, after
S-C1). Dependency reasons: S-B reads `settings.DefaultComplexity` (S-0); S-C1 needs S-B's wire contract (F-A); S-D's
`get` needs F-A; S-E's tests need S-D's `ScriptedApi`; S-F documents S-D's and S-E's verbs and renders S-B's effective
cell. S-E's `-Apply` is only meaningful once S-A and S-B are **active on the live server** (§7); its runbook
paragraph says so in its first line.

Commit and push after each slice. One Code task per slice, each in its own `-Worktree`, default placement, branched
from master at or after `d317c3f59`.

**Collisions.** The census is read with the Antiphon project id: `GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f`
and `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=Queued|Dispatched|Working`; the
`?unscoped=true` read is the null-project bucket and is not evidence. CARD-1234 and CARD-1213 are on master; no wait
remains for them. CARD-0506 S5 is `tests/Antiphon.E2E` only. Shared-area rule: before dispatching S-C1/S-C2, re-read the
project-scoped pipeline for Code tasks touching `client/src/features/settings`; before S-F, for tasks touching
`docs/orchestration-loop.md` or `docs/antiphon-api.md`.

## 4. Edge cases (each bound to a test in the amendment)

- Unset row at the default tier → RolePolicy path, Queued, no Blocked event, Created detail `(role policy)` (V-A2).
- Set row, each candidate held → Blocked with the chain sentence naming `<Role>/Medium`; queued parent note row with
  `SourceTaskId`, `NoteHeader`, `ContentDigest`; clearing the hold requeues it (V-A3, R-A4).
- Explicit `-Kind` or `-Level` with a set row → not stamped; warning names the row; Required pin 409 unchanged (V-A4,
  R-A2).
- Single-candidate Required stage pin plus a set row → not stamped; pin pair; `Complexity` null; `RoutingPinId` null;
  Created detail `(explicit override)` with the `[pin=…]` note (V-A5a). Required candidate list (2+) plus a set row →
  stamped; pin slots walked; `complexity chain bypassed by …`; `RoutingPinId` set (V-A5b).
- Stage or card agent-only Required pin plus a disagreeing cell → not stamped; `AgentId` = the standing agent;
  `AgentKind` = the agent's kind; `Complexity` null (V-A10, V-A11).
- The predicate excludes specialist roles, orchestrator kind, any agent id, an explicit kind, an explicit level,
  `ignoreModelDisabled` and live follow-ups (V-A6, eight `[Arguments]` rows).
- Orchestrator kind with a set Plan row → not stamped, `MinOrchestratorLevel` clamp still applies (V-A7).
- Settlement Commit child with a row and no Required pin: available head → that pair, stamped; held head with an
  available second → the second with the skipped warning; each candidate held → Blocked with the sentence, a Blocked
  event and a parent note (V-A8a, V-A14, V-A13). Empty matrix, or a Required pin with candidates, with or without a
  row → today's Commit path: the pin list head with `RoutingPinId`, or the single pin pair with `RoutingPinId` null, or
  RolePolicy `ClaudeCode/Medium`; `Complexity` null in each (V-A8b).
- Server-created Merge child with a row and no Required pin → the row candidate, stamped (V-A12a); exhausted → Blocked
  with the parent note row (V-A12c, the Merge mirror of V-A13). Empty matrix, with the live-shaped Merge pin list, a card pin, or `RolePolicy["Merge"].Kind = Codex`,
  or a row beside a Required Merge pin → today's `ClaudeCode/High`, `Complexity` null, `RoutingPinId` null (V-A12b).
- Chain read throws with a Required Commit pin present → the pin pair, Queued, `Complexity` null, a Warning event
  `Routing read failed` (V-A15). Resolved Commit row plus a disagreeing Required pin → the pin pair, one `RoutingPins`
  read for the whole producer call; a second read throws inside the test and fails it (V-A15b).
- Blocked Commit child whose settled task has no parent session → `Status Blocked`, no `SessionQueuedMessages` row,
  no exception (V-G4).
- `DefaultComplexity` undefined in configuration → validator failure naming the key (V-A9).
- Effective DTO: Required pin → `source pin`; Preferred pin → `pin+chain`; no pin, cell → `role`/`any`/`config`;
  nothing → `rolePolicy` with the policy pair; policy Raw/Frontier, OpenCode/Medium or ClaudeCode/999 → `rolePolicy`,
  empty candidates, the literal `withheldReason`; a card pin is not composed; `governsDefault` true only on the default
  tier (V-B1–V-B5, V-B10).
- Two PUTs on one cell → second wins, `updatedAt >= U1`, no 409 (V-B6).
- Row editor (optional): three PUTs in tier order; a 422 on Medium stops before Easy and the form names `Hard written;
  Medium failed`; DELETE pin only after three successes; checkbox hidden without a pin (V-C7–V-C11).
- Script `-Complexity All` stops on the first failing tier and prints which were written; `-ClearStagePin` refuses
  without `All`, with `Any`, or without a role, before any request; DELETE targets the `cardId == null` row only and
  only after three 2xx (V-D1–V-D7).
- Migration: dry run writes nothing and records before-images; manual and card pins are not written; apply PUTs three
  cells per role with Human provenance; a non-2xx stops apply or restore and names the cells written; a second apply
  sends the same PUTs and leaves the store unchanged; restore re-PUTs a present Human before-image whose `notAfter`
  has not passed, DELETEs an absent, an expired and an Auto one, sends no pin write (V-E1–V-E6); restore refuses
  before any write while a migrated role has no Required stage pin back, and proceeds once it has (V-E12).
- Instructions: a cell write regenerates the file and notifies through the existing path (V-F2); an unchanged matrix
  produces no delta line and a changed cell names the role (V-F3); the matrix section renders 14 rows with the per-line
  cap and `+N` elision at the default cap and collapses to the in-section pointer line, with `Sample()`'s card pin
  still rendered and no cut notice, when the cap is one byte short of the full render (V-F7).
- Receipts (R3-06, R4, R5): the Blocked Commit child's note reaches the settled task's idle parent session as one complete
  `UserPrompt` row equal to the queued `Body`, through the real settle path and queue, on the first flush; the
  worker's own completion note, which the same settle queues for the same parent, stays `Pending` until a second
  flush and then arrives as its own complete `UserPrompt`, after the first (V-G1). A cell-write notice is received as
  one `UserPrompt` equal to its `Sent` row, above a seeded transcript floor, with the session's version column equal
  to the state's and no other notice row for the session (V-F2b). V-G2 holds both Commit notes while the parent is
  busy, then delivers them on two turn-end calls. V-G3 interrupts after settlement commits, reconstructs the queue
  service and delivers the aged Blocked row via the sweep. V-G5 delivers the direct Merge producer's note. Each
  selects the receipt by session, attempt floor and complete body and rechecks per-body count 1 after another
  delivery trigger; `SubmittedBodies` follows queue sequence order (amendment V-G2/V-G3/V-G5).

## 5. Verification design

Full V/R/PC binding, fixtures and the wire contract are in
[the amendment](2026-10-14-card-0097-matrix-test-design.md). IDs are stable across both files.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S-0 tests committed, validator line not yet | `tests/Antiphon.Tests -> bin-c097m-0red/` | setting-red | `/*/*/MatrixDefaultComplexitySettingsTests/*` | V-A9 | 2 methods run; `An_undefined_default_complexity_fails_validation` fails (intentional red), `The_default_instance_validates` passes | 2 | 6 | false |
| CP-2 | S-0 | `tests/Antiphon.Tests -> bin-c097m-0/` | setting | `/*/*/MatrixDefaultComplexitySettingsTests/*` | V-A9 | 2 methods, 0 failed/skipped | 2 | 6 | false |
| CP-3 | S-A tests and scaffold committed, stamping not yet | `tests/Antiphon.Tests -> bin-c097m-ared/` | matrix-default-red | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A8b, V-A10–V-A15b, V-G4 | the class compiles against the scaffold and the 20 methods run (34 results: V-A6 8 rows, V-A8b 4, V-A12b 5); the rows the amendment lists as red fail by the named assertion, none by build or fixture error | 20 | 13 | false |
| CP-4 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` | matrix-default | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A8b, V-A10–V-A15b, V-G4 | 20 methods (34 results), 0 failed/skipped | 20 | 13 | false |
| CP-5 | S-A | CP-4 | matrix-default-regression | `/*/*/ComplexityCreateTests/*` | R-A1 | the class roster, 0 failed/skipped (count by the command below) | 14 | 8 | false |
| CP-6 | S-A | CP-4 | pin-create-regression | `/*/*/RoutingPinCreateTests/*` | R-A2 | the class roster, 0 failed/skipped | 13 | 8 | false |
| CP-7 | S-A | CP-4 | pin-list-create-regression | `/*/*/RoutingPinCandidateCreateTests/*` | R-A3 | the class roster, 0 failed/skipped | 17 | 8 | false |
| CP-8 | S-A | CP-4 | dispatcher-regression | `/*/*/ComplexityDispatcherTests/*` | R-A4 | the class roster, 0 failed/skipped | 9 | 8 | false |
| CP-9 | S-A | CP-4 | commit-on-settle-regression | `/*/*/CommitOnSettlePolicyTests/*` | R-A5 | the class roster (8 methods, 6 `[Arguments]` rows), 0 failed/skipped | 8 | 6 | false |
| CP-10 | S-B tests committed, production not yet | `tests/Antiphon.Tests -> bin-c097m-bred/` | effective-red | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4, V-B10, V-B11 | compile-only stub of `ComplexityEffective.Compose` returning an empty list and `PolicyTarget` returning a resolution with null fields; 6 methods (9 results) run, each fails | 6 | 6 | false |
| CP-11 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` | effective-compose | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4, V-B10, V-B11 | 6 methods (9 results), 0 failed/skipped | 6 | 6 | false |
| CP-12 | S-B | CP-11 | effective-http | `/*/*/ComplexityEffectiveHttpTests/*` | V-B5, V-B6, V-B12 | 3 methods, 0 failed/skipped | 3 | 6 | false |
| CP-13 | S-B | CP-11 | chain-http-regression | `/*/*/ComplexityChainRoleHttpTests/*` | R-B1 | the class roster, 0 failed/skipped | 3 | 5 | false |
| CP-14 | S-B | CP-11 | chain-service-regression | `/*/*/ComplexityChainServiceTests/*` | R-B2 | the class roster, 0 failed/skipped | 10 | 5 | false |
| CP-15 | S-B | CP-11 | role-policy-read-regression | `/*/*/RolePolicyReadServiceTests/*` | R-B3 | the class roster and its `MethodDataSource` rows (15 cases), 0 failed/skipped | 7 | 5 | false |
| CP-16 | S-C1 | n/a | client-overlay | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-C1–V-C6, R-C1 | 3 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 5 | false |
| CP-17 | S-D | `tests/Antiphon.Tests -> bin-c097m-d/` | script-row-verb | `/*/*/ComplexityChainScriptTests/*` | V-D1–V-D7, R-D | 16 methods (9 existing + 7 new), 0 failed/skipped | 16 | 8 | false |
| CP-18 | S-E | `tests/Antiphon.Tests -> bin-c097m-e/` | migration-script | `/*/*/MigrateStagePinsScriptTests/*` | V-E1–V-E6, V-E12 | 7 methods (10 results: V-E4 2 rows, V-E6 3 rows), 0 failed/skipped | 7 | 8 | false |
| CP-19 | S-F | `tests/Antiphon.Tests -> bin-c097m-f/` | instructions-render | `/*/*/OrchestratorInstructionsRendererTests/*` | V-F1, V-F7 | the class roster (7 methods, 3 `[Arguments]` rows), 0 failed/skipped | 7 | 6 | false |
| CP-20 | S-F | CP-19 | instructions-refresh | `/*/*/OrchestratorInstructionsRefreshTests/*` | V-F2, V-F2b, V-F9, V-F10 | the class roster (14 methods, 26 results; covered writes 7 rows, two read-failure methods 4 rows each), 0 failed/skipped | 26 | 12 | true |
| CP-21 | S-F | CP-19 | instructions-delta | `/*/*/OrchestratorInstructionsDeltaTests/*` | V-F3 | the class roster, 0 failed/skipped | 5 | 4 | false |
| CP-22 | S-C2 | n/a | client-row-editor | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ComplexityChainRowEditor.test.tsx src/api/routingPins.test.ts src/features/settings/routingSettingsModel.test.ts src/features/settings/ComplexityChainCellEditor.test.tsx` | V-C7–V-C13, R-C2 | 4 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 6 | false |
| CP-23 | all | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | lint gate (`client/src/lint.gate.test.ts`), whole client suite | `CLIENT TESTS EXIT CODE: 0` and the aggregate line reports a nonzero passed count that Code records | n/a | 10 | true |
| CP-24 | S-A | CP-4 | child-receipt | `/*/*/MatrixChildReceiptTests/*` | V-G1, V-G2, V-G3, V-G5 | 4 named methods / 4 results (amendment receipt roster), 0 failed/skipped | 4 | 12 | true |

Lane for CP-1–CP-24: portable TUnit/Vitest on default placement resolved from current runner-defaults/catalogue;
no `-Runner`, no `-Platform`. CP-24 is serial integration (local fake adapters, isolated PostgreSQL and scratch Git).
TUnit rows run one class filter each through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints --
run --plan <this file>`), which takes the build slot itself; `Antiphon.Tests` is not run whole here. A `CP-n` Build
cell reuses that row's output with `--no-build` and shares its `After` group (`PlanTableImporter.cs:200-211`).
`Min` is a floor on executed results, not the roster: regression `Min` values are the `[Test]` method count read from
master `d317c3f59` with `git show origin/master:tests/Antiphon.Tests/<path> | grep -c "^\s*\[Test\]"`; Code re-runs that
command for each regression class when reporting and records the executed count the runner prints, including
`[Arguments]` and `MethodDataSource` expansion. Green Vitest runs print only the aggregate line; Code records it.

### Positive controls (pending; one per behaviour; Mutation is not run in this card)

| PC | Exact method | Case → mutation → expected red |
|---|---|---|
| PC-1 | `MatrixDefaultComplexityCreateTests.A_plain_create_walks_the_role_row_at_the_default_tier` | Plan/Medium cell `[Codex/Frontier]` → skip the stamp → `ClaudeCode/Frontier` from RolePolicy |
| PC-2 | `MatrixDefaultComplexityCreateTests.An_unset_row_keeps_the_role_policy_path` | no rows → stamp unconditionally → `Blocked` |
| PC-3 | `MatrixDefaultComplexityCreateTests.An_exhausted_default_row_blocks_with_the_cell_name` | each candidate held → force `RefuseIfExhausted` → `RoutingExhaustedException` |
| PC-4 | `MatrixDefaultComplexityCreateTests.An_explicit_kind_is_not_stamped_and_warns` | `-Kind ClaudeCode` with a Plan row → stamp anyway → the walk runs under the explicit kind and `Routing != null` |
| PC-5 | `MatrixDefaultComplexityCreateTests.A_required_candidate_list_plus_a_row_is_stamped_and_walks_the_pin` (V-A5b) | list + row → pass `Decision.None` to `WalkAsync` → row candidate chosen, no bypass warning |
| PC-6 | `MatrixDefaultComplexityCreateTests.The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled` (row `Check`) | Check → drop the specialist clause → `true` |
| PC-7 | `MatrixDefaultComplexityCreateTests.An_orchestrator_create_keeps_role_policy_and_the_level_floor` | Orchestrator + Plan/Medium `[ClaudeCode/Low]` → stamp → `Low` ≠ `MinOrchestratorLevel` |
| PC-8 | `MatrixDefaultComplexityCreateTests.The_settlement_commit_child_takes_the_commit_row` (V-A8a) | Commit/Medium `[Codex/Frontier]` → return `legacy(pinRead)` unconditionally → `ClaudeCode` |
| PC-9 | `MatrixDefaultComplexitySettingsTests.An_undefined_default_complexity_fails_validation` | `(TaskComplexity)999` → drop the validator line → `Succeeded` |
| PC-10 | `MatrixDefaultComplexityCreateTests.A_required_pin_with_candidates_keeps_the_commit_child_on_todays_pin_path` (V-A8b, row `row-and-single-pin`) | row + Required single pin `Codex/Frontier` → walk the row despite the pin → `ClaudeCode/High` from the row, `Complexity Medium` |
| PC-11 | `MatrixDefaultComplexityCreateTests.A_stage_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` (V-A10) | stage agent-only pin + Codex cell → evaluate the predicate before the pin overlay → `AgentKind Codex` ≠ the agent's kind |
| PC-12 | `MatrixDefaultComplexityCreateTests.The_server_created_merge_child_takes_the_merge_row` (V-A12a) | Merge/Medium `[Codex/Frontier]` → keep `ResolveLevel` only → `ClaudeCode/High` |
| PC-13 | `MatrixDefaultComplexityCreateTests.An_empty_matrix_or_a_required_pin_keeps_the_merge_child_on_todays_program_default` (V-A12b, row `stage-list-pin`) | no row + Merge pin `[ClaudeCode/Frontier, ClaudeCode/High]` → resolve the pin in the legacy arm → `Frontier` ≠ `High` |
| PC-14 | `MatrixDefaultComplexityCreateTests.An_exhausted_commit_row_blocks_the_commit_child` (V-A13) | each candidate held → fall back to the RolePolicy pair → `Status Queued` |
| PC-15 | `MatrixDefaultComplexityCreateTests.A_held_commit_row_head_falls_to_the_next_candidate_with_the_skipped_warning` (V-A14) | head held → take `Candidates[0]` without the walk → `ClaudeCode` |
| PC-16 | `MatrixDefaultComplexityCreateTests.A_chain_read_failure_keeps_the_required_commit_pin` (V-A15) | routing service over a disposed context + Required pin `Codex/Frontier` → catch into the RolePolicy pair instead of `legacy(pinRead)` → `ClaudeCode/Medium` |
| PC-17 | `ComplexityEffectiveComposeTests.A_required_pin_replaces_the_cell_and_sources_pin` | Required pin + cell → return the chain → `source role` |
| PC-18 | `ComplexityEffectiveComposeTests.A_preferred_pin_prepends_and_sources_pin_plus_chain` | Preferred + cell → ignore the pin → first candidate is the cell head |
| PC-19 | `ComplexityEffectiveComposeTests.No_pin_and_no_cell_falls_back_to_role_policy` | empty → return empty → `source rolePolicy` missing |
| PC-20 | `ComplexityEffectiveComposeTests.Governs_default_is_true_only_on_the_default_tier` | Hard vs Medium → true for both → red on Hard |
| PC-21 | `ComplexityEffectiveComposeTests.An_unlaunchable_policy_is_withheld_with_its_reason` (V-B10, row `Raw/Frontier`) | policy kind Raw → build the resolution through `ResolveAgainstRolePolicy` → candidates non-empty, reason null |
| PC-22 | `ComplexityEffectiveHttpTests.Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded` | in `GetEffectiveAsync`, resolve the decision with the role's first active card pin's `CardId` (`_db.RoutingPins.Where(p => p.Role == role && p.CardId != null && p.ClearedAt == null).Select(p => p.CardId).FirstOrDefaultAsync()`) instead of `null` → `stagePin.id == C` → red |
| PC-23 | `ComplexityEffectiveHttpTests.Put_twice_is_last_writer_wins_without_a_409` | second PUT → refuse on an existing Human row → 409 |
| PC-24 | `routingSettingsModel.test.ts` V-C1 | source `rolePolicy` → ignore `effective` → `Unset — dispatch blocks` |
| PC-25 | `RoutingSettingsTab.test.tsx` V-C4 | Required pin row → render stored list first → pin line not first |
| PC-26 | `RoutingSettingsTab.test.tsx` V-C5 | default column → drop the header suffix → `(default` missing |
| PC-27 | `ComplexityChainScriptTests.ClearStagePin_requires_All_and_a_named_role` (V-D3) | `-Complexity Hard -ClearStagePin` → drop the refusal → `RequestCount > 0` |
| PC-28 | `ComplexityChainScriptTests.ClearStagePin_is_skipped_when_a_tier_failed` (V-D5) | 422 on Easy → DELETE anyway → a DELETE in the sequence |
| PC-29 | `MigrateStagePinsScriptTests.Manual_and_card_pins_are_listed_and_never_written` (V-E2) | forbid-list pin → migrate it anyway → a PUT for that role |
| PC-30 | `MigrateStagePinsScriptTests.A_non_2xx_stops_the_run_and_names_the_cells_written` (V-E4, row `apply`) | 500 on `TestDesign/Medium` → continue past it → a PUT after the failure |
| PC-31 | `MigrateStagePinsScriptTests.Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones` (V-E6, row `human`) | prior Human Plan/Medium cell → DELETE instead of PUT the before body → DELETE where a PUT was expected |
| PC-32 | `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (V-F1) | matrix section → omit it → `## 5. Role × complexity matrix` missing |
| PC-33 | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify("complexity-chain")` (V-F2) | cell upsert → drop the signal → `SubmittedBodies.Count == 0` |
| PC-34 | `OrchestratorInstructionsDeltaTests.An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` (V-F3) | byte-equal rewrite → `DiffMatrix` reports on equal input → a line appears |
| PC-35 | `OrchestratorInstructionsRendererTests.The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` (V-F7) | 42 cells × 8 candidates → drop the per-line cap → a `- <Role>:` line longer than 160 bytes |
| PC-36 | `ComplexityChainRowEditor.test.tsx` V-C7 (optional S-C2) | Save → PUT only Hard → two calls missing |
| PC-37 | `ComplexityChainRowEditor.test.tsx` V-C9 (optional S-C2) | pin present, box on → DELETE before the PUTs → order red |
| PC-38 | `routingPins.test.ts` V-C12 (optional S-C2) | DELETE → invalidate pins only → no second complexity-chains GET |
| PC-39 | `MatrixDefaultComplexityCreateTests.The_commit_child_reads_the_pin_once_and_keeps_a_resolved_required_pin` (V-A15b) | Commit/Medium `[ClaudeCode/High]` + Required single pin `Codex/Frontier` + an interceptor that throws on the second `RoutingPins` SELECT → read the pin again inside Commit's legacy selection → the throw is caught into `ClaudeCode/Medium` with the `using role policy` warning |
| PC-40 | `MigrateStagePinsScriptTests.Restore_refuses_until_each_migrated_roles_required_stage_pin_is_back` (V-E12) | pins store emptied after apply → skip precondition 1 → a DELETE is recorded |
| PC-41 | `ComplexityEffectiveComposeTests.An_agent_only_stage_pin_withholds_the_default_target_and_sources_agent` (V-B11, row `with-row`) | agent label + Plan/Medium row → ignore `agentLabel` → `source role`, `governsDefault true` |
| PC-42 | `OrchestratorInstructionsRendererTests.The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` (V-F7, part b) | cap `full − 1` with `Sample()`'s one card pin → place the matrix step after the card-pin loop → `Antiphon CARD-0822 Code` cut, `more: GET /api/routing-pins` present, `- Plan:` remains |
| PC-43 | `MatrixChildReceiptTests.A_blocked_commit_child_note_and_the_settled_tasks_completion_each_reach_the_parent_once_in_queue_order` (V-G1) | exhausted Commit row at the real settle → skip `EnqueueBlockedParentNoteAsync` in the Blocked arm → no `SessionQueuedMessages` row with `SourceTaskId == child.Id` at stage 1 |
| PC-44 | `OrchestratorInstructionsRefreshTests.A_cell_write_notice_is_received_as_one_complete_user_prompt_equal_to_its_queued_body` (V-F2b) | cell upsert after a startup reconcile under `Notify Off` → drop the signal → zero rows with the note header for the session and `SubmittedBodies` empty |
| PC-45 | `MigrateStagePinsScriptTests.Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones` (V-E6, row `auto`) | Auto before-image → PUT the saved Auto body after the DELETE → a PUT on Plan/Medium |
| PC-46 | `MatrixDefaultComplexityCreateTests.An_exhausted_merge_row_blocks_the_merge_child` (V-A12c) | Merge/Medium `[ClaudeCode/Frontier]` with `fable` held → fall back to the RolePolicy pair in the Merge arm instead of Blocking → `Status Queued`, no `SessionQueuedMessages` row |
| PC-47 | `MatrixChildReceiptTests.A_blocked_commit_child_note_reaches_a_busy_parent_once_after_turn_end` (V-G2) | replace the `DeliverNextLockedAsync` assignment in `OnTurnEndAsync` (`SessionMessageQueueService.cs:1568`, master `c24ad5682`) with `flush = FlushResult.Nothing` → after the persisted parent TurnEnd, the Blocked row stays Pending and the complete-body receipt count is 0 instead of 1 |
| PC-48 | `MatrixChildReceiptTests.A_committed_blocked_child_note_is_recovered_once_by_the_stranded_sweep` (V-G3) | remove `.Union(machineOriginSessionIds)` from stranded candidate construction (`SessionMessageQueueService.cs:1681`, master `c24ad5682`) → no Agents row exists in this fixture, the sweep returns 0, the committed note stays Pending and the matching receipt count is 0 |
| PC-49 | `MatrixChildReceiptTests.A_blocked_merge_child_note_reaches_the_parent_once_as_a_complete_user_prompt` (V-G5) | remove D-3's new `EnqueueBlockedParentNoteAsync` call from the Merge Blocked arm (insertion within `AgentTaskService.cs:3789-3803`; helper `:4610-4634`, master `c24ad5682`) → child remains Blocked but the child-owned row count is 0; no complete-body recipient receipt follows |

### Cost

Ordinary green rows (CP-2, CP-4–CP-9, CP-11–CP-24): 6+13+8+8+8+8+6+6+6+5+5+5+5+8+8+6+12+4+6+10+12 = 155 minutes.
Red-first rows (CP-1, CP-3, CP-10): 6+13+6 = 25 minutes. Authoring: S-0 20, S-A 110, S-B 75, S-C1 60, S-D 60, S-E 65,
S-F 80, S-C2 90 (optional) minutes. No production timeout or assertion is loosened by this plan; the three rewritten
client cases and the renumbered renderer literals are listed with their reasons in the amendment.

## 6. Rollout and migration

1. **Land order.** S-0; S-A and S-B (parallel); S-C1 and S-D (parallel); S-E; S-F; S-C2 if commissioned. Land each as
   soon as its Review is confirmed.
2. **Restart.** Nothing above is active until the desktop server is restarted onto a build containing it (§7). After
   the restart, confirm `GET /api/version` equals the intended SHA and `GET /api/complexity-chains?role=Plan` returns
   `effective` and `defaultComplexity` (the S-B shape) before running the migration.
3. **Migration (operator).** `pwsh -NoProfile -File scripts/migrate-stage-pins-to-cells.ps1 -OutDir <dir>` (dry
   run) against the live server; read the table and keep the printed snapshot path; `-Apply -OutDir <dir>`; then for
   each role run `complexity-chain.ps1 get -Role <R>` and read the Routing tab (each migrated cell now says
   `Required Human pin → …` above an own rule whose list equals the pin's). Only then clear the pins, one role at a
   time, with `pwsh -NoProfile -File scripts/routing-pin.ps1 clear -Role <R>`, and re-read `get -Role <R>`: the
   `effective:` line now reads `role` with the same list. The expected cell contents, from the live pins:

   | Role (pin id) | Hard = Medium = Easy |
   |---|---|
   | Plan (`bbdce6e6`) | ClaudeCode/Frontier (fable) |
   | TestDesign (`90e8d896`) | ClaudeCode/Frontier (fable) |
   | Code (`4683c049`) | ClaudeCode/High (opus) |
   | Review (`50328b61`) | Codex/Frontier (gpt-6-astra) |
   | Investigate (`de218932`), Mutation (`64e43198`), Debug (`909836ed`), Coverage (`858afdbb`), Docs (`11a17ef6`), Commit (`68d8d684`), Test (`6855ae74`), Deploy (`f890319f`), Merge (`e6d574aa`) | ClaudeCode/Frontier (fable), ClaudeCode/High (opus) |
   | Custom | no pin, no cell: RolePolicy fallback (`DefaultLevel` High, opus) |

   The reasons carried over are the pin reasons with the `(migrated from stage pin <id>)` suffix. Mutation's RolePolicy
   default is `Low` (`DelegationSettings.cs:357`); the migrated cell says Frontier then High because that is what the
   pin says today — the operator may edit that row afterwards. Writing the three tiers identically means `-Complexity
   Hard|Easy` behave like the default until the operator differentiates them.
4. **What changes for callers after the pins are cleared.** An explicit `-Kind`/`-Level` that disagrees with the row
   runs as asked with a warning instead of 409 `routing_pin_conflict` (OD-2); a plain dispatch whose row is exhausted
   is Blocked and auto-resumes (OD-6); a settlement Commit child or a conflict-resolver Merge child whose row is
   exhausted is Blocked with a note to the settled task's caller (D-3); until the Merge pin is cleared the Merge child
   keeps ignoring it, as today (D-3 step 3); an Orchestrator-kind create and an `ignoreModelDisabled` create follow
   RolePolicy, not the row (OD-7, V-A7, V-A6); `delegate.ps1 -Pin` still writes a card pin, which the cell shows as an
   exception (OD-5); the Role policy section's `Effective next create` column and the delegate modal's tier preview
   keep projecting pin-or-policy (D-6). Holds, quota, forbid lists on remaining pins and reroute are unchanged.
5. **Rollback, in this order (R3-03).** Pins first, cells second, so that no interval exists in which neither a pin
   nor the migrated cells decide a create:

   | Step | Operator action | Why in this position | Bound by |
   |---|---|---|---|
   | 1 | For each role whose pin was cleared in §6.3: `pwsh -NoProfile -File scripts/routing-pin.ps1 set -Role <R> -Provenance Human -Strength Required -Candidates <list> -Reason <text>`, list and reason from the §6.3 table | A Required pin with candidates bypasses the row for an ordinary create (`RoutingCandidates.cs:58-63`) and for the two children (D-3 step 3). Deleting the Review cells first would put a plain Worker/Review create on RolePolicy's ClaudeCode/Frontier (`DelegationSettings.cs:358`) until the Review pin is back, where before the migration it ran Codex/Frontier | — (operator step) |
   | 2 | Read back each role: `complexity-chain.ps1 get -Role <R>` prints `effective: pin …`; stop here if any `set` failed — nothing has been removed yet | the pin, not the cell, is the governing rule from here on | V-D6 (the line) |
   | 3 | `pwsh -NoProfile -File scripts/migrate-stage-pins-to-cells.ps1 -Restore -Snapshot <first snapshot> -OutDir <dir>` | the script re-checks step 1 itself and refuses with zero writes while a migrated role has no Required stage pin (D-9 precondition 1); with pins back, the order in which cells are removed or re-PUT does not matter | V-E12 (refusal, then proceed), V-E6 (per-case action), V-E4 `restore` (stop on non-2xx) |

   Cells written by the row editor after the first snapshot are overwritten or deleted by step 3, which is why the
   runbook says to restore from the first snapshot and before any editor use. An Auto before-image is cleared, not
   re-created (D-9). An operator who wants the cells gone without recreating the pins is not rolling back: that is
   `complexity-chain.ps1 clear -Role <R> -Complexity All` per role (S-D), chosen explicitly.

## 7. Live lag and restart sequencing

The desktop server is `893ddfcee` (2026-10-13 21:50 +0100) and `origin/master` is `d317c3f59`; CARD-0097 S1/S2/S4/S5
and this plan's slices are inactive until a restart. Sequencing:

- Restart from the **main checkout** after `git pull --rebase`, with `pwsh -NoProfile -File scripts/restart-apphost.ps1
  -ExpectedServerSha <full sha>`; exit 3 is a lock/worktree refusal to inspect, exit 5 is "server build unverified" with
  the lock retained, and a passing `/health` is not activation — read `GET /api/version`.
- Do **not** restart with tasks in flight or with a checkpoint run bound to a task: the checkpoint tool's owner read
  turns a vanished owner into exit 7 (`docs/testing-and-build.md:475`). The zero-in-flight gate is read with the
  project id (`?projectId=d4ea7ae9-…`) and the board task list, not `?unscoped=true`; the orchestrator re-reads it
  immediately before the restart and waits for in-flight Code checkpoints to finish.
- Restart once after S-0, S-A, S-B, S-C1, S-D, S-E and S-F have landed (S-C2 can follow in a second restart), then
  run §6.3.

## 8. Risks

- **The matrix lies in the window between S-A and the pin clearing.** Mitigated by S-B/S-C1 showing the pin inside
  each cell it overrides, and by OD-3 (pins are cleared only by the operator's command).
- **Fail-open must not become fleet-wide Blocked.** D-2's guard and D-1's predicate are the protection; V-A2 and V-A6
  are its tests, and PC-2/PC-6 are the controls that go red when either is removed. For children, D-3 steps 1–3 keep
  the legacy path on an empty matrix, a read failure or a Required pin, over one pin read per call (V-A8b, V-A12b,
  V-A15, V-A15b; PC-10, PC-13, PC-16, PC-39).
- **Behaviour change for exhausted plain dispatches and server-created children** (OD-6, D-3): Blocked instead of
  409, or instead of a silently queued RolePolicy pair. The attention feed names the cell and the dispatcher requeues
  on hold clear; orchestrators already handle `[task … blocked]` notes.
- **Explicit asks bypass cells** (OD-2): the create warning names the cell; the instructions file (S-F) tells
  orchestrators to omit `-Kind`/`-Level` unless the operator asked for a specific model.
- **Preview divergence** (D-6): the two pin-or-policy previews can disagree with a dispatch once rows exist; the docs
  say so beside each, and CARD-1250 owns the rework.
- **Last writer wins** on cells (OD-4): the migration's PUTs overwrite a pre-existing cell by design, after the dry-run
  table showed it; the first snapshot restores it.
- **Renumbered instruction sections** (D-10): an external reader that greps `## 5. Routing pins` must move to `## 6.`;
  the file header already says the file is generated and names the route per section.
- **Instructions byte budget** (D-10): the matrix is bounded per line and elided in place to a pointer as the first
  ladder step; V-F7 binds the per-line cap and the first-step elision at a cap one byte under the full render with
  `Sample()`'s card pin kept, not a universal byte figure; PC-42 goes red when the step is moved behind the card-pin
  loop (R2-10, R3-05).
- **Grok hold** until 2026-10-16 05:00Z: a cell naming Grok shows `held` and is skipped; harmless.
- **msw `onUnhandledRequest: 'error'`**: S-C2's DELETE needs a handler in each test that triggers it; a missing one is a
  red CP-22, not a silent gap.
- **Rollback order** (R3-03): the restore refuses with zero writes while a migrated role has no Required stage pin
  back; the cost is one GET per migrated role and a refusal the operator reads. **Auto before-images** (R3-02) are
  cleared, not re-created, by the restore; the dry-run table shows that beforehand.
- **Receipt tests over a real host** (R3-06, R4, R5): V-G1 boots its own `CommitEndpointWebAppFactory` and drives two
  parent flushes, the shape the existing chain test already runs (`AgentTaskCommitChildChainTests.cs:141`, `:192`);
  CP-24 is serial and reuses CP-4's build for four exact methods. V-G2 uses two parent turn-end calls; V-G3 uses the
  saved-boundary interruption and sweep; V-G5 calls the Merge producer directly. These are planned tests, not
  executed acceptance. Class-local helpers keep the chain test's private members out of the dependency list;
  code/test repairs after a failure require the failing case's base-commit comparison, not a presumed inherited red.

## 9. Sessions that wait for input

This plan adds no session that waits for input and changes no release deadline; the CARD-1083 checklist does not
apply. S-E is an operator-run script, not a session. With parking disabled, an input-waiting session has no automatic
release deadline; nothing here changes that.

## 10. Round-2 disposition (Final Review `f39ef633`)

| Finding | Disposition |
|---|---|
| R2-01 Merge not an empty-matrix no-op; child single-pin contradiction | **Fixed by reduction.** D-3 now reads the row first and returns each producer's own legacy delegate on `none` or on a Required pin with candidates; Merge's legacy is `ResolveLevel(Worker, Merge, null)` with no pin read. One child rule: a Required pin with candidates keeps today's path (Commit: list head with `RoutingPinId`, single pair without; Merge: RolePolicy High). V-A8b (4 rows), V-A12b (5 rows), PC-10, PC-13. |
| R2-02 chain-read failure discards a Required pin | **Fixed in round 2, completed in round 3.** Round 2 read the chain first and let Commit's legacy delegate re-read the pin, which R3-01 showed could discard a resolved Required pin on a second read; round 3 reads the pin once in the helper and hands the decision into `legacy` (D-3). No `Blocked` on any read failure. V-A15, V-A15b; PC-16, PC-39. |
| R2-03 V-B10 diagnostic unreachable from a nullable tuple | **Fixed.** `PolicyTarget` returns `PolicyResolution` with the usable pair or the literal rejected values in `WithheldReason`; V-B10 binds Raw/Frontier, OpenCode/Medium and ClaudeCode/999 with independent literal reasons; PC-21. |
| R2-04 preview disagrees with dispatch | **Deleted with the mechanism removed.** S-B2, D-4b, V-B7–V-B9, V-C15–V-C17, CP-16–CP-18 (old numbering) and PC-32 (old) are gone; D-6 states the divergence as a limitation, S-F documents it beside both consumers, and CARD-1250 owns the rework. |
| R2-05 migration state machine contradicts its tests | **Deleted with the mechanism removed.** No hash, no ledger, no drift refusal, no non-empty refusal, no `-Resume`; apply is a full idempotent overwrite behind a snapshot (D-9); V-E1–V-E6 bind one contract. |
| R2-06 restore replays a GET pin body the PUT rejects | **Deleted with the mechanism removed.** The script writes no pin body; pins are cleared by the existing `routing-pin.ps1 clear` and re-created by hand from the §6.3 table; restore re-PUTs cells in the shape `ComplexityChainDtos.cs:6-12` accepts (V-E6). Round 3 defines the restore per before-image kind against `UpsertAsync`'s refusals (R3-02). |
| R2-07 ledger ownership and recovery | **Deleted with the mechanism removed.** No ledger; a crash is rerun; the first snapshot is the restore source; the runbook orders editor use after the restore window (§6.5). |
| R2-08 notice save reorder lets a stale pending notice deliver | **Deleted with the mechanism removed.** Round 1's D-12 is gone; the notice path is unchanged (D-10); a cell write only fires the existing signal (V-F2). |
| R2-09 crash recovery and child-delivery verification missing | **Deleted with the mechanism removed.** No delivery flow is added; the Blocked note reuses `EnqueueBlockedParentNoteAsync` and the existing flushes; V-A3/V-A13 bind the row, V-A16 and PC-39/PC-40 (old numbering) are withdrawn. Round 3 adds the producer-to-recipient receipt rows V-G1–V-G5 and V-F2b and discloses the inherited notice window in the inventory (R3-06). |
| R2-10 4,096-byte test impossible | **Fixed in round 2, completed in round 3.** V-F7 drops the universal figure: it binds the per-line cap and `+N` elision at the default cap, and the in-place pointer at a cap derived from the full render (one byte short); PC-35. Round 3 moves the matrix step first in `Fit` so that cap reaches it with `Sample()`'s card pin kept (R3-05, PC-42). |
| R2-11 PC-34 binding ineffective | **Fixed.** The DELETE-after-failure control is bound to V-D5 `ClearStagePin_is_skipped_when_a_tier_failed` (PC-28). |

Net change: slices 9 → 8 (S-B2 deleted; S-C2 marked optional); checkpoint rows 26 → 23; positive controls 40 → 38
(38 includes the three optional S-C2 controls); V rows: S-A 16 → 17 methods, S-B 7 → 7, S-B2 3 → 0, S-C 17 → 13,
S-D 7 → 7, S-E 11 → 6, S-F 8 → 4. OD-1–OD-7 are unchanged.

## 11. Round-3 disposition (Final Review `f406ced5`)

| Finding | Disposition |
|---|---|
| R3-01 second pin read discards a resolved Required pin | **Fixed.** D-3 reads the pin once (step 1) and hands `PinRead` into each `legacy` delegate; Commit's delegate is selection over that read (`:3848-3863` / `:3865-3868`) with no `ResolveAsync` of its own; the `_routingPins is null` arm stays in the producer. V-A15b counts `RoutingPins` SELECTs through a `DbCommandInterceptor` and throws on a second one; PC-39. |
| R3-02 a saved Auto or expired before-image is not accepted back by the PUT | **Fixed.** D-9's restore acts per before-image kind against `UpsertAsync`'s rules: Human with a live `notAfter` → PUT; absent, expired Human and Auto → DELETE; the dry-run table marks Auto rows `(restore clears)`. V-E6 × 3; PC-31 (`human`), PC-45 (`auto`). |
| R3-03 rollback removes cells before pins are back | **Fixed.** §6.5 is an ordered table (recreate pins, read back, restore) and D-9 precondition 1 makes the script refuse with zero writes while a migrated role lacks a Required stage pin. V-E12; PC-40. |
| R3-04 effective cell contradicts agent-only pin routing | **Fixed.** D-4/F-A add `source: agent` with an empty candidate list, `governsDefault false` and a `withheldReason` naming the agent; the default target is withheld, not guessed; `RoutingPinRefDto` unchanged. V-B11 (U), V-B12 (A), V-C1 row `agent`; PC-41. |
| R3-05 V-F7's cap reaches the card-pin step first | **Fixed.** D-10 puts the matrix elision first in `Fit` (matrix → card pins → holds → standing → features); V-F7 part (b) derives `full − 1` from the real render and asserts the card pin is kept and no cut notice appears; PC-42 goes red when the step is moved behind the card-pin loop. |
| R3-06 producer-to-recipient receipts missing | **Fixed by adding tests, not machinery.** V-G1–V-G3 and V-G5 drive the real settle (`OnTurnEndAsync(workerSession)`) or the direct Merge producer through `CommitEndpointWebAppFactory` and assert the complete recipient `UserPrompt` (`Kind == UserPrompt && Sequence > LastDeliveryBaselineSequence && Text == Body`) for an idle parent, a busy parent at its turn end and the stranded sweep over the committed row; V-G4 binds the no-parent boundary; V-F2b binds the cell-write notice receipt and version identity; the inventory's loss column discloses the inherited version-before-enqueue window. CP-24; PC-43, PC-44. |

Net change in round 3: slices 8 → 8; checkpoint rows 23 → 24 (CP-24); positive controls 38 → 45 (PC-39–PC-45);
V rows: S-A 17 → 19 methods (V-A15b, V-G4) plus 4 receipt methods in `AgentTaskCommitEndpointTests` (V-G1–V-G3,
V-G5), S-B 7 → 9 (V-B11, V-B12), S-C 13 → 13 (one `it.each` row added to V-C1), S-D 7 → 7, S-E 6 → 7 (V-E12; V-E6
becomes three rows), S-F 4 → 5 (V-F2b). Ordinary checkpoint minutes 142 → 155; red-first 24 → 25. OD-1–OD-7 are
unchanged; nothing deleted in round 2 returns.

## 12. Round-4 disposition (Final Review `ce3b2bda`)

Historical disposition: round 4 repaired the fixture, V-G1 and V-F2b mechanics below, but withdrew three required
receipt cases. Final Review `2ec3e207` rejected those withdrawals as R5-01. §13 and D-13 now bind the restored
methods; the resolved R4-01–R4-03 mechanics are retained.

| Finding / row | Disposition |
|---|---|
| R4-01 the proposed partial has no access to the private nested chain helpers | **Fixed by removing the partial.** `MatrixChildReceiptTests` is a standalone class whose methods each construct their own `CommitEndpointWebAppFactory` (as `AgentTaskCommitChildChainTests.cs:28` does) and whose file carries private static `SeedRepoAsync`, `EntryAsync` and `AdapterFor`, each the body of the cited existing helper (`AgentTaskCommitEndpointTests.cs:283-289`; `AgentTaskCommitChildChainTests.cs:217-242`) with the real `AgentSessionRuntime.Register` and database writes kept. No existing test file is edited; S-A's closed list is unchanged in production files. CP-24's filter is the ordinary class filter `/*/*/MatrixChildReceiptTests/*`. Test-design F-C `K`. |
| R4-02 V-G1 expects one flush to deliver two notes | **Fixed by binding the deterministic two-note outcome.** V-G1 asserts, after the settle, the Blocked row (`ConversationKey` null) and the worker's completion row (`SourceLandNotificationId` set, higher `Sequence`), both `Pending`; after the first `FlushSessionAsync(parent)` the Blocked row `Sent` with its complete `UserPrompt` above its baseline and the completion still `Pending`; after a second flush the completion `Sent` with its own complete `UserPrompt` above its baseline and after the first receipt; each receipt selected by session, baseline floor and complete body with per-body exactly-one counts; `SubmittedBodies` equals the two bodies in that order. The queue is unchanged; the shape is the chain test's two parent flushes (`:141`, `:192`, `:212`). Method renamed `A_blocked_commit_child_note_and_the_settled_tasks_completion_each_reach_the_parent_once_in_queue_order`. PC-43 unchanged in mutation; its witness is the stage-1 row count. |
| R4-03 V-F2b counts a startup notice as the one submission | **Fixed with the `CoveredWriteAsync` order and a seeded floor.** `Notify = Off` before `ReconcileNowAsync("startup")`, a seeded `UserPrompt` and `TurnEnd` through the public `InsertTranscriptEntryAsync` (the returned `TurnEnd` sequence is the floor), `Notify = All`, then the cell write. Assertions: exactly one header row for the session, `Sent`, `LastDeliveryBaselineSequence == floor`; exactly two `UserPrompt` rows for the session, exactly one above the floor and equal to the body; `state.Revision == startup.Revision + 1`; `SubmittedBodies == [row.Body]`; `SessionVersionAsync == state.Version`. PC-44's witness is now zero header rows and an empty `SubmittedBodies`, which the `Off` startup leaves unambiguous. |
| V-G2 busy parent, delivered at its turn end | Round-4 withdrawal superseded by §13: exact Blocked Commit receipt method, CP-24, PC-47. |
| V-G3 stranded sweep over the committed row | Round-4 withdrawal superseded by §13: saved-boundary interruption, fresh queue instance and sweep receipt, CP-24, PC-48. |
| V-G5 Merge producer receipt | Round-4 withdrawal superseded by §13: direct producer-to-recipient method, CP-24, PC-49. V-A12c and PC-46 retain their separate construction assertions. |

Net change in round 4: slices 8 → 8 (S-A estimate 110 → 90 minutes); checkpoint rows 24 → 24 (CP-24 refiltered to
one method, 12 → 8 minutes; CP-3/CP-4 20 methods, 34 results); positive controls 45 → 46 (PC-46); V rows: receipt
methods 4 → 1 (V-G1 kept, V-G2, V-G3, V-G5 withdrawn), S-A service rows 19 → 20 (V-A12c), S-F 5 → 5 (V-F2b
rewritten). Ordinary checkpoint minutes 155 → 151; red-first 25 → 25. OD-1–OD-7 and the round-2 and round-3 design
are unchanged; no production file list changes.

## 13. Round-5 disposition (Final Review `2ec3e207`, R5-01)

Current source for this repair: master `c24ad5682315a0c9fb6d4a123d0cf8bce4e538c0`. The following source chain binds
the restored recipes; the test titles are in the amendment and the PC table, with no production queue edits:

| Case | Source order and bound evidence | Selection / cost / control |
|---|---|---|
| V-G2 busy parent | Seed the chain worker and a parent UserPrompt without TurnEnd (`AgentTaskCommitChildChainTests.cs:47-94`); settle worker (`:96`; `AgentTaskReplyService.cs:4712`, `:2629`, `:2651-2671`); ordinary flush holds while Working (`SessionMessageQueueService.cs:1761-1763`); persist parent TurnEnd and publish committed state (`SessionStateStore.cs:127-165`); call queue `OnTurnEndAsync` (`:1554-1568`) twice, one note per call. Match complete bodies and ordered receipts above their attempt floors, then repeat the trigger and retain per-body count 1 | CP-24; 3 min; PC-47 |
| V-G3 committed-row recovery | Set `factory.Boundary.InterruptedTask` before worker settle (`AgentTaskCommitChildChainTests.cs:95-96`); hook throws after save and before completion enqueue (`AgentTaskCommitEndpointTests.cs:342-353`; `AgentTaskReplyService.cs:2629`, `:2651-2671`). Fresh scope sees child and its Pending note; the worker completion obligation has no queue row. Age only the Blocked row's CreatedAt beyond the configured cutoff, reconstruct the queue through its public constructor (`SessionMessageQueueService.cs:77-109`) over the same factory services, then call only `FlushStrandedQueuesAsync` (`:1630-1713`). Match the receipt and repeat the sweep with count unchanged | CP-24; 4 min; PC-48 |
| V-G5 Merge producer | Seed the conflicted task's worktree fields (`CommitOnSettlePolicyTests.cs:176-187`) and idle parent (`AgentTaskCommitChildChainTests.cs:53-58`, `:86-90`); call `CreateMergeTaskAsync` then save (`AgentTaskService.cs:3706-3803`); its D-3 Blocked arm uses the existing row producer (`:4610-4634`). Dispose scope, flush parent, match complete UserPrompt, flush again and retain count 1. The upstream Git-conflict trigger is outside this test | CP-24; 3 min; PC-49 |

CP-24's total is 12 minutes: V-G1 2, V-G2 3, V-G3 4, V-G5 3, including per-method host setup and CP-4 build reuse.
Its roster grows 1 → 4; checkpoint rows stay 24; receipt methods grow 1 → 4; PCs grow 46 → 49. Ordinary estimates
grow 151 → 155 minutes; red-first stays 25. S-A authoring grows 90 → 110 minutes (the additional fixture work is
kept in its existing two new test files); other slice estimates and OD-1–OD-7 are unchanged. V-G1's two-flush
outcome and V-F2b's Off-then-All seeded-floor setup remain binding. The instructions version-before-enqueue loss
window remains disclosed in the amendment (`OrchestratorInstructionsService.cs:156-157`, `:268-278`).

Documentation validation is a changed-row consistency sweep and the existing checkpoint binary's `import` verb.
Import acceptance establishes table syntax/selection metadata; Code still owes four discovered, executed receipt
identities with nonzero counts, and SourceLanding Mutation owes the method-scoped PC runs. Next stage: Review.
