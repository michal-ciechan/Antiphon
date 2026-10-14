# CARD-0097 verification design — per-role model/tier settings, read slices

TestDesign task `6fb07d73` (Frontier), branch `feat/card-task-6fb07d73`, based on plan commit
`0ccea9b6dfa3fc26903c1fa42d3648675afcc880` ([the plan](2026-10-13-card-0097-role-model-settings-plan.md), branch
`feat/card-task-9d94f1e5`, lands with the first Code slice). Source census 2026-10-13, read from source only: no
build, no test run, no discovery execution. Task binding present (`ANTIPHON_TASK_TOKEN` checked for presence only).
This is an additive verification companion; the plan's fix design (D-1–D-10) is unchanged except where O-1 holds S3.

**Orchestrator decisions applied.** O-1: dispatch the read slices S1, S2, S4, S5; **S3 (stage-pin editor over
Required pins) is HELD** and appears only as Appendix A (no checkpoint, no Code dispatch). O-2 (Preferred default),
O-3 (stale-write 409 deferred), O-4 (card pins read-only), O-5 (no pin hub event) stand. Post-land Mutation is
paused: PC-1–PC-27 are specified and pending; none is claimed run.

**Consequences of holding S3.** `fallbackOnClearCopy` (plan S2 file list) is Clear-confirm copy with no caller
until S3; it moves to Appendix A with its test V-7 so S2 ships no dead export. V-11–V-15, R-3 and the plan's CP-5
move to Appendix A under their original IDs. New IDs start at V-19.

**Slice order and sizing.** S2 client section (50 min) → S4 delegate modal (30) → S5 docs (20); S1 server route
(45) runs in parallel with S2 but its three checkpoint rows need a server-project build, and `tests/Antiphon.Tests`
restores with NU1605 today (floating `9.*` references, CARD-1229 InProgress). **Dispatch S1's Code task only after
CARD-1229 lands**, or accept that CP-3–CP-5 cannot run until it does; the Code task must not pin package versions
itself. S4 depends on S2's `client/src/api/rolePolicy.ts` and the `handlers.ts` default handler, so S4 is
dispatched after S2 is pushed (branch from S2's tip or after S2 lands). S5 is independent. Each slice commits and
pushes before its row runs.

**Collisions (read 2026-10-13).** CARD-0506 S1 landed on master (`f43fc6103`, F1 `9f27bd32e`); S2
(`DispatchConcurrencyEditor*`) is in review and S3 (`ConcurrencySettingsTab*`, `SettingsPage.tsx/.test.tsx`,
`ProjectConfig.tsx/.test.tsx`) is unlanded: none of those files is in S1/S2/S4. The one shared file is
`client/src/test/mocks/handlers.ts`: 0097 S2 appends one handler after the `/api/routing-pins` row (`:34`); a
0506 S3 handler would be a textual merge. S5 edits `docs/orchestration-loop.md:883-899`, `docs/antiphon-api.md`
beside `:623-631`, `docs/ops-http.md` beside the runner-defaults row (`:138`); 0506 touches `:811-816`, `:538`
and `:177` of the same files — different lines. CARD-1213 (branch naming, `{{GIT-COMMON}}`) has no file overlap.
`GET /api/agent-tasks/pipeline?unscoped=true` at design time showed zero in-flight, queued or ready tasks in all
14 stages; the orchestrator re-reads it before dispatching S2/S4 (same-area defer rule).

**Ground truth confirmed from source.** `DelegationSettings.RolePolicy` (`server/Application/Settings/DelegationSettings.cs:347-372`):
Investigate High→Frontier, Plan Frontier, TestDesign Frontier, Code Frontier, **Mutation Low→High**, Review
Frontier, Debug High→Frontier, Coverage High, Merge High, Docs Medium, Commit Medium, Test Low→Medium, Deploy Low;
`Kind` null and `EscalateAfterMinutes` null on each; `TimeoutMinutes` 240; `DefaultLevel = High` (`:374`),
`DefaultTimeoutMinutes = 240` (`:417`). `ComplexityRoutingService.RoutableRoles` (`:61-76`) is 14 roles:
Investigate, Plan, TestDesign, Code, Mutation, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, Custom.
`ModelLevelAliases.For` (`:90-95`): Claude fable/opus/sonnet/haiku, Grok `grok-4.7` at any level, Codex
`gpt-6-astra`/`gpt-6.1-sol`/`gpt-6.1-sol`/`gpt-5.6-luna`. The stale client mirror is `AGENT_TASK_ROLES` at
`client/src/api/agentTasks.ts:696` (`Mutation … level: 'Frontier'`; the comment is `:687`, the plan's `:697` is
off by one) and `DelegateModal.tsx:85` reads it; the other 13 mirror rows match the server. Enums serialize as
strings (`Program.cs:365`, `JsonStringEnumConverter`), so `agentKind`/`modelLevel`/`strength` are string values on
the wire and `kind: null` is JSON null. `RoutingPinService.ListAsync(null, null, ct)` returns stage **and** card
pins (`:130-154`: the card filter applies only when a card is given), so the stage-only filter must live in the new
read service. A pin may legitimately carry **zero** candidates (forbid-only or agent-only, `ResolveWrittenCandidates`
returns `[]`) or a **partial** head (`RoutingCandidateRequest` fields are nullable); `RoutingPin.Head` is
`Candidates[0]` or null.

## Verification design

### Inspection

| Bodies read | Boundary → V/R or exclusion |
|---|---|
| `DelegationSettings.cs:340-425`, `:1036-1130` (`RolePolicy`, `DefaultLevel`, `DefaultTimeoutMinutes`, `RolePolicyEntry`) | shipped values and nullability → V-1, V-4, V-19; `RecommendedInFlight` excluded (CARD-0506 owns it) |
| `ComplexityRoutingService.cs:50-85` (`RoutableRoles`, specialist refusal) | row order and count → V-1, V-19; Check/Distill/Diagnose never rows |
| `ModelLevelAliases.cs` (all), `AgentKind`, `AgentModelLevel`, `AgentTaskRole` enums | alias literals and enum spellings → V-1, V-4, F-1 |
| `RoutingPinDtos.cs` (all); `RoutingPinService.cs:95-300`, `:438-500`, `:570-610`, `:780-810` (`FindActiveAsync`, `ListAsync`, `UpsertAsync`, `ClearAsync`, `ToDto`, `ValidateRole`, `ExpireAsync`, `EvaluateCandidatesAsync`) | stage/card grain, cleared rows, candidate availability verdict, zero/partial candidates → V-2, V-3, V-20–V-23, V-25 |
| `RoutingPinEndpoints.cs` (all); `AgentTaskEndpoints.ResolvePollingCallerAsync` (`:538-549`); `Program.cs:726`, `:1144`, `:365` | PUT needs no bearer in the test host (caller null); registration seams; string enums → V-2, V-3 recipe |
| `OrchestratorInstructionsSnapshotBuilder.cs:47-110` (`ReadPipelinePolicy`, `RolesFromSettings`) | the existing RolePolicy reader (alphabetical, 13 keys, no Custom) — precedent, not reused: this card renders `RoutableRoles` order and 14 rows |
| **`tests/Antiphon.Tests/Api/OrchestratorInstructionsEndpointTests.cs`**, **`Application/ComplexityChainHttpTests.cs`** (incl. `ComplexityChainApiWebAppFactory` at `:142`), `TestHelpers/AntiphonWebAppFactory.cs:1-140`, **`Application/RoutingPinServiceTests.cs:1-80`, `:280-345`** (`SeedCardAsync`), `Application/ComplexityRoutingComposeTests.cs:1-50` | HTTP fixture (PerClass factory subclass declared in the test file, `JsonElement` oracles, `[Category("Integration")]`), card seeding through `AppDbContext`, pure-compose `[Category("Unit")]` recipe → V-1–V-4, V-19–V-23 |
| `client/src/api/routingPins.ts`, `runnerDefaults.ts:1-80`, `agents.ts:90-110`, `boards.ts:10`, `agentTasks.ts:14-50`, `:687-706` | client types, query-key/hook recipe, mirror rows → F-1, F-2, V-8, V-16–V-18 |
| `client/src/features/settings/RoutingSettingsTab.tsx:1-110`, `routingSettingsModel.ts:1-130`, `RunnerDefaultsSection.tsx:1-80`; **`RoutingSettingsTab.test.tsx`** (all), **`routingSettingsModel.test.ts`** (all) | section pattern (own query, `Loader`, `Could not load …` text, `data-testid` papers), `serveRouting` fixture, `compareDocumentPosition` order recipe → V-9, V-10, V-25, R-2 |
| `client/src/features/delegations/DelegateModal.tsx` (all), `TaskChip.tsx:41` (`tier-<Level>` testid); **`DelegateModal.test.tsx`** (all); `client/src/api/agentTasks.test.tsx:1-120` and its 7 titles | role chips are `radio` roles, synchronous tier assertions, `captureCreate` recipe → V-16–V-18, V-27, R-4 |
| `client/src/test/utils.ts`, `setup.ts`, `mocks/handlers.ts` (all); `vite.config.ts` test block; `lint.gate.test.ts:16-21`; `scripts/test-client.ps1` (all) | retry off, `onUnhandledRequest: 'error'`, 20 s budget, positional file filters, aggregate-only output on green → CP-1, CP-2, CP-6 |
| `docs/testing-and-build.md:304-474`, `:814-931` (manifest schema, runner tool, combined filters, PC execution) | row shapes, `Min` = executed TUnit results, method-scoped PC cycles → Checkpoints, Positive controls |
| `docs/orchestration-loop.md:862-899`, `docs/antiphon-api.md:623-631`, `docs/ops-http.md:138` | S5 target lines → M-1 |

#### F-1 Frozen wire contract for `GET /api/role-policy` (new; binds S1 and the S2/S4 msw fixtures)

Property names camelCase; enums as strings; nullable fields present explicitly.

```
RolePolicyDto      { defaultLevel: AgentModelLevel | null, defaultTimeoutMinutes: number, roles: RolePolicyRowDto[] }   // 14 rows, RoutableRoles order
RolePolicyRowDto   { role: AgentTaskRole, configured: boolean,
                     level: AgentModelLevel | null,     // DefaultLevel when !configured; null when undefined (S1 R3)
                     kind: AgentKind | null,            // null = ClaudeCode (not resolved here)
                     alias: string,                     // For(kind ?? ClaudeCode, level); non-null
                     escalateTo: AgentModelLevel | null, escalateAfterMinutes: number | null,
                     timeoutMinutes: number,            // DefaultTimeoutMinutes when !configured
                     stagePin: RoutingPinDto | null,    // the active stage-wide pin (cardId null) with evaluated candidates
                     effective: RolePolicyEffectiveDto }
RolePolicyEffectiveDto { source: 'pin' | 'rolePolicy', agentKind: AgentKind | null, modelLevel: AgentModelLevel | null,
                         alias: string | null, strength: RoutingPinStrength | null }
```

`effective` is a fail-closed whitelist over the stage pin (hand-written expected rows; V-2, V-20–V-23):

| stagePin | head candidate | effective |
|---|---|---|
| null | — | `rolePolicy`, `agentKind = kind ?? ClaudeCode`, `modelLevel = level`, `alias = row.alias`, `strength null` |
| pin | kind and level set | `pin`, head kind, head level, `For(head kind, head level)`, `strength = pin.strength` |
| pin | kind set, level null | `pin`, head kind, `row.level`, `For(head kind, row.level)`, pin strength |
| pin | kind null, level set | `pin`, `row.kind ?? ClaudeCode`, head level, `For(row.kind ?? ClaudeCode, head level)`, pin strength |
| pin | no candidates (forbid-only / agent-only) | `pin`, `agentKind null`, `modelLevel null`, `alias null`, pin strength (target withheld, not guessed) |

Head = `candidates[0]`, never a later candidate; availability does not move the head (the launch-time walker owns
holds; the row shows the server's per-candidate verdict). Card pins never populate `stagePin` or `effective`.

**F-1 unsupported values (S1 R2, review 738e05a2).** A kind is supported when it is null or in
`AgentTaskService.DelegatableKinds` (ClaudeCode, Grok, Codex); a level when it is a defined `AgentModelLevel`.
Configuration validation admits Raw/OpenCode kinds and undefined (999) enum values, so the reader guards itself.
S1 R3 (review 6cb28e72 F1) makes row `level` and `defaultLevel` nullable; other field names and nullability are
unchanged:

| value | wire |
|---|---|
| policy kind or level unsupported, or head kind/level (after filling from the policy) unsupported | `effective` `agentKind`, `modelLevel`, `alias` null; `source` and `strength` kept |
| policy kind unsupported | row `alias` `""` (no alias is invented); row `kind` as configured when defined (`"Raw"`), null when undefined |
| policy level, `DefaultLevel` undefined | row `level` / `defaultLevel` null (no tier is invented), row `alias` `""` |
| `escalateTo` undefined | null |
| `stagePin` (and head mirror) kind or level undefined | null; that candidate's `alias` (`modelAlias`) null when its kind or level is unsupported |

The other rows are unaffected (V-28, V-29). Clients show a null `level` as `unknown` (V-5) and the modal falls back to
the constant with the fallback note (V-30).

#### F-2 Default msw handler and fixture (S2 adds to `client/src/test/mocks/handlers.ts`; S4 reuses)

`export const ROLE_POLICY_FIXTURE: RolePolicyDto` with `defaultLevel 'High'`, `defaultTimeoutMinutes 240`, and
the 14 shipped rows in RoutableRoles order, each `configured: true` (Custom `false`), `kind: null`,
`escalateAfterMinutes: null`, `timeoutMinutes: 240`, `stagePin: null`, `effective: { source: 'rolePolicy',
agentKind: 'ClaudeCode', modelLevel: <level>, alias: <alias>, strength: null }`:

| role | level | alias | escalateTo |
|---|---|---|---|
| Investigate | High | opus | Frontier |
| Plan, TestDesign, Code, Review | Frontier | fable | null |
| Mutation | Low | haiku | High |
| Debug | High | opus | Frontier |
| Coverage, Merge | High | opus | null |
| Docs, Commit | Medium | sonnet | null |
| Test | Low | haiku | Medium |
| Deploy | Low | haiku | null |
| Custom (`configured: false`) | High | opus | null |

Handler: `http.get('/api/role-policy', () => HttpResponse.json(ROLE_POLICY_FIXTURE))`, inserted after the
`/api/routing-pins` row. The 19 existing `DelegateModal.test.tsx` cases keep passing only because the fixture
mirrors the shipped server values (Test → `tier-Low`, Docs → `tier-Medium`, Plan → `tier-Frontier`). Tests that
need a different row build it by spreading the fixture (`{ ...ROLE_POLICY_FIXTURE, roles: ROLE_POLICY_FIXTURE.roles.map(…) }`);
no production resolver is called to derive an expectation.

#### F-3 Recipes

`A` = real HTTP through `RolePolicyApiWebAppFactory : AntiphonWebAppFactory` (declared `public sealed … ;` at the
bottom of `RolePolicyEndpointTests.cs`, as `ComplexityChainHttpTests.cs:142` does), `[ClassDataSource<…>(Shared =
SharedType.PerClass)]`, `[NotInParallel("RolePolicyEndpoints")]`, `[Category("Integration")]`; oracles are
`JsonElement` reads of literal values; every pin written is deleted in `finally`. `U` = pure
`RolePolicyReadService.Compose(settings, pins)` with `new DelegationSettings()` and hand-built `RoutingPinDto`
records, `[Category("Unit")]`. `M` = direct pure calls into `rolePolicyModel.ts`, `it.each` rows with literal
expected strings, titles `C097 Vnn <name>`. `E` = `renderWithProviders` + `server.use` + `userEvent`, notifications
mocked as the touched files already do. Test files and titles below are **binding required** until Code creates
them and records their execution; this survives Code for any missing or unbound payload.

### Delivery inventory

No new async delivery path, queue, event, session input or persistence boundary is introduced: the route is a
synchronous composition over `IOptions<DelegationSettings>` and one `ListAsync` read; the client polls at 15 s
(`refetchInterval`, same as `useRoutingPins`) and the delegate modal reads the same query. No producer-to-recipient
or UserPrompt receipt claim is made anywhere in this design, so no substitute is declared and none is needed. The
only write exercised (PUT/DELETE `/api/routing-pins` in V-2/V-3) is the existing CARD-0305 service, observed by a
subsequent GET of the new route in the same test — a GET of the recipient, not a write receipt. Deadlines: none;
fixture timestamps (`updatedAt`) are display data, not clocks.

### Proves it works now

**S1 — `tests/Antiphon.Tests/Api/RolePolicyEndpointTests.cs` (A; committed before the endpoint for CP-3 red,
`JsonElement` only, no new server type referenced)**

- V-1 `Get_lists_every_routable_role_in_order_with_policy_fields` | A | GET `/api/role-policy` on a host with no
  pins → 200; `defaultLevel == "High"`, `defaultTimeoutMinutes == 240`; `roles.Length == 14` and the `role`
  sequence equals the literal list Investigate, Plan, TestDesign, Code, Mutation, Review, Debug, Coverage, Docs,
  Commit, Test, Deploy, Merge, Custom; Mutation row `configured true, level "Low", kind null, alias "haiku",
  escalateTo "High", escalateAfterMinutes null, timeoutMinutes 240, stagePin null, effective {source "rolePolicy",
  agentKind "ClaudeCode", modelLevel "Low", alias "haiku", strength null}`; Custom row `configured false, level
  "High", alias "opus", escalateTo null, timeoutMinutes 240, stagePin null`; Test row `escalateTo "Medium"`; Docs row
  `alias "sonnet"`. `stagePin == null` is asserted only on Mutation and Custom (V-2/V-3 use Plan, Code, Debug).
  Case Mutation → mutation: `Compose` reads `DefaultLevel` for each role → `"High"` read, `"Low"` expected → red.
  Literal aliases are deliberate (house rule and `ComplexityChainHttpTests` precedent): a test that compares
  production to `ModelLevelAliases.For` cannot go red on an alias regression; an alias-map edit changes these
  literals on purpose.
- V-2 `Get_overlays_the_active_stage_pin_as_effective` | A | PUT `/api/routing-pins` `{ role: "Plan", provenance:
  "Human", strength: "Required", candidates: [{ agentKind: "Codex", modelLevel: "Frontier" }], reason: "C097 V-2" }`
  → 200, keep `id`; GET → Plan row `stagePin.id == id`, `stagePin.candidateCount == 1`,
  `stagePin.candidates[0].alias == "gpt-6-astra"`, `stagePin.candidates[0].availableNow == true` (no holds in this
  host), `effective {source "pin", agentKind "Codex", modelLevel "Frontier", alias "gpt-6-astra", strength
  "Required"}`, while the default column is untouched (`level "Frontier"`, `alias "fable"`); Code row
  `effective.source == "rolePolicy"`, `stagePin null`. `finally` DELETE `/api/routing-pins/{id}`. Case pinned Plan →
  mutation: `Compose` ignores the pin list → `source "rolePolicy"` → red.
- V-3 `Get_ignores_card_pins_and_cleared_pins` | A | seed a card with
  `RoutingPinServiceTests.SeedCardAsync(db, "CARD-9097")` from a factory `AppDbContext` scope; PUT a **card** pin
  `{ role: "Code", card: "<card Id guid>", provenance: "Human", strength: "Required", agentKind: "Grok",
  modelLevel: "Frontier", reason }` → 200 (id A); PUT a Debug stage pin `{ role: "Debug", provenance: "Human",
  strength: "Preferred", agentKind: "ClaudeCode", modelLevel: "High" }` → 200 (id B); DELETE `/api/routing-pins/{B}`
  → 204; control: GET `/api/routing-pins` lists A (the card pin is live); GET `/api/role-policy` → Code row
  `stagePin null`, `effective.source "rolePolicy"`, `effective.alias "fable"`; Debug row `stagePin null`,
  `effective.source "rolePolicy"`. `finally` DELETE A. Case card pin → mutation: drop the `cardId == null` filter →
  Code shows A → red. Case cleared pin → mutation: `ReadAsync` queries `_db.RoutingPins` directly instead of
  `ListAsync` (no `ClearedAt` filter) → Debug shows B → red.

**S1 — `tests/Antiphon.Tests/Application/RolePolicyReadServiceTests.cs` (U; committed with the production code)**

- V-4 `Compose_resolves_alias_from_kind` | U | settings with `RolePolicy["Docs"].Kind = Grok`,
  `RolePolicy["Commit"].Kind = Codex`, `RolePolicy["Debug"].EscalateAfterMinutes = 7`, no pins → Docs `kind Grok,
  alias "grok-4.7", effective.agentKind Grok, effective.alias "grok-4.7"`; Commit `alias "gpt-6.1-sol"`; Code (kind
  null) `alias "fable", effective.agentKind ClaudeCode`; Debug `escalateAfterMinutes 7`. Case Grok Docs → mutation:
  `Compose` passes `AgentKind.ClaudeCode` to the alias map unconditionally → `"sonnet"` → red.
- V-19 `Compose_lists_RoutableRoles_in_order_and_marks_Custom_unconfigured` | U | settings with `DefaultLevel =
  Medium`, `DefaultTimeoutMinutes = 90`, no pins → 14 rows in `RoutableRoles` order; Custom `configured false, level
  Medium, alias "sonnet", timeoutMinutes 90, escalateTo null, kind null`; Plan still `Frontier`, `240`. Case Custom →
  mutation: `Compose` builds Custom from `new RolePolicyEntry()` (configured true, High, 240) instead of the
  defaults → `High` ≠ `Medium` → red.
- V-20 `Compose_effective_prefers_the_first_stage_pin_candidate_and_excludes_card_pins` | U | pins: a Plan stage
  `RoutingPinDto` (`CardId null`, Preferred, Human, candidates `[Codex/Frontier "gpt-6-astra", ClaudeCode/High
  "opus"]`, `CandidateCount 2`) and a Code **card** pin (`CardId` set, Grok/Frontier) → Plan `stagePin` is that pin,
  `effective {pin, Codex, Frontier, "gpt-6-astra", Preferred}`; Code `stagePin null`, `effective.source rolePolicy`.
  Case two candidates → mutation: effective takes the **last** candidate → `ClaudeCode/High` → red.
- V-21 `Compose_fills_a_missing_pin_level_from_the_policy` | U | Docs stage pin, head `{Grok, null}` → `effective
  {pin, Grok, Medium, "grok-4.7", strength}`. Mutation: leave `modelLevel` null when the head has none → red.
- V-22 `Compose_fills_a_missing_pin_kind_from_the_policy` | U | Docs stage pin, head `{null, High}` → `effective
  {pin, ClaudeCode, High, "opus"}`; with `RolePolicy["Docs"].Kind = Grok` → `{pin, Grok, High, "grok-4.7"}`.
  Mutation: default the missing kind to ClaudeCode ignoring `row.kind` → Grok case `"opus"` → red.
- V-23 `Compose_withholds_the_target_for_a_candidate_free_pin` | U | Docs stage pin with `Candidates []`,
  `CandidateCount 0`, Required → `effective {source "pin", agentKind null, modelLevel null, alias null, strength
  Required}`. Mutation: fall through to the policy when there is no head → `source "rolePolicy"` → red.

- V-28 `Compose_withholds_the_target_for_unsupported_or_undefined_values` | U, `[MethodDataSource]` 15 rows | the
  DTO is serialised with the API options (web + `JsonStringEnumConverter(allowIntegerValues: false)`) and read as
  JSON; literal expectations per row: `valid-control`, `policy-kind-Raw`, `policy-kind-OpenCode`, `policy-kind-999`,
  `policy-level-999`, `policy-escalateTo-999`, `default-level-999` (Custom row), `pin-valid-control`,
  `pin-head-kind-Raw`, `pin-head-kind-999`, `pin-head-level-999` (each with a valid second candidate that must not
  be used), `pin-grok-level-999`, `pin-grok-inherits-policy-level-999`, `pin-level-inherits-policy-kind-Raw`,
  `prototype-role-names` (`__proto__`, `constructor`, `toString` keys); every row: 14 roles, Mutation effective
  `ClaudeCode/Low/haiku`, Code alias `fable`, every other row's effective alias non-empty. Red at `d138b99ef`:
  invented `sonnet`/`opus` aliases and `JsonException` for 999. S1 R3: `policy-level-999`, `default-level-999` and
  `pin-grok-inherits-policy-level-999` expect row `level` null (was `"High"`); `default-level-999` expects
  `defaultLevel` null (was `"High"`). Red at `9330f6bcd`: those three rows read `"High"`.
- V-29 `RolePolicyEndpointTests.Get_serialises_every_row_when_one_role_has_unsupported_values` | A, own factory bound
  with `Delegation:RolePolicy:Docs:Level=999`, `Commit:Kind=Raw`, `Deploy:Kind=999` → 200, 14 rows; Docs `level
  null, kind null, alias ""`, Commit `kind "Raw", level "Medium", alias ""`, Deploy `kind null, level "Low",
  alias ""`, each `effective {rolePolicy, null, null, null, null}`; Mutation `ClaudeCode/Low/haiku`, Test alias
  `haiku`. Red at test-only `dcf1a2835` (production code of `d138b99ef`): HTTP 400, expected 200. S1 R3 red at
  `9330f6bcd`: Docs `level` `"High"`, expected null.

**S2 — `client/src/features/settings/rolePolicyModel.test.ts` (M)**

- V-5 `C097 V05 $name` over `defaultLabel(row)` (7 rows) | M | `null-kind-low` `{configured true, kind null, level
  'Low', alias 'haiku'}` → `ClaudeCode/Low (haiku)`; `grok-frontier` `{kind 'Grok', 'Frontier', 'grok-4.7'}` →
  `Grok/Frontier (grok-4.7)`; `codex-medium` `{kind 'Codex', 'Medium', 'gpt-6.1-sol'}` → `Codex/Medium
  (gpt-6.1-sol)`; `unconfigured` `{configured false, kind null, 'High', 'opus'}` → `not configured — DefaultLevel
  High (opus)`. Mutations: drop the null → ClaudeCode branch → row 1 `null/Low (haiku)` → red; ignore `configured` →
  row 4 renders the configured form → red. S1 R3 rows: `configured-level-null` `{configured true, kind null, level
  null, alias ''}` → `unknown`; `unconfigured-default-level-null` `{configured false, level null, alias ''}` → `not
  configured — DefaultLevel unknown`; `configured-kind-undefined` `{configured true, kind null, 'Medium', alias ''}` →
  `unknown/Medium ()` (an empty alias beside a null kind is the server's NoAlias for an undefined kind).
- V-6 `C097 V06 $name` over `effectiveLabel(row)` (5 rows) | M | `pin-required-human` (stagePin provenance Human,
  effective `{pin, Codex, Frontier, 'gpt-6-astra', Required}`) → `Required Human pin → Codex/Frontier (gpt-6-astra)`;
  `pin-preferred-auto` (provenance Auto, `{pin, ClaudeCode, High, 'opus', Preferred}`) → `Preferred Auto pin →
  ClaudeCode/High (opus)`; `role-policy` (`{rolePolicy, ClaudeCode, Low, 'haiku', null}`) → `default →
  ClaudeCode/Low (haiku)`; `pin-without-candidates` (`{pin, null, null, null, Required}`, Human) → `Required Human
  pin → no candidate (agent-only or forbid-only; see the pin)`; `unknown-source` (`source: 'future' as never`) →
  `unknown`. Mutations: ignore `source` → row 3 red; treat anything not `rolePolicy` as a pin → row 5 red.
- V-24 `C097 V24 $name` over `escalateLabel(row)` / `timeoutLabel(row)` (5 rows) | M | `escalate-manual`
  `{escalateTo 'High', escalateAfterMinutes null}` → `High (manual)`; `escalate-none` `{null, null}` → `—`;
  `escalate-timed` `{'Frontier', 7}` → `Frontier after 7 min quiet`; `timeout-240` `{timeoutMinutes 240}` →
  `240 min`; `timeout-off` `{0}` → `off` (`<= 0` turns the ceiling off, `DelegationSettings.cs:1093`). Mutations:
  render the timed form when `escalateAfterMinutes` is null → row 1 red; render `0 min` → row 5 red.

**S2 — `client/src/features/settings/RolePolicySection.test.tsx` (E, `renderWithProviders(<RolePolicySection />)`)**

- V-8 `C097 V08 rows-from-dto-not-mirror` | E | handler returns the fixture plus a 15th row `{ role: 'FutureRole',
  configured true, level 'Medium', alias 'sonnet', … }` appended → `getAllByTestId(/^role-policy-row-/)` length 15;
  `role-policy-row-Mutation` has text `ClaudeCode/Low (haiku)` and not `Frontier`; `role-policy-row-Custom` contains
  `not configured`; `role-policy-row-FutureRole` present; `role-policy-row-TestDesign` precedes
  `role-policy-row-Mutation` (`compareDocumentPosition`; the mirror lists Mutation before TestDesign). Mutations:
  read the level from `AGENT_TASK_ROLES` → `Frontier` → red; iterate `AGENT_TASK_ROLES` for rows → FutureRole
  missing and order flips → red.
- V-25 `C097 V25 stage-pin-column-and-effective` | E | Plan row with `stagePin` `{Required, Human, candidates:
  [{Codex, Frontier, alias 'gpt-6-astra', availableNow false, unavailableReason 'held until 2026-10-14T00:00:00Z
  (manual)'}, {ClaudeCode, Frontier, 'fable', true, null}], candidateCount 2, reason 'plan on sol', updatedAt
  '2026-10-13T16:47:00Z'}` and `effective {pin, Codex, Frontier, 'gpt-6-astra', Required}` → within
  `role-policy-row-Plan`: `Required Human pin`, `1. Codex/Frontier (gpt-6-astra)`, `held until 2026-10-14T00:00:00Z
  (manual)`, `2. ClaudeCode/Frontier (fable)`, `plan on sol`, `Required Human pin → Codex/Frontier (gpt-6-astra)`;
  within `role-policy-row-Code`: pin cell `—` and `default → ClaudeCode/Frontier (fable)`; no text matching
  `/fallback to/i` anywhere in the section (server verdict only, no invented fallback). Mutation: render every
  candidate as available (ignore `availableNow`) → the held reason is missing → red.

**S2 — `client/src/features/settings/RoutingSettingsTab.test.tsx` (E, +2 after the existing 14)**

- V-9 `C097 V09 role-policy-failure-keeps-siblings` | E | `serveRouting()` plus `server.use(http.get('/api/role-policy',
  () => new HttpResponse(null, { status: 500 })))` → `findByText('Could not load role policy.')` inside
  `role-policy-section`; `getByRole('heading', { name: 'Model availability' })`, `getByTestId('runner-defaults-section')`
  and `getByTestId('routing-matrix-row-Code')` still present. Mutation: the section has no error branch (throws /
  renders rows from `undefined`) → React error unmounts the tree → red.
- V-10 `C097 V10 section-order` | E | `serveRouting()` (default role-policy handler) → DOM order
  `runner-defaults-section` → `role-policy-section` → `routing-matrix-row-any` by `compareDocumentPosition`; the
  section heading `Role policy` is present. Mutation: remove `<RolePolicySection />` from `RoutingSettingsTab` →
  `role-policy-section` missing → red.

**S4 — `client/src/features/delegations/DelegateModal.test.tsx` (E, +4 after the existing 19)**

- V-16 `C097 V16 server-level-preselects` | E | `server.use` role-policy with the Mutation row `level 'High', alias
  'opus'` (a value equal to neither the old `Frontier` nor the corrected `Low`), no pin → click radio `Mutation` →
  `await findByTestId('tier-High')`; `queryByTestId('tier-Low')` null; type a goal, click Delegate → captured body
  `role 'Mutation', modelLevel null` (the role still carries the tier). Mutation: keep reading the constant → `Low`
  → red.
- V-17 `C097 V17 stage-pin-wins` | E | Plan row `level 'Frontier'`, `effective {source 'pin', agentKind 'ClaudeCode',
  modelLevel 'High', alias 'opus', strength 'Required'}` → click radio `Plan` → `findByTestId('tier-High')`, no
  `tier-Frontier`. Mutation: use `row.level` when a pin exists → `Frontier` → red.
- V-18 `C097 V18 route-failure-falls-back-to-constant` | E | 500 on `/api/role-policy` → click radio `Mutation` →
  `findByTestId('tier-Low')` (corrected constant); `getByLabelText('Goal')` present; after typing, Delegate enabled.
  Mutations: revert `agentTasks.ts` Mutation to `'Frontier'` → red; `throw` on query error instead of falling back →
  modal fails to render → red.
- V-27 `C097 V27 orchestrator-floor-on-server-level` | E | default fixture (Mutation `Low`) → click radio
  `Sub-orchestrator`, then radio `Mutation` → `findByTestId('tier-High')`, no `tier-Low`. Mutation: apply the
  Medium/Low → High floor to the constant only and render the raw server level → `Low` → red.
- V-30 `C097 V30 undefined-policy-level-falls-back-to-constant` | E | Mutation row `level null, alias ''`, effective
  `{rolePolicy, null, null, null, null}` (the S1 R3 wire for Level=999) → click radio `Mutation` →
  `findByTestId('delegate-tier-source-fallback')`, no `delegate-tier-source-rolePolicy`, `tier-Low`. Green at
  `9330f6bcd`: the modal's level check already rejects null; the S1 R3 red is server-side (V-28, V-29). PC-35.

**S5 — docs (manual, no checkpoint)**

- M-1 | manual | `grep -n "read-only for pins\|still-open" docs/orchestration-loop.md` returns the rewritten lines
  (the Routing tab shows RolePolicy defaults, ladder, timeout, alias and the effective next-create target; editing
  stays `routing-pin.ps1` while S3 is held); `docs/antiphon-api.md` has a `GET /api/role-policy` block beside
  `:623-631` naming F-1's fields; `docs/ops-http.md` has one row beside the runner-defaults row. Reviewed by eye;
  no automated oracle.

### Guards the regression

- R-1 whole client suite and the lint gate (`client/src/lint.gate.test.ts`, ESLint over the client) run green with
  the new files; decisive assertion `CLIENT TESTS EXIT CODE: 0` and the lint case `eslint reports zero problems
  across the client` passing (CP-6).
- R-2 the 14 existing `RoutingSettingsTab.test.tsx` cases and the 9 `routingSettingsModel.test.ts` cases run
  unchanged with the new section mounted and the new default handler under `onUnhandledRequest: 'error'` (CP-1).
- R-4 the 19 existing `DelegateModal.test.tsx` cases and the 7 `agentTasks.test.tsx` cases run unchanged (CP-2);
  the decisive existing assertion is `lets the role carry the tier rather than asking for one`, which reads
  `tier-Low` synchronously after clicking Test — green only while the loading fallback renders the constant.
- Not touched, so no R: `RoutingPinService` and its 9 test classes; `OrchestratorInstructionsEndpointTests`.

### Guard inventory

| G | Plan ref + guard | PC |
|---|---|---|
| G-1 | D-2: a row's `level`/`escalateTo`/`timeoutMinutes` come from that role's own `RolePolicy` entry (V-1) | PC-1 |
| G-2 | D-2: an active stage pin sets `effective.source = pin` with the head's kind/level/alias (V-2) | PC-2 |
| G-3 | D-2 "keeping cardId == null": card pins never populate `stagePin`/`effective` (V-3) | PC-3 |
| G-4 | D-2: cleared pins are excluded because `ReadAsync` reads through `ListAsync` (V-3 Debug arm) | PC-4 |
| G-5 | D-2/G2: `alias` resolves from the role's `Kind`, not Claude unconditionally (V-4) | PC-5 |
| G-6 | D-3: an unconfigured role reports `configured false` with `DefaultLevel`/`DefaultTimeoutMinutes` (V-19) | PC-6 |
| G-7 | F-1: effective head is `candidates[0]`, strength carried (V-20) | PC-7 |
| G-8 | F-1: a head without a level takes the policy level (V-21) | PC-8 |
| G-9 | F-1: a head without a kind takes the policy kind (V-22) | PC-9 |
| G-10 | F-1 fail-closed: a candidate-free pin keeps `source pin` and withholds the target (V-23) | PC-10 |
| G-11 | D-3: `defaultLabel` renders a null kind as ClaudeCode (V-5 row 1) | PC-11 |
| G-12 | D-3: `defaultLabel` renders the not-configured form from `configured` (V-5 row 4) | PC-12 |
| G-13 | D-3: `effectiveLabel` says `default →` for `rolePolicy` (V-6 row 3) | PC-13 |
| G-14 | fail-closed: `effectiveLabel` says `unknown` for an unlisted source (V-6 row 5) | PC-14 |
| G-15 | D-3: ladder shown as manual when `escalateAfterMinutes` is null (V-24 row 1) | PC-15 |
| G-16 | D-3: timeout `<= 0` shown as `off` (V-24 row 5) | PC-16 |
| G-17 | G4/D-8: section values come from the DTO, not `AGENT_TASK_ROLES` (V-8 Mutation text) | PC-17 |
| G-18 | D-2: section rows and order come from the DTO (V-8 FutureRole/order) | PC-18 |
| G-19 | D-3: candidate availability is the server verdict (V-25 held reason) | PC-19 |
| G-20 | D-1: a role-policy failure stays inside its paper (V-9) | PC-20 |
| G-21 | D-1: the section sits between runner defaults and the matrix (V-10) | PC-21 |
| G-22 | D-8: the modal pre-selects the server level (V-16) | PC-22 |
| G-23 | D-8: a stage pin's head level beats the policy level in the modal (V-17) | PC-23 |
| G-24 | D-8/G4: the mirror constant is corrected to Low for Mutation (V-18 fallback value) | PC-24 |
| G-25 | D-8: a route failure falls back to the constant and keeps the modal usable (V-18) | PC-25 |
| G-26 | existing floor: a sub-orchestrator floors a Low/Medium server level at High (V-27) | PC-26 |
| G-27 | D-8: while loading, the constant renders synchronously (R-4 existing test) | PC-27 |
| G-28 | F-1 unsupported: an unsupported policy kind withholds the target and invents no alias (V-28 `policy-kind-Raw`) | PC-28 |
| G-29 | F-1 unsupported: an undefined policy level is written as null (V-28 `policy-level-999`, V-29) | PC-29 |
| G-30 | F-1 unsupported: an unsupported head kind/level withholds the pin target (V-28 `pin-head-kind-Raw`) | PC-30 |
| G-31 | F-1 unsupported: undefined values in `stagePin` are written as null (V-28 `pin-head-kind-999`) | PC-31 |
| G-32 | F-1 unsupported: an undefined `escalateTo` is written as null (V-28 `policy-escalateTo-999`) | PC-32 |
| G-33 | S1 R3: an undefined `DefaultLevel` is written as null (V-28 `default-level-999`) | PC-33 |
| G-34 | S1 R3: `defaultLabel` shows a null level as `unknown` (V-5 `configured-level-null`) | PC-34 |
| G-35 | S1 R3: the modal falls back when the role-policy level is null (V-30) | PC-35 |
| G-36 | S1 R3: `defaultLabel` shows an undefined kind as `unknown`, not ClaudeCode (V-5 `configured-kind-undefined`) | PC-36 |

Audit: **guards=36, mapped=36, missing=0, duplicate PC maps=0**. The polling cadence (15 s) and the tab subtitle
copy are not guards (no behaviour an operator relies on; same untested status as `useRoutingPins`).

### Positive controls

Pending; no run authorized in this task. Mutation runs break → intended red → restore → fresh green after land;
Code runs ordinary V/R; Review judges before land. Each PC uses a compiling production defect, never a changed
expectation or removed test; a build failure, fixture error or zero-execution run is not red. TUnit detectors are
method-scoped `--treenode-filter "/*/*/<Class>/<Method>"` with `-Expect <Class>.<Method> -MinExecuted 1`; Vitest
detectors are `pwsh -NoProfile -File scripts/test-client.ps1 <file> -t '^<describe> <exact title>$'`: Vitest's `-t`
matches the full name with the `describe` prefix, so the M/E cells below name it (a bare title selects zero cases). PC-1–PC-4 (same class)
and PC-5–PC-10 (same `Compose` method) run one at a time; PC-4 (`ReadAsync`) may batch with one `Compose` PC.

| PC | Break G by compiling defect | Exact detecting method / intended red assertion |
|---|---|---|
| PC-1 | `Compose` uses `_settings.DefaultLevel` for each row | `RolePolicyEndpointTests.Get_lists_every_routable_role_in_order_with_policy_fields`: Mutation `level` expected `"Low"`, read `"High"` |
| PC-2 | `Compose` receives an empty pin list (ignore `pins` argument) | `RolePolicyEndpointTests.Get_overlays_the_active_stage_pin_as_effective`: Plan `effective.source` expected `"pin"` |
| PC-3 | remove the `CardId == null` filter in `Compose` | `RolePolicyEndpointTests.Get_ignores_card_pins_and_cleared_pins`: Code `stagePin` expected null |
| PC-4 | `ReadAsync` reads `_db.RoutingPins` without `ClearedAt == null` instead of `ListAsync` | same method: Debug `stagePin` expected null |
| PC-5 | alias map called with `AgentKind.ClaudeCode` unconditionally | `RolePolicyReadServiceTests.Compose_resolves_alias_from_kind`: Docs `alias` expected `"grok-4.7"` |
| PC-6 | Custom row built from `new RolePolicyEntry()` | `RolePolicyReadServiceTests.Compose_lists_RoutableRoles_in_order_and_marks_Custom_unconfigured`: Custom `level` expected `Medium` |
| PC-7 | head = `Candidates[^1]` | `RolePolicyReadServiceTests.Compose_effective_prefers_the_first_stage_pin_candidate_and_excludes_card_pins`: `effective.agentKind` expected `Codex` |
| PC-8 | leave `modelLevel` null when the head has none | `RolePolicyReadServiceTests.Compose_fills_a_missing_pin_level_from_the_policy`: `modelLevel` expected `Medium` |
| PC-9 | default a missing head kind to ClaudeCode ignoring `row.kind` | `RolePolicyReadServiceTests.Compose_fills_a_missing_pin_kind_from_the_policy`: Grok case `alias` expected `"grok-4.7"` |
| PC-10 | fall through to `rolePolicy` when the pin has no head | `RolePolicyReadServiceTests.Compose_withholds_the_target_for_a_candidate_free_pin`: `source` expected `"pin"` |
| PC-11 | `defaultLabel` prints `kind` raw | M `rolePolicyModel C097 V05 null-kind-low`: expected `ClaudeCode/Low (haiku)` |
| PC-12 | `defaultLabel` ignores `configured` | M `rolePolicyModel C097 V05 unconfigured`: expected `not configured — DefaultLevel High (opus)` |
| PC-13 | `effectiveLabel` ignores `source` | M `rolePolicyModel C097 V06 role-policy`: expected `default → ClaudeCode/Low (haiku)` |
| PC-14 | `effectiveLabel` treats any non-`rolePolicy` source as a pin | M `rolePolicyModel C097 V06 unknown-source`: expected `unknown` |
| PC-15 | `escalateLabel` renders the timed form when minutes are null | M `rolePolicyModel C097 V24 escalate-manual`: expected `High (manual)` |
| PC-16 | `timeoutLabel` renders `0 min` | M `rolePolicyModel C097 V24 timeout-off`: expected `off` |
| PC-17 | section reads `level` from `AGENT_TASK_ROLES` | E `RolePolicySection C097 V08 rows-from-dto-not-mirror`: Mutation row expected `ClaudeCode/Low (haiku)` |
| PC-18 | section iterates `AGENT_TASK_ROLES` | same case: `role-policy-row-FutureRole` expected present, 15 rows |
| PC-19 | candidate rendered without `unavailableReason` | E `RolePolicySection C097 V25 stage-pin-column-and-effective`: held reason expected present |
| PC-20 | remove the section's error branch | E `RoutingSettingsTab C097 V09 role-policy-failure-keeps-siblings`: `Could not load role policy.` expected |
| PC-21 | remove `<RolePolicySection />` from the tab | E `RoutingSettingsTab C097 V10 section-order`: `role-policy-section` expected present |
| PC-22 | modal keeps `AGENT_TASK_ROLES` level when data is loaded | E `C097 V16 server-level-preselects`: `tier-High` expected |
| PC-23 | modal uses `row.level` when `stagePin` exists | E `C097 V17 stage-pin-wins`: `tier-High` expected |
| PC-24 | revert `agentTasks.ts:696` to `'Frontier'` | E `C097 V18 route-failure-falls-back-to-constant`: `tier-Low` expected |
| PC-25 | rethrow the query error in the modal | same case: `Goal` input expected present |
| PC-26 | floor applied to the constant only | E `C097 V27 orchestrator-floor-on-server-level`: `tier-High` expected |
| PC-27 | render no `TierBadge` until the query resolves | E existing `lets the role carry the tier rather than asking for one`: synchronous `tier-Low` expected |
| PC-28 | treat every defined kind as supported (drop the `DelegatableKinds` check) | `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` row `policy-kind-Raw`: `alias` expected `""`, read `"sonnet"` |
| PC-29 | write an undefined level as `High` | `RolePolicyEndpointTests.Get_serialises_every_row_when_one_role_has_unsupported_values`: Docs `level` expected null |
| PC-30 | build the pin target without the head guard | `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` row `pin-head-kind-Raw`: effective `agentKind` expected null |
| PC-31 | return `stagePin` unsanitised | same method, row `pin-head-kind-999`: serialisation expected to succeed |
| PC-32 | pass `EscalateTo` through unchecked | same method, row `policy-escalateTo-999`: serialisation expected to succeed |
| PC-33 | write an undefined `DefaultLevel` as `High` | `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` row `default-level-999`: `defaultLevel` expected null |
| PC-34 | `defaultLabel` prints `row.level` without the null branch | M `rolePolicyModel C097 V05 configured-level-null`: expected `unknown` |
| PC-35 | modal reads `row.level ?? 'High'` for `rolePolicy` | E `DelegateModal C097 V30 undefined-policy-level-falls-back-to-constant`: `delegate-tier-source-fallback` expected |
| PC-36 | `defaultLabel` maps a null kind to ClaudeCode unconditionally | M `rolePolicyModel C097 V05 configured-kind-undefined`: expected `unknown/Medium ()` |

### Out of scope

- S3 editor (`StagePinEditor`, `usePutRoutingPin`, `useClearRoutingPin`, V-7, V-11–V-15, R-3): held by O-1,
  Appendix A only.
- Stale-write 409 (O-3), card pins in the UI (O-4), a `RoutingPinsChanged` hub event (O-5), editing `RolePolicy`
  itself (D-7), the in-flight column (CARD-0506): not built, not tested here.
- The 15 s `refetchInterval` and the Routing tab subtitle copy: untested, as the matching `useRoutingPins` poll is.
- Whole `Antiphon.Tests`, whole Unit lane, E2E: no browser surface changes (D-10); the Routing tab has no E2E today.
- Live server activation after land (`GET /api/version`, Routing tab in a browser): a land/ops check, not a test.
- Package version pinning for NU1605: CARD-1229's; this card's Code tasks do not touch csproj/props.

### Checkpoints

Fresh census at the base, read from source: 116 client test files (114 under `src/` plus `scripts/proxy-trace.test.mjs`
and `scripts/serve.test.mjs`, both inside Vitest's default include), 1,049 `it/test` declarations, `.each`
expansion +127 (App 13; AgentReplyStyle 2+2+2; AgentsPage 2; StandingSessionRecovery 3; AttentionPanel 2;
attentionVisuals 6; CardFilePrivacy 2+3+2+2; CardHistory 3; TaskDetailBody 2; TaskDrawer 3; taskVisuals 3; TaskCard
9; ProjectConfig 2+3; ProjectSetupModal 2; dispatchConcurrencyModel 40+6+4+5+5+5+5+5+5+9) = **1,176 results**. One
`it.skipIf(!existsSync(dist/index.html))` in `App.test.tsx`; no `.skip/.todo/.only`. Recipe: `find client -name
'*.test.*' -o -name '*.spec.*'` outside `node_modules`; declaration regex
`^\s*(it|test)(\.(each|for|skipIf|skip|todo|only|concurrent|fails))?(<[^\n]+>)?\s*\(`; each-table rows counted at
depth 1 of the literal or the named `const` it references (`FIELD_CASES` = 4 fields × 10 values). Touched files:
`RoutingSettingsTab.test.tsx` 14, `routingSettingsModel.test.ts` 9, `DelegateModal.test.tsx` 19,
`agentTasks.test.tsx` 7 (0 `.each`). TUnit: no class named `RolePolicy*` exists under `tests/`, so the trailing
wildcard selects only the new classes; planned 3 + 6 methods, zero `[Arguments]`. A zero-result run is a wrong or
missing binding and fails the row.

New results: `rolePolicyModel.test.ts` 4+5+5 = 14, `RolePolicySection.test.tsx` 2, `RoutingSettingsTab.test.tsx`
+2, `DelegateModal.test.tsx` +4 = **22**; 2 new files → **118 files, 1,198 results**. Code confirms these bindings
and reports runtime counts; if unrelated additions land first, record a fresh delta census before running rather
than lowering a floor or dropping a name. Green Vitest runs print only the aggregate, so `Expect` is the aggregate.

Lane: default placement (server2, Linux); no `-Runner`, no `-Platform`. Rows CP-3–CP-5 need `tests/Antiphon.Tests`
to restore, which NU1605 prevents until CARD-1229 lands; run them after it, report them as not run until then.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | n/a | client-role-policy | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/rolePolicyModel.test.ts src/features/settings/RolePolicySection.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/routingSettingsModel.test.ts` | V-5, V-6, V-24, V-8, V-25, V-9, V-10, R-2 | 4 files, exactly 44 results (17 + 2 + 16 + 9), 0 failed/skipped, `CLIENT TESTS EXIT CODE: 0` | n/a | 5 | false |
| CP-2 | S4 | n/a | client-delegate-modal | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/DelegateModal.test.tsx src/api/agentTasks.test.tsx` | V-16, V-17, V-18, V-27, V-30, R-4 | 2 files, exactly 31 results (24 + 7), 0 failed/skipped, `CLIENT TESTS EXIT CODE: 0` | n/a | 5 | false |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c097-red/` | role-policy-red | `/*/*/RolePolicyEndpointTests/*` | V-1, V-2, V-3 red-first | 3 executed, 3 failed at the GET status assertion (404 ≠ 200), 0 skipped; intended red, test file committed before the endpoint | 3 | 8 | false |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c097-api/` | role-policy-api | `/*/*/RolePolicyEndpointTests/*` | V-1, V-2, V-3, V-29 | 4 executed, 0 failed/skipped | 4 | 8 | false |
| CP-5 | S1 | `CP-4` | role-policy-compose | `/*/*/RolePolicyReadServiceTests/*` | V-4, V-19, V-20, V-21, V-22, V-23, V-28 | 21 executed (6 + 15 V-28 rows), 0 failed/skipped | 21 | 3 | false |
| CP-6 | S4 | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | R-1, V-5–V-10, V-16–V-18, V-24–V-27 | 118 files, exactly 1198 results, 0 failed; skipped 0 when `client/dist` exists, else exactly 1 (the App bundle `skipIf`, report which); lint case green; `CLIENT TESTS EXIT CODE: 0` | n/a | 10 | true |

CP-6's `After` is S4 because S4 follows S2 by construction (S4 imports S2's files); S1 is not a prerequisite of
the client suite and S3 has no row. Run rows through the checkpoint tool (`dotnet run --project
tools/Antiphon.Checkpoints -- run --plan <this path> --rows <CP-list> --expected-source-sha <slice sha>`), one run
per committed slice group: `--rows CP-1` after S2, `--rows CP-2,CP-6` after S4, `--rows CP-3` at the S1 test-only
commit, `--rows CP-4,CP-5` at the S1 code commit. S1 R2 runs `--rows CP-4,CP-5` at its test-only commit (intended red: V-29 and the
12 V-28 rows other than the three controls) and again at its fix commit. S1 R3 runs `--rows CP-1,CP-2,CP-4,CP-5` at its
test-only commit (intended red: V-29, three V-28 rows, V-5 `configured-level-null`, `unconfigured-default-level-null`,
`configured-kind-undefined`; V-30 green, see its entry) and again at its fix commit. Builds and any direct driver go through
`scripts/build-slot.ps1`; slot exit 4 is a row not run. Preserve unedited `CHECKPOINT` lines; keep TRX/JSON/logs
ignored; delete owned `bin-c097-*` directories before settlement; Code and Review run
`scripts/check-evidence-diff.ps1` over the full task range.

### Cost

All times estimated. Ordinary V/R floor (Code) = CP-1 5 + CP-2 5 + CP-3 8 + CP-4 8 + CP-5 3 + CP-6 10 = **39
minutes** (two isolated `Antiphon.Tests` builds of ~6 min each are inside CP-3/CP-4; CP-5 reuses CP-4's output).
Setup allowance (worktree npm install on a fresh mirror, slot wait on an idle host) **5**; ordinary total **44**.
Authoring S1 45 + S2 50 + S4 30 + S5 20 = 145; Code budget **189 minutes** across four dispatches.

PC floor (Mutation, separate, pending): 10 TUnit PCs × 14 min (1 defect + 6 red build/run + 1 restore + 6 green
build/run) = 140, with PC-4 batched beside one `Compose` PC saving 14 → **126**; 17 Vitest PCs × 3 min (0.5 defect +
1 red + 0.5 restore + 1 green) = **51**; discovery/report allowance 10; **187 minutes**. Filters are the 27 exact
detectors above, never a class or file. Ordinary + PC = **226 minutes** plus authoring.

Savings: the alternative of a whole `Antiphon.Tests` run for S1 (25 min) plus a full client run after S2 and after
S4 (10 + 10) is 45 minutes against 39 here, **6 minutes saved**, mostly from the two class-scoped server rows (19)
replacing the assembly run. Holding S3 removes the plan's CP-5 (6 min) and 5 PCs from this round; they return
with Appendix A if S3 is released. No row is skipped to realize a larger saving.

## Appendix A — HELD: S3 stage-pin editor (deferred design, no checkpoint, no Code dispatch)

Held by O-1 until the operator confirms D-4/D-5 (an editable surface over Required pins is a fleet-wide routing
change per click). If released, this appendix becomes a new TestDesign slice with its own checkpoint row and the
IDs below keep their numbers. Files per the plan: `client/src/api/routingPins.ts` (`PutRoutingPinRequest`,
`usePutRoutingPin`, `useClearRoutingPin`, both invalidating `routingPinKeys.all` and `rolePolicyKeys.all`); new
`StagePinEditor.tsx`; `RolePolicySection.tsx` (Configure/Clear per row); `rolePolicyModel.ts` (`pinRequestFrom`,
`requiredConfirmCopy`, `fallbackOnClearCopy`).

- V-7 `C097 V07 $name` over `fallbackOnClearCopy(row)`: contains the role, `ClaudeCode/Low (haiku)` and the alias;
  unconfigured row names `DefaultLevel`. Mutation: omit the alias → red.
- V-11 `C097 V11 pin-request-carries-read-only-fields`: body keys exactly `role, provenance, strength, candidates,
  reason, forbiddenAliases, notBefore, notAfter, agentId`; `provenance === 'Human'`; `card` absent;
  `forbiddenAliases` equals the loaded pin's. Mutation: drop the carry-through → key missing → red.
- V-12 `C097 V12 required-needs-confirm`: Required → Save opens a confirm containing `routing_pin_conflict`;
  Preferred PUTs on the first click. Mutation: skip the confirm → PUT on first click → red.
- V-13 `C097 V13 422-lands-on-the-field`: msw 422 `{errors:{Candidates:[…]}}` → inline error under the candidates
  list; no second GET on either route. Mutation: swallow the error → red.
- V-14 `C097 V14 clear-after-confirm`: confirm text contains the V-7 copy; `DELETE /api/routing-pins/<id>` sent
  once. Mutation: DELETE before confirm → red.
- V-15 `C097 V15 write-invalidates-both-queries`: after PUT, msw records a second GET on `/api/role-policy` and
  `/api/routing-pins`. Mutation: invalidate pins only → red.
- R-3 `RoutingSettingsTab.test.tsx`, `routingSettingsModel.test.ts`, `ComplexityChainCellEditor.test.tsx` (10
  cases) unchanged.
- Held PCs: H-1 (V-11 carry-through), H-2 (V-12 confirm), H-3 (V-13 field error), H-4 (V-14 DELETE after confirm),
  H-5 (V-15 dual invalidation). Held checkpoint: one Vitest row over `StagePinEditor.test.tsx`,
  `src/api/routingPins.test.ts`, `rolePolicyModel.test.ts`, `RoutingSettingsTab.test.tsx`,
  `ComplexityChainCellEditor.test.tsx`, 6 minutes, counts to be frozen when released.
