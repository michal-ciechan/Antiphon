# CARD-0097 plan (rework) — the Role × complexity matrix as the single routing surface

Task `c491a627` (Plan, Frontier) wrote this plan at base `054795510c1249ffbf65c552ac4e6d3e87defb99`; task `785ac731`
(Plan, Frontier) repaired it for Final Review `2f218eeb` (F01–F12); task `9e5cdef3` (Plan, Frontier) **reduced** it on
2026-10-14 for Final Review `f39ef633` (R2-01–R2-11), continuing from `6356c5d6851f97c1fcbadc1a0abcbe007abe333d` on
branch `feat/card-task-9e5cdef3` (landing owner stays `c491a627`). Round 2 cut the mechanisms round 1 had grown
(ledgered migration, restore of pin bodies, notice reordering, a preview slice) and kept the smallest design that
holds the safety properties; the per-finding disposition is §10.

`origin/master` at this repair is `d317c3f593112e8da01a046694db2e5671594969`. CARD-1234 (`ba40230d9`, `d317c3f59`) and
CARD-1213 (`6d49673fa`) are on it, so the collision waits of the previous rounds are over; the file:line citations
below were re-read from that master (`git show origin/master:<path>`), not from this branch. Live reads at repair time
(~08:35Z, server2 mirror, injected API base): `GET /api/version` `893ddfcee` (capabilities `land-v2`,
`operator-shutdown-v1`); `GET /api/runner-defaults` revision 2, `globalRunnerId` server2, no kind defaults;
`GET /api/session-runners` desktop (windows, capacity 2) and server2 (linux, capacity 10, 4 occupied), both
dispatch-eligible. The pin and cell census (13 stage-wide Human Required pins, no card pins, three empty any-role rows)
was re-confirmed by Final Review `f39ef633` and is not re-derived here. Lane: default placement (server2); no
`-Runner`, no `-Platform`; the checkpoints are TUnit or Vitest and run on Linux.

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
  otherwise each producer keeps its own existing code (R2-01, R2-02).** `ResolveServerChildRoutingAsync(AgentTaskRole
  role, Guid? cardId, Func<ChildRouting> legacy, CancellationToken ct)` returns `ChildRouting(AgentKind Kind,
  AgentModelLevel Level, TaskComplexity? Complexity, Guid? RoutingPinId, string? BlockedReason, string? Warning)` and
  is called by `CreateCommitTaskAsync` and `CreateMergeTaskAsync`, each passing its own `legacy` delegate. Steps, in
  this order:
  1. **Row first.** `LoadChainAsync(role, DefaultComplexity)` inside a `try`; a throw returns `legacy()` with
     `Warning = "Routing read failed ({type}); using today's path."`; `ResolvedFrom == "none"` returns `legacy()`
     with no warning. Nothing about a pin has been read at this point, so a chain-read failure has no pin to discard.
  2. **Pin second.** `_routingPins.ResolveAsync(cardId, role, Ask(null, null, null, false))`; a throw returns `legacy()`
     (Commit's legacy code re-reads the pin inside its own `try`/`catch`, `:3841-3868`, so its existing
     `"Routing pin service failed"` warning applies; Merge's legacy code reads no pin).
  3. **A Required pin with one or more candidates is the exception and returns `legacy()`** — the pin keeps today's
     producer behaviour exactly: Commit takes the head of `Compose(pin, chain: null, …)` (`:3848-3858`), `RoutingPinId`
     when that list was walked (`:3861-3862`), `Complexity` null; Merge keeps `ResolveLevel(Worker, Merge, null)` with
     no pin consulted (`:3770`), `Complexity` null, `RoutingPinId` null. The matrix UI shows the pin above the cell
     (S-B/S-C1); for Merge that display names a pin the producer does not read, as today, until the operator clears
     the pins (§6.3, §6.4).
  4. **Otherwise walk the row.** `WalkAsync(DefaultComplexity, Worker, role, pin, cardId, null, false, ct)` inside a
     `try`; a throw returns the pin-or-policy pair — the head of `Compose(pin, chain: null, …)` when non-empty, else
     `ResolveRoutingPair(Worker, role, null, null)` — with `Complexity` null, `RoutingPinId` when that list was walked,
     `Warning = "Routing walk failed ({type}); using the pin or role policy."`. `Chosen` → that pair, `Complexity =
     DefaultComplexity`, `RoutingPinId = pin.Pin.Id` when `walk.Walked && pin.Applied`, `Warning = walk.SkippedWarning()`
     when non-empty. No `Chosen` → `BlockedReason = walk.ExhaustedSentence(pin) + " A human must choose; do not pick a
     kind yourself."`, `Complexity = DefaultComplexity`: the caller constructs the child with `Status = Blocked`,
     `FailureReason = BlockedReason`, adds a `Blocked` event and calls `EnqueueBlockedParentNoteAsync(task, reason)`
     (`:4610-4634`; the child's `ParentSessionId` is the settled/conflicted task's). A resolved row with no survivor is
     not replaced by the RolePolicy pair (V-A13).
  Consequences: with an empty matrix both producers are byte-for-byte today's code paths (V-A8b, V-A12b bind the
  live-shaped pin list, a single pin, a card pin and a configured policy kind); a Required pin is kept in each failure
  arm because the pin is read after the row and a walk failure falls to the pin head (V-A15); exhaustion Blocks only
  when a row resolved and the walk found no survivor (V-A13). Rejected: a helper that stamps under a Required pin
  (round 1's contradiction between a stamped `Chosen` arm and an unstamped pin pair); a `Blocked` outcome on any
  read failure; substituting the RolePolicy pair for a known Required pin.
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
  reason verbatim. `ComplexityChainService` gains an optional `RoutingPinService? pins` constructor parameter (DI
  resolves it; the three test files that construct the service directly keep compiling). No cycle: `RoutingPinService`
  depends on `ComplexityRoutingService` only (`RoutingPinService.cs:34-46`). Rejected: calling
  `ResolveAgainstRolePolicy` for the fallback (it fabricates an alias for Raw/999); a nullable tuple (the reason is lost).
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
  - **`-Restore -Snapshot <file>`.** For each `cellsBefore[]` entry: `present` → `PUT` the saved body (`candidates`,
    `provenance`, `reason`, `notAfter`); `absent` → `DELETE /api/complexity-chains/{role}/{complexity}` (204 when already
    clear, `ComplexityChainService.cs:239-252`). The first non-2xx stops the run and prints the restored list, exit 1.
    Rerunnable. Pins are not touched: this script clears none.
  - **Clearing pins is a separate, explicit operator command**: `scripts/routing-pin.ps1 clear -Role <R>` per role
    (`:273-291`), run only after the operator has compared `complexity-chain.ps1 get -Role <R>` (the S-D `effective:`
    line) before and after the apply (§6.3). No wrapper is added.
  Deleted from round 1: the ledger, `diffSha256`, drift refusals, the non-empty-target refusal, `-Resume`, `-ClearPins`,
  pin re-`PUT` on restore, and the S-B-active precondition. Rejected: migrating in `Program.cs` on start.
- **D-10 The instructions file gets a bounded matrix section before the pins section, and cell writes fire the
  existing signal (R2-08, R2-09).** Section 5 `## 5. Role × complexity matrix` (`Owner: GET /api/complexity-chains`;
  `default <tier>`), one line per routable role: `- Plan: Hard a, b; Medium (default) c; Easy unset; effective c via
  stage pin`. Bounds: at most three aliases per tier then `+N`; a line longer than 160 bytes is cut at 157 and ends
  with `…`; 14 lines. The section enters the truncation ladder as its own step between card pins and holds: full
  rows, else the section body is the single line `… 14 rows: GET /api/complexity-chains` (rendered in place under the
  heading, not among the trailing cut notices, which are unchanged). Pins → 6, levels → 7, standing → 8; the literal
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
| **S-A Server: the role row governs ordinary dispatch and the two server-created children** | `MatrixDefaultApplies` (D-1) and the stamp before `:917`; Created event detail `"{Kind}/{Role} at {level} (matrix default {tier}) in …"` when stamped (`:1556-1560`); OD-2 warning `explicit kind/level: the {Role}/{tier} row was not consulted` when a row resolves and the request is explicit; `ResolveServerChildRoutingAsync` (D-3) called from `CreateCommitTaskAsync` (`:3836-3869` becomes its `legacy` delegate) and `CreateMergeTaskAsync` (`:3770` becomes its `legacy` delegate; the task gains `AgentKind`, `Complexity`, `RoutingPinId` from the result and a Warning event when the result carries one), including the Blocked construction and parent note. Scaffold commit first (CP-3): property-free predicate returning `false`, helper returning `legacy()`. | `server/Application/Services/AgentTaskService.cs` (`:917`, `:1556-1560`, `:3706-3797`, `:3811-3935`). Tests: new `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` (V-A1–V-A8b, V-A10–V-A15; fixture from `ComplexityCreateTests.cs:466-487`, seeds from `RoutingPinServiceTests.cs:228-256`, `:289` and `CommitOnSettlePolicyTests.cs:176-184`); `ComplexityCreateTests`, `RoutingPinCreateTests`, `RoutingPinCandidateCreateTests`, `ComplexityDispatcherTests`, `CommitOnSettlePolicyTests` run unchanged (R-A1–R-A5). Needs S-0. | 90 min |
| **S-B Server: effective cell DTO carries the overlay** | `ComplexityEffectiveDto { source, candidates, stagePin, governsDefault, withheldReason }` on `ComplexityChainDto.Effective` (nullable, `?role=` view only); `ComplexityChainListDto.DefaultComplexity`. `ComplexityChainService` takes `RoutingPinService? pins = null`; `GetEffectiveAsync` composes per D-4 through the pure static `ComplexityEffective.Compose(decision, chain, chainLabel, chainResolvedFrom, policy, isDefaultTier)`. `RolePolicyReadService.PolicyTarget` and the `PolicyResolution` record extracted (behaviour-preserving for `Row`). | `server/Application/Dtos/ComplexityChainDtos.cs` (`:26-46`), `server/Application/Services/ComplexityChainService.cs` (`:28-40`, `:111-134`), new `server/Application/Services/ComplexityEffective.cs`, `server/Application/Services/RolePolicyReadService.cs` (`:51-69`, new static and record). Tests: new `ComplexityEffectiveComposeTests.cs` (V-B1–V-B4, V-B10), new `ComplexityEffectiveHttpTests.cs` (factory `ComplexityChainApiWebAppFactory`, `ComplexityChainHttpTests.cs:142`; V-B5, V-B6); `ComplexityChainRoleHttpTests`, `ComplexityChainServiceTests`, `RolePolicyReadServiceTests` unchanged (R-B1–R-B3). Needs S-0; parallel with S-A (no shared files). | 70 min |
| **S-C1 Client: cells show the overlay** | `ComplexityChainDto.effective?`, `ComplexityChainListDto.defaultComplexity?` types. `MatrixCell` renders the effective list first with a source badge (`Required Human pin → Codex/Frontier (gpt-6-astra)` red; `Falls back to RolePolicy → ClaudeCode/High (opus)` grey; `Falls back to RolePolicy → not launchable: <withheldReason>` red; `Own rule`/`Inherits Any role`/`Configuration fallback` as today) and the stored cell beneath it; the default-tier column header reads `Medium (default for an ordinary Worker dispatch without -Complexity)`; an unset non-default cell reads `Unset — a -Complexity <tier> dispatch blocks`. `NON_COMPLEXITY_BOUNDARY` and `pinEffectCopy` rewritten; `cardPinCountLabel(role, pins)` (OD-5). msw default handler gains `defaultComplexity: 'Medium'`. | `client/src/api/complexityChains.ts` (`:19-38`), `client/src/features/settings/routingSettingsModel.ts` (`:25`, `:39-46`, `:113`), `client/src/features/settings/RoutingSettingsTab.tsx` (`:409-531`), `client/src/test/mocks/handlers.ts` (`:76-82`). Tests: `routingSettingsModel.test.ts` (V-C1–V-C3), `RoutingSettingsTab.test.tsx` (V-C4–V-C6; the three cases at `:347`, `:373`, `:447` rewritten to the new copy with that reason), `complexityChains.test.tsx` (R-C1). Needs S-B. | 60 min |
| **S-D Script: one verb for a role-wide change** | D-8 in `complexity-chain.ps1` (`-Complexity` accepts `All`; `-ClearStagePin` switch with its three refusals; `get -Role X` prints `effective:`). `routing-pin.ps1` header points role-wide changes at this script. ASCII-only. No docs in this slice (S-F owns them). | `scripts/complexity-chain.ps1` (`:9-13`, `:30-34`, `:155-183`, `:186-237`), `scripts/routing-pin.ps1` (`:1-28` header). Tests: new `tests/Antiphon.Tests/Scripts/ScriptedApi.cs` (F-C: ordered `Requests`, per-path `Respond`, in-memory `Cells`/`Pins` store) and V-D1–V-D7 in `ComplexityChainScriptTests.cs`; its nine existing methods run unchanged (R-D). Needs S-B for `get`. | 60 min |
| **S-E Migration script (operator-run, additive, snapshot + restore)** | D-9: new `scripts/migrate-stage-pins-to-cells.ps1`. ASCII-only. Not run against the live server by Code; the operator runs it after §7. | new `scripts/migrate-stage-pins-to-cells.ps1`. Tests: new `tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs` (`ScriptedApi` from S-D; V-E1–V-E6). Needs S-D (the stub). | 60 min |
| **S-F Instructions and docs** | Snapshot: `MatrixSection(DefaultComplexity, Rows)` with one `MatrixInstructionRow(Role, Hard, Medium, Easy, Effective, Source)` per routable role from `ComplexityChainService.ListAsync(role)` plus `GetEffectiveAsync`; renderer section and ladder step per D-10; `DiffMatrix`; `ComplexityChainService` fires the signal (D-10); `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside `ModelAvailability` (`:173`). Docs: `docs/orchestration-loop.md:887-921` ("Routing pins beat RolePolicy" becomes "The matrix governs an ordinary Worker dispatch; a pin is the exception"), `:943` (the row saying the section has no editor stays true unless S-C2 lands; it then says the matrix row editor can clear a stage pin), `:952-957` (default tier; the preview limitation of D-6 beside the Role policy section and the delegate modal rows), the S-E runbook paragraph (§6.3–6.5 verbatim), `docs/antiphon-api.md:705-715` (`effective`, `defaultComplexity`, `withheldReason`, last-writer-wins) and `:666-679` (the D-6 limitation sentence on `/api/role-policy`), `docs/ops-http.md:139` (the same sentence), `.claude/skills/antiphon-delegate/SKILL.md:147` (omitted `-Complexity` on an ordinary Worker dispatch means the default tier). | `server/Application/Services/OrchestratorInstructionsSnapshot.cs`, `OrchestratorInstructionsSnapshotBuilder.cs` (`:30-40`, new `ReadMatrixAsync` beside `:203`), `OrchestratorInstructionsRenderer.cs` (`:43-87`, `:149-182`), `OrchestratorInstructionsDelta.cs` (`:20-27`, new `DiffMatrix`), `ComplexityChainService.cs` (`:28-40`, `:228-233`, `:248-252`), `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (`:173`), the docs above. Tests: `OrchestratorInstructionsRendererTests` (V-F1 literal renumbering in `:33-63` and `Sample()`; V-F7 bound and ladder), `OrchestratorInstructionsRefreshTests` (V-F2 `[Arguments("complexity-chain")]` and a `CoveredWriteAsync` arm at `:351-395`), `OrchestratorInstructionsDeltaTests` (V-F3). Needs S-A, S-B, S-D, S-E. | 75 min |
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
- Server-created Merge child with a row and no Required pin → the row candidate, stamped; exhausted → Blocked (V-A12a,
  V-A13 shape). Empty matrix, with the live-shaped Merge pin list, a card pin, or `RolePolicy["Merge"].Kind = Codex`,
  or a row beside a Required Merge pin → today's `ClaudeCode/High`, `Complexity` null, `RoutingPinId` null (V-A12b).
- Chain read throws with a Required Commit pin present → the pin pair, Queued, `Complexity` null, a Warning event
  `Routing read failed` (V-A15).
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
  sends the same PUTs and leaves the store unchanged; restore re-PUTs a present before-image and DELETEs an absent one,
  sends no pin request (V-E1–V-E6).
- Instructions: a cell write regenerates the file and notifies through the existing path (V-F2); an unchanged matrix
  produces no delta line and a changed cell names the role (V-F3); the matrix section renders 14 rows with the per-line
  cap and `+N` elision at the default cap and collapses to the in-section pointer line when the cap is one byte short
  of the full render (V-F7).

## 5. Verification design

Full V/R/PC binding, fixtures and the wire contract are in
[the amendment](2026-10-14-card-0097-matrix-test-design.md). IDs are stable across both files.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S-0 tests committed, validator line not yet | `tests/Antiphon.Tests -> bin-c097m-0red/` | setting-red | `/*/*/MatrixDefaultComplexitySettingsTests/*` | V-A9 | 2 methods run; `An_undefined_default_complexity_fails_validation` fails (intentional red), `The_default_instance_validates` passes | 2 | 6 | false |
| CP-2 | S-0 | `tests/Antiphon.Tests -> bin-c097m-0/` | setting | `/*/*/MatrixDefaultComplexitySettingsTests/*` | V-A9 | 2 methods, 0 failed/skipped | 2 | 6 | false |
| CP-3 | S-A tests and scaffold committed, stamping not yet | `tests/Antiphon.Tests -> bin-c097m-ared/` | matrix-default-red | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A8b, V-A10–V-A15 | the class compiles against the scaffold and the 17 methods run (31 results: V-A6 8 rows, V-A8b 4, V-A12b 5); the rows the amendment lists as red fail by the named assertion, none by build or fixture error | 17 | 12 | false |
| CP-4 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` | matrix-default | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A8b, V-A10–V-A15 | 17 methods (31 results), 0 failed/skipped | 17 | 12 | false |
| CP-5 | S-A | CP-4 | matrix-default-regression | `/*/*/ComplexityCreateTests/*` | R-A1 | the class roster, 0 failed/skipped (count by the command below) | 14 | 8 | false |
| CP-6 | S-A | CP-4 | pin-create-regression | `/*/*/RoutingPinCreateTests/*` | R-A2 | the class roster, 0 failed/skipped | 13 | 8 | false |
| CP-7 | S-A | CP-4 | pin-list-create-regression | `/*/*/RoutingPinCandidateCreateTests/*` | R-A3 | the class roster, 0 failed/skipped | 17 | 8 | false |
| CP-8 | S-A | CP-4 | dispatcher-regression | `/*/*/ComplexityDispatcherTests/*` | R-A4 | the class roster, 0 failed/skipped | 9 | 8 | false |
| CP-9 | S-A | CP-4 | commit-on-settle-regression | `/*/*/CommitOnSettlePolicyTests/*` | R-A5 | the class roster (8 methods, 6 `[Arguments]` rows), 0 failed/skipped | 8 | 6 | false |
| CP-10 | S-B tests committed, production not yet | `tests/Antiphon.Tests -> bin-c097m-bred/` | effective-red | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4, V-B10 | compile-only stub of `ComplexityEffective.Compose` returning an empty list and `PolicyTarget` returning a resolution with null fields; 5 methods (7 results) run, each fails | 5 | 6 | false |
| CP-11 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` | effective-compose | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4, V-B10 | 5 methods (7 results), 0 failed/skipped | 5 | 6 | false |
| CP-12 | S-B | CP-11 | effective-http | `/*/*/ComplexityEffectiveHttpTests/*` | V-B5, V-B6 | 2 methods, 0 failed/skipped | 2 | 6 | false |
| CP-13 | S-B | CP-11 | chain-http-regression | `/*/*/ComplexityChainRoleHttpTests/*` | R-B1 | the class roster, 0 failed/skipped | 3 | 5 | false |
| CP-14 | S-B | CP-11 | chain-service-regression | `/*/*/ComplexityChainServiceTests/*` | R-B2 | the class roster, 0 failed/skipped | 10 | 5 | false |
| CP-15 | S-B | CP-11 | role-policy-read-regression | `/*/*/RolePolicyReadServiceTests/*` | R-B3 | the class roster and its `MethodDataSource` rows (15 cases), 0 failed/skipped | 7 | 5 | false |
| CP-16 | S-C1 | n/a | client-overlay | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-C1–V-C6, R-C1 | 3 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 5 | false |
| CP-17 | S-D | `tests/Antiphon.Tests -> bin-c097m-d/` | script-row-verb | `/*/*/ComplexityChainScriptTests/*` | V-D1–V-D7, R-D | 16 methods (9 existing + 7 new), 0 failed/skipped | 16 | 8 | false |
| CP-18 | S-E | `tests/Antiphon.Tests -> bin-c097m-e/` | migration-script | `/*/*/MigrateStagePinsScriptTests/*` | V-E1–V-E6 | 6 methods (7 results: V-E4 2 rows), 0 failed/skipped | 6 | 8 | false |
| CP-19 | S-F | `tests/Antiphon.Tests -> bin-c097m-f/` | instructions-render | `/*/*/OrchestratorInstructionsRendererTests/*` | V-F1, V-F7 | the class roster (7 methods, 3 `[Arguments]` rows), 0 failed/skipped | 7 | 6 | false |
| CP-20 | S-F | CP-19 | instructions-refresh | `/*/*/OrchestratorInstructionsRefreshTests/*` | V-F2 | the class roster (`Covered_writes_each_regenerate_and_notify` carries 7 rows after V-F2), 0 failed/skipped | 11 | 12 | true |
| CP-21 | S-F | CP-19 | instructions-delta | `/*/*/OrchestratorInstructionsDeltaTests/*` | V-F3 | the class roster, 0 failed/skipped | 5 | 4 | false |
| CP-22 | S-C2 | n/a | client-row-editor | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ComplexityChainRowEditor.test.tsx src/api/routingPins.test.ts src/features/settings/routingSettingsModel.test.ts src/features/settings/ComplexityChainCellEditor.test.tsx` | V-C7–V-C13, R-C2 | 4 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 6 | false |
| CP-23 | all | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | lint gate (`client/src/lint.gate.test.ts`), whole client suite | `CLIENT TESTS EXIT CODE: 0` and the aggregate line reports a nonzero passed count that Code records | n/a | 10 | true |

Lane: each row runs on the default placement (runner-defaults revision 2 = server2); no `-Runner`, no `-Platform`.
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
| PC-8 | `MatrixDefaultComplexityCreateTests.The_settlement_commit_child_takes_the_commit_row` (V-A8a) | Commit/Medium `[Codex/Frontier]` → return `legacy()` unconditionally → `ClaudeCode` |
| PC-9 | `MatrixDefaultComplexitySettingsTests.An_undefined_default_complexity_fails_validation` | `(TaskComplexity)999` → drop the validator line → `Succeeded` |
| PC-10 | `MatrixDefaultComplexityCreateTests.A_required_pin_with_candidates_keeps_the_commit_child_on_todays_pin_path` (V-A8b, row `row-and-single-pin`) | row + Required single pin `Codex/Frontier` → walk the row despite the pin → `ClaudeCode/High` from the row, `Complexity Medium` |
| PC-11 | `MatrixDefaultComplexityCreateTests.A_stage_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` (V-A10) | stage agent-only pin + Codex cell → evaluate the predicate before the pin overlay → `AgentKind Codex` ≠ the agent's kind |
| PC-12 | `MatrixDefaultComplexityCreateTests.The_server_created_merge_child_takes_the_merge_row` (V-A12a) | Merge/Medium `[Codex/Frontier]` → keep `ResolveLevel` only → `ClaudeCode/High` |
| PC-13 | `MatrixDefaultComplexityCreateTests.An_empty_matrix_or_a_required_pin_keeps_the_merge_child_on_todays_program_default` (V-A12b, row `stage-list-pin`) | no row + Merge pin `[ClaudeCode/Frontier, ClaudeCode/High]` → resolve the pin in the legacy arm → `Frontier` ≠ `High` |
| PC-14 | `MatrixDefaultComplexityCreateTests.An_exhausted_commit_row_blocks_the_commit_child` (V-A13) | each candidate held → fall back to the RolePolicy pair → `Status Queued` |
| PC-15 | `MatrixDefaultComplexityCreateTests.A_held_commit_row_head_falls_to_the_next_candidate_with_the_skipped_warning` (V-A14) | head held → take `Candidates[0]` without the walk → `ClaudeCode` |
| PC-16 | `MatrixDefaultComplexityCreateTests.A_chain_read_failure_keeps_the_required_commit_pin` (V-A15) | routing service over a disposed context + Required pin `Codex/Frontier` → catch into the RolePolicy pair instead of `legacy()` → `ClaudeCode/Medium` |
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
| PC-31 | `MigrateStagePinsScriptTests.Restore_re_puts_present_before_images_and_deletes_absent_ones` (V-E6) | prior Human Plan/Medium cell → DELETE instead of PUT the before body → DELETE where a PUT was expected |
| PC-32 | `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (V-F1) | matrix section → omit it → `## 5. Role × complexity matrix` missing |
| PC-33 | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify("complexity-chain")` (V-F2) | cell upsert → drop the signal → `SubmittedBodies.Count == 0` |
| PC-34 | `OrchestratorInstructionsDeltaTests.An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` (V-F3) | byte-equal rewrite → `DiffMatrix` reports on equal input → a line appears |
| PC-35 | `OrchestratorInstructionsRendererTests.The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` (V-F7) | 42 cells × 8 candidates → drop the per-line cap → a `- <Role>:` line longer than 160 bytes |
| PC-36 | `ComplexityChainRowEditor.test.tsx` V-C7 (optional S-C2) | Save → PUT only Hard → two calls missing |
| PC-37 | `ComplexityChainRowEditor.test.tsx` V-C9 (optional S-C2) | pin present, box on → DELETE before the PUTs → order red |
| PC-38 | `routingPins.test.ts` V-C12 (optional S-C2) | DELETE → invalidate pins only → no second complexity-chains GET |

### Cost

Ordinary green rows (CP-2, CP-4–CP-9, CP-11–CP-23): 6+12+8+8+8+8+6+6+6+5+5+5+5+8+8+6+12+4+6+10 = 142 minutes.
Red-first rows (CP-1, CP-3, CP-10): 6+12+6 = 24 minutes. Authoring: S-0 20, S-A 90, S-B 70, S-C1 60, S-D 60, S-E 60,
S-F 75, S-C2 90 (optional) minutes. No production timeout or assertion is loosened by this plan; the three rewritten
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
5. **Rollback.** `-Restore -Snapshot <first snapshot>` re-PUTs each present before-image and DELETEs each absent
   one; cells written by the row editor after that snapshot are overwritten or deleted by it, which is why the
   runbook says to restore from the first snapshot and before any editor use. Pins the operator cleared are
   re-created by hand with `routing-pin.ps1 set -Role <R> -Provenance Human -Strength Required -Candidates <list>
   -Reason <text>` from the §6.3 table; with a pin back, a Required pin bypasses the row as it bypasses a
   `-Complexity` cell today.

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
  the legacy path on an empty matrix, a read failure or a Required pin (V-A8b, V-A12b, V-A15; PC-10, PC-13, PC-16).
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
- **Instructions byte budget** (D-10): the matrix is bounded per line and elided in place to a pointer under pressure;
  V-F7 binds the per-line cap and the ladder step at a derived cap, not a universal byte figure (R2-10).
- **Grok hold** until 2026-10-16 05:00Z: a cell naming Grok shows `held` and is skipped; harmless.
- **msw `onUnhandledRequest: 'error'`**: S-C2's DELETE needs a handler in each test that triggers it; a missing one is a
  red CP-22, not a silent gap.

## 9. Sessions that wait for input

This plan adds no session that waits for input and changes no release deadline; the CARD-1083 checklist does not
apply. S-E is an operator-run script, not a session. With parking disabled, an input-waiting session has no automatic
release deadline; nothing here changes that.

## 10. Round-2 disposition (Final Review `f39ef633`)

| Finding | Disposition |
|---|---|
| R2-01 Merge not an empty-matrix no-op; child single-pin contradiction | **Fixed by reduction.** D-3 now reads the row first and returns each producer's own legacy delegate on `none` or on a Required pin with candidates; Merge's legacy is `ResolveLevel(Worker, Merge, null)` with no pin read. One child rule: a Required pin with candidates keeps today's path (Commit: list head with `RoutingPinId`, single pair without; Merge: RolePolicy High). V-A8b (4 rows), V-A12b (5 rows), PC-10, PC-13. |
| R2-02 chain-read failure discards a Required pin | **Fixed by reduction.** The chain is read before the pin; a chain-read failure returns the legacy delegate (which reads the pin itself); a walk failure falls to the pin head. No `Blocked` on any read failure. V-A15 seeds a Required pin and asserts it is kept; PC-16. |
| R2-03 V-B10 diagnostic unreachable from a nullable tuple | **Fixed.** `PolicyTarget` returns `PolicyResolution` with the usable pair or the literal rejected values in `WithheldReason`; V-B10 binds Raw/Frontier, OpenCode/Medium and ClaudeCode/999 with independent literal reasons; PC-21. |
| R2-04 preview disagrees with dispatch | **Deleted with the mechanism removed.** S-B2, D-4b, V-B7–V-B9, V-C15–V-C17, CP-16–CP-18 (old numbering) and PC-32 (old) are gone; D-6 states the divergence as a limitation, S-F documents it beside both consumers, and CARD-1250 owns the rework. |
| R2-05 migration state machine contradicts its tests | **Deleted with the mechanism removed.** No hash, no ledger, no drift refusal, no non-empty refusal, no `-Resume`; apply is a full idempotent overwrite behind a snapshot (D-9); V-E1–V-E6 bind one contract. |
| R2-06 restore replays a GET pin body the PUT rejects | **Deleted with the mechanism removed.** The script writes no pin body; pins are cleared by the existing `routing-pin.ps1 clear` and re-created by hand from the §6.3 table; restore re-PUTs cells in the shape `ComplexityChainDtos.cs:6-12` accepts (V-E6). |
| R2-07 ledger ownership and recovery | **Deleted with the mechanism removed.** No ledger; a crash is rerun; the first snapshot is the restore source; the runbook orders editor use after the restore window (§6.5). |
| R2-08 notice save reorder lets a stale pending notice deliver | **Deleted with the mechanism removed.** Round 1's D-12 is gone; the notice path is unchanged (D-10); a cell write only fires the existing signal (V-F2). |
| R2-09 crash recovery and child-delivery verification missing | **Deleted with the mechanism removed.** No delivery flow is added; the Blocked note reuses `EnqueueBlockedParentNoteAsync` and the existing flushes; V-A3/V-A13 bind the row, V-A16 and PC-39/PC-40 (old) are withdrawn; the amendment's delivery inventory names the existing receipt tests. |
| R2-10 4,096-byte test impossible | **Fixed.** V-F7 drops the universal figure: it binds the per-line cap and `+N` elision at the default cap, and the in-place pointer at a cap derived from the full render (one byte short); PC-35. |
| R2-11 PC-34 binding ineffective | **Fixed.** The DELETE-after-failure control is bound to V-D5 `ClearStagePin_is_skipped_when_a_tier_failed` (PC-28). |

Net change: slices 9 → 8 (S-B2 deleted; S-C2 marked optional); checkpoint rows 26 → 23; positive controls 40 → 38
(38 includes the three optional S-C2 controls); V rows: S-A 16 → 17 methods, S-B 7 → 7, S-B2 3 → 0, S-C 17 → 13,
S-D 7 → 7, S-E 11 → 6, S-F 8 → 4. OD-1–OD-7 are unchanged.
