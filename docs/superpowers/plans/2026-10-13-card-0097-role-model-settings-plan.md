# CARD-0097 plan — per-role model/tier settings: what exists, what remains

Task `9d94f1e5` (Plan, Frontier); base `b5653d5832aac66c5ac1ff1a0012efb33b54de79`; branch `feat/card-task-9d94f1e5`.
Code read at that base on 2026-10-13. Live reads at 18:45Z: `GET /api/routing-pins`, `GET /api/runner-defaults`
(revision 2, global `server2`, Human), `GET /api/session-runners` (desktop windows 2 seats; server2 linux 10 seats;
server2-temp stale/draining), `GET /api/orchestrator-instructions` (`vf1b22da9 rev 133`). Post-land Mutation was
not run for any landed card named here. Lane: default placement (runner-defaults revision 2 puts everything on
server2); no `-Runner`, no `-Platform` — every checkpoint below is Vitest or TUnit API and runs on Linux.

## 0. Verdict

**(b) Partly satisfied.** The card asked for two things: *view* which model/tier each role uses, and *control* it
without a code edit and restart.

- **View** is satisfied for agents and half-satisfied for operators. Since CARD-0822 (2026-10-10) the live
  instructions file renders every role's Level, EscalateTo, Kind and in-flight cap as a table, and lists pins and
  the kind × level → alias map (§1.1 row 2). The operator UI (`/settings?tab=routing`, CARD-0333, 2026-09-05)
  shows the live **pin** per role but nowhere shows the **RolePolicy default** a role falls back to, its
  escalation ladder, its timeout, or the resolved alias (§1.1 rows 4, 6). The one client copy of role → tier is a
  hand-maintained mirror that has already drifted: `AGENT_TASK_ROLES` says Mutation is `Frontier`
  (`client/src/api/agentTasks.ts:697`, set 2026-09-10 by CARD-0470) while the server default has been `Low` since
  CARD-0479 (`9bae13661`, 2026-10-10, server-only commit), and `DelegateModal.tsx:85-87` pre-selects that stale
  value. That drift is the card's "visibility gap" reproduced in the code.
- **Control** is satisfied at the API and script level, not in the UI. `PUT /api/routing-pins` (CARD-0305,
  2026-09-01) with `provenance: Human` changes a role's kind/level/candidates live, no restart, and the operator is
  using it today: 13 stage-wide Human Required pins cover every routable role except Custom (live read, §1.1 row 3).
  The client has no write path: `client/src/api/routingPins.ts` exports only `useRoutingPins` (GET), and the owner
  doc says the Routing tab "is read-only for pins" (`docs/orchestration-loop.md:899`) and names CARD-0097 as "the
  separate, still-open RolePolicy visibility/editing follow-up" (`:883`).

What remains is G1–G4 in §1.4: a per-role read section with the RolePolicy default beside the live pin and the
effective next-create target, an operator pin editor over the existing API, and the stale mirror fix. Editing
`RolePolicy` itself live is **not** a gap (D-7): pins are the live layer by design.

## 1. Gap analysis

### 1.1 What exists today

| # | Capability | Where | Evidence |
|---|---|---|---|
| 1 | Role → tier data model | `server/Application/Settings/DelegationSettings.cs:347-372` (`RolePolicy`, 13 roles: Investigate, Plan, TestDesign, Code, Mutation, Review, Debug, Coverage, Merge, Docs, Commit, Test, Deploy), `:374` `DefaultLevel = High`, `:1036-1130` `RolePolicyEntry` (`Level`, `EscalateTo`, `EscalateAfterMinutes` null everywhere since CARD-0158, `TimeoutMinutes = 240`, `Kind` null = ClaudeCode, `RecommendedInFlight` seed) | Bound from `appsettings` via `IOptions<DelegationSettings>`; changing it is still an edit plus restart. Custom and Check have no entry and fall back to `DefaultLevel` (`:410-417`). |
| 2 | Agent-facing read of RolePolicy | `GET /api/orchestrator-instructions` (`server/Api/Endpoints/OrchestratorInstructionsEndpoints.cs:17-33`, `text/markdown` only, ETag). Section 1 table `Role / recommended in-flight / Level / EscalateTo / Kind` built by `OrchestratorInstructionsSnapshotBuilder.ReadPipelinePolicy` (`server/Application/Services/OrchestratorInstructionsSnapshotBuilder.cs:47-110`; row type `RoleInstructionRow`, `OrchestratorInstructionsSnapshot.cs:24-29`); section 5 lists pins; section 6 lists kind × level → alias from `ModelLevelAliases.For` (`server/Application/Services/ModelLevelAliases.cs:90`). | Live file at rev 133 lists 14 roles including Custom. No JSON shape; the client has zero references to the route (`grep -rn orchestrator-instructions client/src` = 0). |
| 3 | Live per-role override (write) | Routing pins: `GET/PUT /api/routing-pins`, `DELETE /api/routing-pins/{id}` (`server/Api/Endpoints/RoutingPinEndpoints.cs:18-48`); request `PutRoutingPinRequest` (`server/Application/Dtos/RoutingPinDtos.cs:13-31`: `Role`, `Card?`, `Provenance` Human/Auto, `Strength` Required/Preferred, `AgentKind`/`ModelLevel` or 1–8 `Candidates`, `ForbiddenAliases`, `NotBefore/NotAfter`, `AgentId`, `Reason`); rules in `RoutingPinService.UpsertAsync` (`server/Application/Services/RoutingPinService.cs:163-262`: Auto over Human is 409 `routing_pin_human` `:233-236`; pool delegate as `AgentId` is 422 `:213-224`; Check/Distill/Diagnose are 422 `:578-598`); per-candidate live availability `EvaluateCandidatesAsync` `:784-800`. Script `scripts/routing-pin.ps1 get|set|clear`. | Live: 13 stage-wide pins, all Human Required, written 2026-10-13 16:47–16:59Z (Plan/TestDesign → ClaudeCode/Frontier `fable`; Code → ClaudeCode/High `opus`; Review → Codex/Frontier `gpt-6-astra`; the other nine → `ClaudeCode/Frontier, ClaudeCode/High`). No restart was needed. The PUT has no revision token (last writer wins) and no operator-token gate (no `OperatorCredential.Require` in that file; the same is true of runner-defaults, model-availability, complexity-chains and dispatch-concurrency endpoints — grep over those five files). |
| 4 | Operator UI, Routing tab | `client/src/features/settings/RoutingSettingsTab.tsx:68-84` stacks Model availability (`:86-207`, editable: hold form + Clear), Subscription usage (`:209-283`), Runner defaults (`RunnerDefaultsSection.tsx`, editable, revisioned, Human provenance `:150-154`), Role × complexity matrix (`:285-405`, editable cells via `ComplexityChainCellEditor.tsx`). Stage-wide pins appear as a banner on the matching role row (`:461-471`, copy `pinEffectCopy` at `routingSettingsModel.ts:113-119`); card pins listed (`:575-605`). Pins come from `useRoutingPins` (`client/src/api/routingPins.ts:48-54`, GET, 15 s poll). | No RolePolicy values anywhere: the tab's own boundary text says "RolePolicy still resolves work that is not launched with -Complexity … This matrix does not govern every dispatch" (`routingSettingsModel.ts:25-26`). Pin banners print `Plan → ClaudeCode/Frontier`, not the alias. `grep -rn RolePolicy client/src` = 2 hits, both prose (`routingSettingsModel.ts:26`, `agentTasks.ts:687`). |
| 5 | Operator UI, Orchestrator page | Pipeline tab stage header chip `pin: fable +2` (`client/src/features/orchestrator/pipelineStageModel.ts:143-159`) from `AgentTaskPipelineStageDto.RoutingPin` (`server/Application/Dtos/AgentTaskPipelineDtos.cs:79-92`); in-flight/queued rows carry `AgentKind`/`ModelLevel`. | Shows what is pinned and what is running; not the default. |
| 6 | Client role → tier mirror | `AGENT_TASK_ROLES` (`client/src/api/agentTasks.ts:687-706`, comment "mirroring the server's default RolePolicy") feeds the delegate modal's pre-selected level (`client/src/features/delegations/DelegateModal.tsx:85-87`). | Drifted: Mutation `Frontier` vs server `Low`. Nothing tests the mirror against the server. |
| 7 | Effective-settings route | `GET /api/agent-tasks/pipeline` (CARD-0881): `concurrencyScopes`, `hosts`, `runners`, `runnerDefaults` (`AgentTaskPipelineDtos.cs:9-44`). | Carries no role Level/Kind. `RoutingPinEndpoints.cs:11-13` records that pins were deliberately kept off this "frozen read aggregation". |
| 8 | Concurrency axis | `RecommendedInFlight` is now a seed for the CARD-0505 dispatch-concurrency store (`DelegationSettings.cs:1125-1130`); UI is CARD-0506 (S1 landed `f43fc6103`; S2 editor in review; S3 adds a Settings tab). | This card must not re-render the in-flight column. |
| 9 | Complexity axis | CARD-0090 chains and CARD-0322 pin candidate lists are Done; the matrix lives in the Routing tab. | The card's "likely the same screen" guess is confirmed; the shared home exists. |

### 1.2 What changed since the card was filed (2026-08-19)

| Date | Change | Effect on this card |
|---|---|---|
| 2026-08-23 | CARD-0158: Debug `EscalateAfterMinutes = 25` retired; auto-escalation disarmed on every role (`DelegationSettings.cs:1052-1066`) | The card's quoted Debug entry is stale; EscalateTo is a manual ladder only. |
| 2026-08-30 | CARD-0099: Codex is a delegatable kind | Three kinds to show, not two. |
| 2026-09-01 | CARD-0305: routing pins (stage/card, Human/Auto, Required/Preferred) | Answers the card's open question 2: live per-role control exists, without a restart, as a layer above RolePolicy. |
| 2026-09-02 | CARD-0022/0309 model-availability holds; CARD-0090 plan | Availability is a server verdict the UI can show per candidate. |
| 2026-09-04/05 | CARD-0333 S2–S4: Settings → Routing tab; CARD-0322 S1/S4: candidate lists on pins and the pin chip | The operator home exists; pins are visible there, read-only. |
| 2026-09-10 | CARD-0470: Mutation role; client mirror sets Mutation `Frontier` | Mirror created. |
| 2026-09-25 | CARD-0710: runner defaults editor (revisioned, Human provenance) | The write-editor pattern to copy. |
| 2026-10-10 | CARD-0479: Mutation default → `Low` (server only); CARD-0505: concurrency store; CARD-0822: live orchestrator instructions | Mirror drifted; in-flight moved to its own store; agents got the role table. |
| 2026-10-13 | Operator pins every role via the orchestrator (`routing-pin.ps1`) | Live proof that control is exercised through pins, by script. |

### 1.3 Ground truth: what the card assumes versus what the code does

| Card assumes | Code today | Evidence |
|---|---|---|
| `RolePolicy` at `DelegationSettings.cs:249-265`, 10 roles | `:347-372`, 13 roles (adds Investigate, TestDesign, Mutation), each with `RecommendedInFlight` | file read |
| Debug `EscalateTo = Frontier, EscalateAfterMinutes = 25` | `EscalateTo = Frontier`, `EscalateAfterMinutes` null (CARD-0158) | `:360`, `:1052-1066` |
| `RolePolicyEntry` at `:451-464` with Level/EscalateTo/EscalateAfterMinutes/TimeoutMinutes/Kind | `:1036-1130`, same fields plus `RecommendedInFlight` (seed after CARD-0505) | file read |
| "zero client-side surface … grep for RolePolicy, no hits" | 2 prose hits, still no rendered RolePolicy value | `grep -rn RolePolicy client/src` |
| Per-role model is C# config only, text edit plus restart to change | True for `RolePolicy`; false for the effective per-role routing, which pins change live | `RoutingPinEndpoints.cs:25-37`; live pins |
| `Kind` change is "a config edit and a restart" (CARD-0084) | True for `RolePolicy.Kind`; a pin carries a kind live | `RoutingPinDtos.cs:19`, `:31` |
| CARD-0090's UI "likely the same screen" | Confirmed: the Routing tab hosts the matrix and the pin banners | `RoutingSettingsTab.tsx:285-405` |
| CARD-0092/0094/0095/0096 are a candidate shared home | All four are Done; the Orchestrator page is tabs; Settings → Routing is the home that exists | `card.ps1 get` status lines; `OrchestratorPage.tsx:49-115` |
| Open question: does a change apply live or need a restart? | Decided by CARD-0305: "Routing pins beat RolePolicy … RolePolicy remains the provenance-less fallback when no pin exists" | `docs/orchestration-loop.md:862-866` |
| A read-only view "already answers most of what the user asked" | Agents have it (instructions table); operators see pins but not defaults, ladder, timeout or alias | §1.1 rows 2, 4 |
| The data is "just invisible" | Worse in one place: the client shows a wrong value (Mutation `Frontier` vs `Low`) | `agentTasks.ts:697` vs `DelegationSettings.cs:356` |

### 1.4 What remains

- **G1 Operator view of the default.** No client surface shows, per role, the RolePolicy `Level`, `Kind` (null =
  ClaudeCode), `EscalateTo`, `TimeoutMinutes`, or whether the role is configured at all (Custom is not). Proving
  test: V-8.
- **G2 Resolved target.** Neither the pin banner nor any client text resolves kind + level to the alias the launch
  will use (`fable`, `opus`, `gpt-6-astra`); the server has the map (`ModelLevelAliases.For`), the client has a
  second hand-written copy (`tierAlias`, `client/src/features/delegations/taskVisuals.ts:59-74`). Proving tests:
  V-1, V-5.
- **G3 Editing from the UI.** No PUT/DELETE pin client; the operator edits by script or through the orchestrator.
  Proving tests: V-11, V-14.
- **G4 Stale mirror.** `AGENT_TASK_ROLES.level` is wrong for Mutation and will drift again; the delegate modal's
  pre-selected level comes from it. Proving test: V-16.
- **Not a gap:** live editing of `RolePolicy` itself (D-7); the concurrency column (CARD-0506); card-scoped pins in
  the UI (D-5).

## 2. Decisions

- **D-1 Home: a new section inside the existing Routing tab, not a new tab.** `RolePolicySection` is inserted
  between `RunnerDefaultsSection` and `MatrixSection` (`RoutingSettingsTab.tsx:78-82`). Reasons: the doc already
  sends operators to `/settings?tab=routing` to reason about routing (`orchestration-loop.md:886`); each section
  owns its query so a failure cannot blank its siblings (`:63-67`); and CARD-0506 S3 edits `SettingsPage.tsx` and
  `SettingsPage.test.tsx` (new tab, controlled Tabs) — this plan touches neither, so the two land in any order.
  Rejected: a "Roles" tab (collides with 0506 S3 and splits routing across two tabs); the Orchestrator page
  (operational glance, not settings).
- **D-2 Read model: a new JSON route `GET /api/role-policy`.** One row per `ComplexityRoutingService.RoutableRoles`
  entry (`server/Application/Services/ComplexityRoutingService.cs:61-76`: 14 roles, Custom last) carrying the
  RolePolicy fields, `configured`, the alias from `ModelLevelAliases.For`, the active stage-wide pin (the
  `RoutingPinDto` with evaluated candidates) and `effective { source: pin|rolePolicy, agentKind, modelLevel, alias }`
  for the next create. Composition is a pure static method over `DelegationSettings` plus the pin list so it is
  unit-testable without HTTP (V-4). Rejected: parsing the markdown instructions in the client (a text contract
  with an ETag, versioned for agents, not a data API); hanging the rows off `/api/agent-tasks/pipeline`
  (`RoutingPinEndpoints.cs:11-13` calls it a frozen read aggregation, it is polled by the pipeline glance, and the
  Routing tab does not read it); exposing `DelegationSettings` wholesale (it holds operational knobs that are not
  this card's).
- **D-3 Columns.** Role · Default (`ClaudeCode/Low (haiku)`; "not configured, uses DefaultLevel" for Custom) ·
  Escalate to (manual ladder; "—" when null; never shown as a timer because `EscalateAfterMinutes` is null on every
  role) · Timeout (minutes) · Stage pin (strength, provenance, ordered candidates with the server's availability
  verdict, reason) · Effective next create (pin candidate 1 or the default, with its alias). **Excluded:** the
  in-flight cap (CARD-0506 owns it); `EscalateAfterMinutes` (dead by default; shown only if non-null).
- **D-4 Read first, then edit, both over the existing API.** S1 + S2 ship the view; S3 adds the editor; S4 fixes
  the mirror; S5 docs. The editor writes `PUT /api/routing-pins` with `provenance: "Human"` fixed and
  `DELETE /api/routing-pins/{id}`; no new server write path. Reasons: the API already enforces the rules the card
  cares about (Human outranks Auto, specialist roles refused, pool agents refused, candidate validation), the
  operator already exercises exactly this write by script, and a Human-provenance UI write is what the runner
  defaults editor does today (`RunnerDefaultsSection.tsx:154`). Rejected: a UI that edits `RolePolicy` in the DB
  (D-7).
- **D-5 Editor scope.** Stage-wide pins only; card pins stay with `routing-pin.ps1 -Card` and `delegate.ps1 -Pin`
  (`docs/antiphon-api.md:629-631`) and remain listed read-only. Fields: ordered candidates (1–8, reuse
  `candidateListError`/`nextUnusedCandidate`, `routingSettingsModel.ts:238-269`), Strength (default **Preferred**,
  the script's default; Required needs a confirm that restates "bypasses the complexity cell for this role and
  refuses any create that asks for a different kind or level with 409 `routing_pin_conflict`"), Reason (required,
  like every other editor). `forbiddenAliases`, `notBefore`, `notAfter` and `agentId` are **carried through
  unchanged** from the loaded pin because the PUT replaces the whole grain (`RoutingPinDtos.cs:5-8`); they are shown
  read-only with "edit with routing-pin.ps1". Clear shows the RolePolicy default the role returns to (G1 is what
  makes Clear safe to offer).
- **D-6 Concurrency on write.** The PUT has no revision token, so a UI Save can overwrite a script write made
  seconds earlier. Mitigation inside this card: the editor is opened from a row that was refetched at open, shows
  `updatedAt`, and both query keys are invalidated after every write (V-15). A server `expectedUpdatedAt` → 409
  `routing_pin_stale` is **out of scope** (it changes the script and the orchestrator's writes); file it as a
  follow-up card if O-3 is wanted.
- **D-7 `RolePolicy` stays config-only.** Level, Kind, EscalateTo and TimeoutMinutes are displayed, not edited.
  Reasons: pins are the designed live layer for kind/level ("Routing pins beat RolePolicy", `orchestration-loop.md:862`);
  a second live layer for the same axis gives two precedence-fighting write surfaces; EscalateTo and Timeout are
  operational knobs with their own history (`DelegationSettings.cs:1052-1105`). Rejected: a DB-backed RolePolicy
  override table modelled on CARD-0505 (that was justified because in-flight had no live layer; kind/level has one).
- **D-8 Replace the mirror with the server value.** `DelegateModal` pre-selects the level from `useRolePolicy()`:
  the pin's first candidate when a stage pin exists (so the default ask cannot 409 against a Required pin),
  otherwise the RolePolicy level; `AGENT_TASK_ROLES.level` becomes the loading/error fallback and is corrected to
  `Low` for Mutation now. Rejected: only correcting the constant (drifts again, nothing tests it).
- **D-9 No new SignalR event.** Pins have no hub event today (`client/src/hooks/useSignalRInvalidation.ts` maps
  `RunnerDefaultsChanged` and `DispatchConcurrencyChanged`, nothing for pins; `RoutingPinService` broadcasts
  nothing). The new query polls at 15 s like `useRoutingPins` and invalidates on its own writes. A
  `RoutingPinsChanged` broadcast would need the server to emit on script writes too; follow-up card, not this one.
- **D-10 Lane and tests.** Vitest and the TUnit API class run on the default lane; no E2E in this card (the
  Routing tab has no E2E today, `grep -rli routing tests/Antiphon.E2E` hits only the pipeline glance and the
  fixture). Checkpoints are the closed list in §4.

## 3. Slices

| Slice | Files (closed list) | Est. |
|---|---|---|
| **S1 Server read route** | new `server/Application/Dtos/RolePolicyDtos.cs` (`RolePolicyDto { roles, defaultLevel, defaultTimeoutMinutes }`, `RolePolicyRowDto { role, configured, level, kind, alias, escalateTo, escalateAfterMinutes, timeoutMinutes, stagePin: RoutingPinDto?, effective: RolePolicyEffectiveDto }`, `RolePolicyEffectiveDto { source, agentKind, modelLevel, alias, strength? }`); new `server/Application/Services/RolePolicyReadService.cs` (`Compose(DelegationSettings, IReadOnlyList<RoutingPinDto>)` static + `ReadAsync` calling `RoutingPinService.ListAsync(null, null, ct)` and keeping `cardId == null`); new `server/Api/Endpoints/RolePolicyEndpoints.cs` (`MapGet("/api/role-policy")`); registration beside `MapRoutingPinEndpoints` in `server/Program.cs` and DI beside `RoutingPinService`. Tests: new `tests/Antiphon.Tests/Api/RolePolicyEndpointTests.cs` (V-1–V-3, `AntiphonWebAppFactory`, `[Category("Integration")]`, committed before the endpoint for CP-1), new `tests/Antiphon.Tests/Application/RolePolicyReadServiceTests.cs` (V-4). `RoutingPinService` is not modified. | 45 min |
| **S2 Client read section** | new `client/src/api/rolePolicy.ts` (types, `rolePolicyKeys.all = ['role-policy']`, `useRolePolicy()` GET `/role-policy`, `refetchInterval: 15_000`); new `client/src/features/settings/rolePolicyModel.ts` (`defaultLabel`, `effectiveLabel`, `escalateLabel`, `timeoutLabel`, `fallbackOnClearCopy`); new `client/src/features/settings/RolePolicySection.tsx`; `client/src/features/settings/RoutingSettingsTab.tsx` (one import, one element at `:80-81`, subtitle `:74-75`); `client/src/test/mocks/handlers.ts` (one default `http.get('/api/role-policy', …)` row — required because `client/src/test/setup.ts:40` runs msw with `onUnhandledRequest: 'error'`). Tests: new `rolePolicyModel.test.ts`, new `RolePolicySection.test.tsx`, one order case in `RoutingSettingsTab.test.tsx`. | 50 min |
| **S3 Stage pin editor** | `client/src/api/routingPins.ts` (`PutRoutingPinRequest` type, `usePutRoutingPin()`, `useClearRoutingPin()`, both invalidating `routingPinKeys.all` and `rolePolicyKeys.all`); new `client/src/features/settings/StagePinEditor.tsx` (Modal; candidates; Strength radio; Reason; Required confirm; Clear confirm with fallback copy; carry-through of read-only fields); `RolePolicySection.tsx` (Configure/Clear buttons per row); `rolePolicyModel.ts` (`pinRequestFrom(draft, loadedPin)`, `requiredConfirmCopy(role)`). Tests: new `StagePinEditor.test.tsx`, new `client/src/api/routingPins.test.ts`, cases added to `rolePolicyModel.test.ts`. | 60 min |
| **S4 Delegate modal default level** | `client/src/features/delegations/DelegateModal.tsx:85-87` (level from `useRolePolicy()` per D-8; fallback to the constant); `client/src/api/agentTasks.ts:697` (Mutation `Frontier` → `Low`, comment says it is a fallback). Tests: three cases in `DelegateModal.test.tsx`. | 30 min |
| **S5 Docs** | `docs/orchestration-loop.md:883` (CARD-0097 no longer "still-open" for the view; say what the section shows), `:886-891` (add the role-policy section to the tab description), `:896-899` (pins are editable from the tab, Human provenance, card pins still script-only); `docs/antiphon-api.md` one route block beside `:623-631`; `docs/ops-http.md` one row beside `:138` (runner defaults). No tests; M-1. | 20 min |

Commit and push after each slice. Branch names for the Code tasks follow whatever dispatch mints at the time
(CARD-1213, InProgress, is changing that format); it does not affect these files.

**Collisions.** CARD-0506 (S2 in review, S3 not landed) touches `SettingsPage.tsx`, `SettingsPage.test.tsx`,
`ProjectConfig.tsx`, `ProjectConfig.test.tsx`, new `DispatchConcurrencyEditor*`, new `ConcurrencySettingsTab*`,
`docs/orchestration-loop.md:811-816`, `docs/antiphon-api.md` near `:538`, `docs/ops-http.md:177` — none of the
files in S1–S4; S5 edits different lines of the same three docs (textual merge, no semantic overlap). Shared risk:
`client/src/test/mocks/handlers.ts` is not in the 0506 file list but is the one file both cards could plausibly
add a line to; S2 adds exactly one handler at the end of the routing block (`:34`). CARD-1213 (Plan stage) is
server dispatch and worktree naming; no file overlap. In-flight Code tasks touching `client/src/features/settings`
should be checked on the pipeline route before dispatching S2/S3 (same-area defer rule, AGENTS.md).

## 4. Verification design

Every V names its test method; "case → mutation → red" is the deliberate production change that must make it fail.

**S1 (`RolePolicyEndpointTests`, `RolePolicyReadServiceTests`)**

- V-1 `Get_lists_every_routable_role_in_order_with_policy_fields`: 14 rows in `RoutableRoles` order; Mutation row
  `configured=true, level=Low, kind=null, alias=haiku, escalateTo=High, timeoutMinutes=240`; Custom row
  `configured=false, level=High` (DefaultLevel), `alias=opus`. Alias assertions compare to
  `ModelLevelAliases.For(kind ?? ClaudeCode, level)` rather than a literal, because the alias map is edited often
  (`8d2a447b7`). Case: Mutation → mutation: `Compose` reads `DefaultLevel` for every role → red (`Low` expected,
  `High` read).
- V-2 `Get_overlays_the_active_stage_pin_as_effective`: `PUT /api/routing-pins` `{role: Plan, provenance: Human,
  strength: Required, candidates: [{Codex, Frontier}], reason}` then GET → Plan `effective.source == "pin"`,
  `agentKind == Codex`, `alias == gpt-6-astra` (compared via `ModelLevelAliases.For`), `stagePin.candidateCount == 1`;
  Code `effective.source == "rolePolicy"`. Case: pinned role → mutation: `Compose` ignores pins → red.
- V-3 `Get_ignores_card_pins_and_cleared_pins`: create a board/card through the API; PUT a Code **card** pin; PUT
  then DELETE a Debug stage pin; GET → Code `stagePin == null`, `effective.source == "rolePolicy"`; Debug
  `stagePin == null`. Case: card pin present → mutation: drop the `cardId == null` filter → red (Code shows a pin).
- V-4 `Compose_resolves_alias_from_kind` (unit, no HTTP): `DelegationSettings` with `RolePolicy["Docs"].Kind = Grok`
  and no pins → Docs `alias == ModelLevelAliases.For(Grok, Medium)`; `Kind = null` → ClaudeCode alias; a role with
  `EscalateAfterMinutes = 7` surfaces the number. Case: Grok kind → mutation: `Compose` passes `ClaudeCode`
  unconditionally → red.

**S2 (`rolePolicyModel.test.ts`, `RolePolicySection.test.tsx`, `RoutingSettingsTab.test.tsx`)**

- V-5 `defaultLabel renders kind, level and alias, with ClaudeCode for a null kind`: `{kind:null, level:'Low',
  alias:'haiku'}` → `ClaudeCode/Low (haiku)`; `{configured:false}` → `not configured — DefaultLevel High (opus)`.
  Mutation: drop the null → ClaudeCode branch → red.
- V-6 `effectiveLabel prefers the pin and names strength and provenance`: source pin → `Required Human pin →
  Codex/Frontier (gpt-6-astra)`; source rolePolicy → `default`. Mutation: ignore `source` → red.
- V-7 `fallbackOnClearCopy names the RolePolicy default the role returns to`: contains the role, kind/level and
  alias from the row. Mutation: omit the alias → red.
- V-8 `renders one row per role from the DTO, not from the client mirror`: msw returns a Mutation row with
  `level:'Low'`; `role-policy-row-Mutation` shows `Low` and `haiku`, and `role-policy-row-Custom` shows the
  not-configured text. Mutation: read the level from `AGENT_TASK_ROLES` → shows `Frontier` → red. (This is the
  drift reproduced as a test.)
- V-9 `a role-policy failure does not blank the availability section`: msw 500 on `/api/role-policy` →
  `role-policy-section` shows `Could not load role policy.`; `routing-availability-section` still renders its
  heading. Mutation: throw from the section instead of rendering the error state → red.
- V-10 (in `RoutingSettingsTab.test.tsx`) `role policy section sits between runner defaults and the matrix`: DOM
  order of the three testids. Mutation: remove the element → red.
- R-2 the 14 existing `RoutingSettingsTab.test.tsx` cases and 9 `routingSettingsModel.test.ts` cases run
  unchanged (the new default handler keeps them green under `onUnhandledRequest: 'error'`).

**S3 (`StagePinEditor.test.tsx`, `routingPins.test.ts`, `rolePolicyModel.test.ts`)**

- V-11 `pinRequestFrom fixes Human provenance, omits card, and carries read-only fields through`: body keys are
  exactly `role, provenance, strength, candidates, reason, forbiddenAliases, notBefore, notAfter, agentId`;
  `provenance === 'Human'`; `card` absent; `forbiddenAliases` equals the loaded pin's. Mutation: drop the
  carry-through → red.
- V-12 `Required needs a confirm, Preferred saves directly`: with Strength Required, Save opens a confirm
  containing `routing_pin_conflict`; Preferred PUTs on the first click. Mutation: skip the confirm → red.
- V-13 `a 422 lands on the field, not in a toast`: msw 422 `{errors:{Candidates:[…]}}` → inline error under the
  candidates list; nothing was invalidated. Mutation: swallow the error → red.
- V-14 `Clear deletes the loaded pin id after a confirm that names the fallback`: confirm text contains the V-7
  copy; `DELETE /api/routing-pins/<id>` is sent once. Mutation: send DELETE before confirm → red.
- V-15 `a write invalidates both role-policy and routing-pins queries`: after PUT, msw records a second GET on
  both routes. Mutation: invalidate only pins → red.
- R-3 `RoutingSettingsTab.test.tsx`, `routingSettingsModel.test.ts`, `ComplexityChainCellEditor.test.tsx` run
  unchanged.

**S4 (`DelegateModal.test.tsx`)**

- V-16 `pre-selects the server level for the role`: msw role-policy with Mutation `level:'Low'`, no pin → choosing
  Mutation shows `Low`. Mutation: keep the constant → `Frontier` → red.
- V-17 `a stage pin wins over the policy default`: Plan row `effective {source:'pin', modelLevel:'High'}` with
  policy `Frontier` → `High`. Mutation: use the policy level when a pin exists → red.
- V-18 `falls back to the constant when the route fails`: msw 500 → Plan shows `Frontier` (constant) and the modal
  still opens. Mutation: throw → red.
- R-4 the 19 existing `DelegateModal.test.tsx` and 7 `agentTasks.test.tsx` cases run unchanged.

**S5** — M-1 manual: `grep -n "read-only for pins\|still-open" docs/orchestration-loop.md` returns the rewritten
lines; `docs/antiphon-api.md` has the `GET /api/role-policy` block; `docs/ops-http.md` has the row.

### V/R matrix

| Slice | V | R | Checkpoint |
|---|---|---|---|
| S1 | V-1, V-2, V-3, V-4 | — | CP-1 (red), CP-2, CP-3 |
| S2 | V-5–V-10 | R-2 | CP-4 |
| S3 | V-11–V-15 | R-3 | CP-5 |
| S4 | V-16–V-18 | R-4 | CP-6 |
| all | — | lint gate, whole client suite | CP-7 |

### Positive controls (pending; Mutation is not run in this card)

| PC | Exact method | Case → mutation → expected red |
|---|---|---|
| PC-1 | `RolePolicyEndpointTests.Get_lists_every_routable_role_in_order_with_policy_fields` | Mutation row → `Compose` uses `DefaultLevel` for all → `High` ≠ `Low` |
| PC-2 | `RolePolicyEndpointTests.Get_overlays_the_active_stage_pin_as_effective` | pinned Plan → ignore pins → `source == rolePolicy` |
| PC-3 | `RolePolicyEndpointTests.Get_ignores_card_pins_and_cleared_pins` | card pin → drop `cardId == null` filter → Code shows a pin |
| PC-4 | `RolePolicyReadServiceTests.Compose_resolves_alias_from_kind` | Grok kind → force ClaudeCode → alias mismatch |
| PC-5 | `rolePolicyModel.test.ts` V-5 | null kind → drop ClaudeCode default → `null/Low` |
| PC-6 | `RolePolicySection.test.tsx` V-8 | Mutation row → read `AGENT_TASK_ROLES` → `Frontier` |
| PC-7 | `RolePolicySection.test.tsx` V-9 | 500 → throw → sibling section missing |
| PC-8 | `rolePolicyModel.test.ts` V-11 | loaded pin with forbidden aliases → drop carry-through → key missing |
| PC-9 | `StagePinEditor.test.tsx` V-12 | Required → skip confirm → PUT on first click |
| PC-10 | `StagePinEditor.test.tsx` V-14 | Clear → DELETE before confirm |
| PC-11 | `routingPins.test.ts` V-15 | PUT → invalidate pins only → no second role-policy GET |
| PC-12 | `DelegateModal.test.tsx` V-16 | Mutation → keep constant → `Frontier` |
| PC-13 | `DelegateModal.test.tsx` V-17 | pinned Plan → policy level → `Frontier` ≠ `High` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 tests committed, endpoint not yet | `tests/Antiphon.Tests -> bin-c097-red/` | role-policy-red | `/*/*/RolePolicyEndpointTests/*` | V-1, V-2, V-3 | 3 methods run, 3 fail on 404 (intentional red; the HTTP tests use `JsonDocument`, no new server types) | 3 | 8 | false |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c097-api/` | role-policy-api | `/*/*/RolePolicyEndpointTests/*` | V-1, V-2, V-3 | 3 methods, 0 failed/skipped | 3 | 8 | false |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c097-api/` | role-policy-compose | `/*/*/RolePolicyReadServiceTests/*` | V-4 | 1 method, 0 failed/skipped (same build output as CP-2; the tool serialises the build) | 1 | 3 | false |
| CP-4 | S2 | n/a | client-role-policy | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/rolePolicyModel.test.ts src/features/settings/RolePolicySection.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/routingSettingsModel.test.ts` | V-5–V-10, R-2 | 4 files run; aggregate = 14 + 9 existing + the new cases (TestDesign freezes the number), 0 failed/skipped | n/a | 5 | false |
| CP-5 | S3 | n/a | client-pin-editor | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/StagePinEditor.test.tsx src/api/routingPins.test.ts src/features/settings/rolePolicyModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx` | V-11–V-15, R-3 | 5 files run, 0 failed/skipped | n/a | 6 | false |
| CP-6 | S4 | n/a | client-delegate-modal | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/DelegateModal.test.tsx src/api/agentTasks.test.tsx` | V-16–V-18, R-4 | 2 files run; aggregate = 19 + 7 existing + 3 new, 0 failed/skipped | n/a | 5 | false |
| CP-7 | all | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | lint gate (`client/src/lint.gate.test.ts`), whole client suite | `CLIENT TESTS EXIT CODE: 0` | n/a | 10 | true |

Lane: every row runs on the default placement (runner-defaults revision 2 = server2); no `-Runner`, no `-Platform`.
Green Vitest runs print only the aggregate line, so `Expect` on CP-4/CP-6 is the aggregate count, not per-file.
`Antiphon.Tests` is never run whole here (25 min); CP-1–CP-3 run one class filter each through
`scripts/run-checkpoint.ps1`, which takes the build slot itself.

### Cost

Ordinary green floor: 8 + 3 + 5 + 6 + 5 + 10 = 37 minutes (CP-2–CP-7). Red-first: 8 minutes (CP-1). Authoring:
S1 45, S2 50, S3 60, S4 30, S5 20 minutes. No production timeout or assertion is changed by this plan.

## 5. Risks and open operator decisions

Risks:

- **An editable UI over Required pins is a fleet-wide routing change per click.** A stage-wide Required pin makes
  every next create for that role go to the pinned target, refuses a disagreeing explicit ask with 409
  `routing_pin_conflict` (`RoutingPinService.cs:348-358`), bypasses the complexity cell (`orchestration-loop.md:878-880`),
  and a Human row cannot be replaced by the orchestrator's Auto writes until a human clears it (`:233-236`). The
  editor defaults to Preferred, confirms Required with that text (V-12), and never writes Auto. It does not touch
  Queued or running work (`antiphon-api.md:628`), and a pin naming a held alias is still 409 `model_disabled` at
  create — the editor shows the server's per-candidate availability so the operator sees that before saving.
- **Last writer wins** on `PUT /api/routing-pins` (D-6). The window is seconds and the row shows `updatedAt`;
  O-3 decides whether to close it server-side.
- **msw `onUnhandledRequest: 'error'`.** Any component that now calls `/api/role-policy` needs the default handler
  (S2) before its existing tests can pass; forgetting it is a red CP-4/CP-6, not a silent gap.
- **Alias literals.** Tests compare to `ModelLevelAliases.For` on the server and to the DTO's `alias` on the
  client; no test hard-codes `fable`/`opus`/`gpt-6-astra` as an expected value.
- **Lint gate.** Unused imports or `any` in new files fail CP-7.

Open decisions, each with a recommendation (the plan is written under the recommended value):

- **O-1 Read-only first, or editor in the same card?** Recommend: dispatch S1, S2, S4, S5 now; dispatch S3 after
  the operator confirms this plan's D-4/D-5 (an editable surface over Required pins is the one safety-relevant
  addition). If the operator declines S3, the card closes on S1/S2/S4/S5 with "editing stays `routing-pin.ps1`".
- **O-2 Default Strength in the editor.** Recommend Preferred (the script's default; falls through to the
  matrix/RolePolicy when the target is unavailable). Required stays one confirm away.
- **O-3 Stale-write protection.** Recommend defer; file a card for `expectedUpdatedAt` on the PUT with a 409.
- **O-4 Card-scoped pins in the UI.** Recommend no (D-5); listed read-only as today.
- **O-5 `RoutingPinsChanged` hub event.** Recommend defer (D-9); polling at 15 s matches the existing pin query.

## 6. Sessions that wait for input

This plan adds no session that waits for input and changes no release deadline; the CARD-1083 checklist does not
apply.
