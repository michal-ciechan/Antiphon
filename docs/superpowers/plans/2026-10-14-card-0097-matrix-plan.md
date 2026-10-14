# CARD-0097 plan (rework) — the Role × complexity matrix as the single routing surface

Task `c491a627` (Plan, Frontier); base `054795510c1249ffbf65c552ac4e6d3e87defb99` (the Investigate branch tip,
carrying [the investigation](../../investigations/2026-10-14-card-0097-role-x-complexity-matrix-vs-pins.md));
`origin/master` at start `8995111253ae0b54b66c2d480aee76508d265220`; branch `feat/card-task-c491a627`. Code read at
that base on 2026-10-14. Live reads ~06:25Z against the desktop server: `GET /api/version` (`893ddfcee`, capabilities
`land-v2`, `operator-shutdown-v1`), `GET /api/routing-pins` (13 stage-wide Human Required pins, no card pins, no
forbid lists, no dates), `GET /api/complexity-chains` (three any-role rows `resolvedFrom: none`, no role cells),
`GET /api/runner-defaults` (revision 2, global `server2`, Human), `GET /api/session-runners` (desktop windows 2 seats
occupied 2; server2 linux 10 seats occupied 8; server2-temp stale, draining), `GET /api/agent-tasks/pipeline?unscoped=true`
(0 in flight, 0 queued, 0 ready in all 14 stages). Lane: default placement (runner-defaults revision 2 puts everything
on server2); no `-Runner`, no `-Platform`; every checkpoint below is TUnit or Vitest and runs on Linux.

This plan supersedes CARD-0097 S3 (the `StagePinEditor` over pins, held in
[the 2026-10-13 test design](2026-10-13-card-0097-test-design.md) Appendix A). S1, S2, S4 and S5 of the 2026-10-13
plan are landed on master (`acfb1c325`, `7549067d6`, `47d807c14`, `00ec864ba`) and are built on, not changed.
The verification companion is [the test-design amendment](2026-10-14-card-0097-matrix-test-design.md).

## 0. Outcome

**The operator instruction (2026-10-14) is: "we should make sure the pins are set on Role x complexity matrix …
whenever we ask you to change routing we should be changing these."** The investigation confirmed three gaps, all
reproduced from source here:

1. **The matrix governs no dispatch today.** A cell is loaded only when a create carries `-Complexity`
   (`server/Application/Services/AgentTaskService.cs:921-932`); without it the create resolves through
   `ResolveLevel`/`ResolveAgentKind` (`:1068-1072`, `:3976-3986`, `:4004-4040`) and the pin overlay (`:800-835`). Zero
   board tasks since 2026-10-07 carried a complexity (investigation §2.3).
2. **The 13 operator routing changes are pins, not cells.** `RoutingPins` is a separate table the chain service never
   reads (`server/Application/Services/ComplexityChainService.cs:111-134` reads cells, any-role row and config only).
3. **A Required pin with a candidate replaces the cell outright** (`server/Application/Services/RoutingCandidates.cs:58-63`),
   so cells written today would still be bypassed, and the matrix UI says so only in a row banner
   (`client/src/features/settings/RoutingSettingsTab.tsx:461-473`) while every cell reads `Unset — dispatch blocks`
   (`client/src/features/settings/routingSettingsModel.ts:46`).

The fix is six slices (plus one split): make non-complexity creates walk the role row at a default tier (S-A), make the
effective cell say what will run and why (S-B), show that and edit a whole row from the matrix (S-C1, S-C2), give the
script a row verb (S-D), migrate the 13 pins into cells behind an operator-run dry run (S-E), and teach the generated
instructions and the docs the new precedence (S-F). Nothing here is active on the desktop server until it is restarted
onto a build that contains these commits (§7).

## 1. Ground truth: what the brief and the card assume versus what the code does

| Assumed | Code today | Evidence |
|---|---|---|
| A cell is consulted only when a create passes `-Complexity` | True. `if (request.Complexity is { } complexity)` opens the walk; the `else` at the end is `ResolveLevel`/`ResolveAgentKind` | `AgentTaskService.cs:921-932`, `:1068-1072` |
| Precedence card pin > stage pin > chain (role cell, any-role, config) > Blocked | True for `-Complexity`. `ResolveAsync` picks `cardPin ?? stagePin`; `LoadChainAsync` reads cell, any-role, config, else `([], null, "config", null, "none")` | `RoutingPinService.cs:306-309`; `ComplexityRoutingService.cs:312-344` |
| A Required pin with a candidate bypasses the chain; Preferred prepends | True. `pin.Applied && Required && pinSlots.Count > 0` returns the pin slots; otherwise pin slots are added first, then chain slots deduplicated | `RoutingCandidates.cs:58-74`; `RoutingPinCandidateCreateTests.Required_list_plus_complexity_bypasses_the_chain` |
| An unset cell under `-Complexity` Blocks the task | True: `routingExhausted` → `Status = Blocked`, `FailureReason` = the D3 sentence | `AgentTaskService.cs:1004-1016`, `:1448-1452`; `ComplexityCreateTests.All_cells_empty_blocks_with_the_D3_sentence_and_a_parent_note` |
| No `-Complexity` falls to RolePolicy with `DefaultLevel = High` | True; `Kind` null on every entry; Mutation is `Low` | `DelegationSettings.cs:349-372`, `:375` |
| `ComplexityChainService` composes nothing from pins | True: its constructor takes `AppDbContext`, `TimeProvider`, `ComplexityRoutingService`, settings, logger; no pin service | `ComplexityChainService.cs:28-40` |
| `PUT /api/complexity-chains/{role}/{complexity}` has no concurrency token | True: body is `{candidates, provenance, reason?, notAfter?}`; compare `expectedRevision` on runner defaults (409 on mismatch) | `ComplexityChainDtos.cs:6-10`; `RunnerDefaultSettingsService.cs:133-136` |
| The instructions snapshot has no matrix section | True: sections 1–7 are caps, hosts, defaults, holds, pins, levels, standing; the builder reads `RoutingPins` and never `ComplexityChains` | `OrchestratorInstructionsRenderer.cs:111-180`; `OrchestratorInstructionsSnapshotBuilder.cs:204-247` |
| A cell write regenerates the instructions file | False: `RoutingPinService` fires `OrchestratorInstructionsSignal` on upsert/clear; `ComplexityChainService` has no `IServiceProvider` and fires nothing | `RoutingPinService.cs:48-49`, `:270`, `:287`; `ComplexityChainService.cs:28-40` |
| `complexity-chain.ps1 set` writes one cell per call and cannot touch pins | True: `-Complexity` is `ValidateSet('Hard','Medium','Easy')`; one PUT per call; `routing-pin.ps1 clear` looks the stage pin up by grain and DELETEs by id | `scripts/complexity-chain.ps1:30-31`, `:204-226`; `scripts/routing-pin.ps1:273-291` |
| The 13 live pins are Plan/TestDesign ClaudeCode/Frontier, Code ClaudeCode/High, Review Codex/Frontier, nine others ClaudeCode/Frontier then ClaudeCode/High | Confirmed by the live read (ids in §6 S-E). Custom has no pin. All `strength Required`, `provenance Human`, empty `forbiddenAliases`, null dates and agent | live `GET /api/routing-pins` ~06:25Z |
| Queued chain tasks re-walk the live matrix when their alias is held | True for list-governed tasks (`Complexity` or `RoutingPinId` set); a single-candidate Required pin is never rerouted by a tick | `AgentTaskDispatcher.cs:716-745`, `:1749-1778`, `:1791-1792`; `ComplexityRoutingService.cs:31-32` |
| A reroute ends matrix governance | True: `task.Complexity = null; task.RoutingPinId = null` | `AgentTaskService.cs:2813-2816`; `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` |
| The settlement Commit delegate goes through `CreateAsync` | False: `CreateCommitTaskAsync` resolves kind/level itself from RolePolicy plus the Commit pin via `RoutingCandidates.Compose` with `chain: null` | `AgentTaskService.cs:3811`, `:3833-3860` |
| CARD-0097 S3 is still a pin editor waiting for O-1 | Superseded: this plan's S-C2 edits cells and clears the stage pin; `StagePinEditor.tsx`, `usePutRoutingPin`, `pinRequestFrom`, `requiredConfirmCopy` are not built | 2026-10-13 test design Appendix A |
| CARD-0506 collides on `client/src/features/settings` | S1–S4 landed (`f43fc6103` … `6e5dae6d5`); only S5 (E2E in `tests/Antiphon.E2E`) is unlanded; the Concurrency tab files exist on master and are not touched here | `git log origin/master`; `ConcurrencySettingsTab.tsx` present |
| The live server runs master | False: `893ddfcee`, 216 commits behind master at the investigation (more now). S1/S2 of CARD-0097 and everything here are inactive until a restart | live `GET /api/version` |

## 2. Decisions

Design decisions (D-n) are the plan's; operator decisions (OD-n) are recorded under the brief's recommended defaults so
the operator can overrule any one before Code is dispatched.

- **D-1 Stamp `Complexity` at create instead of teaching `ResolveLevel`/`ResolveAgentKind` to read cells.** When a
  create has no `Complexity`, no explicit `AgentKind`/`ModelLevel`, no `AgentId`, no `IgnoreModelDisabled`, is a
  `Worker` kind, a non-specialist role, not a live follow-up, and `LoadChainAsync(role, DefaultComplexity)` resolves to
  anything but `none`, S-A sets `request = request with { Complexity = _settings.DefaultComplexity }` immediately
  before the complexity validation block (`AgentTaskService.cs:736`). Everything downstream is unchanged: the pin
  decision (`:769-779`), the walk (`:921-932`), the Required-pin bypass warning (`:933-941`), the Blocked path
  (`:1448-1452`), the stored `Complexity` (`:1364`), the dispatcher re-walk (`AgentTaskDispatcher.cs:1749-1778`), the
  attention grouping and the reroute. Rejected: reading cells inside `ResolveLevel` (the single-pair path at
  `:1068-1110` applies the forbid list, quota and hold to one pair only; the walker's four skips, re-walk and Blocked
  machinery would all need a second copy). Rejected: an instruction-only "always pass `-Complexity`" (silently falls to
  RolePolicy whenever an orchestrator forgets, which is every dispatch today).
- **D-2 The fail-open guard is the resolved source, not the cell's existence.** The stamp happens only when
  `LoadChainAsync` returns `ResolvedFrom` ≠ `none` (role cell, any-role row or a non-empty config default). An unset
  row therefore keeps today's RolePolicy path and never Blocks a plain dispatch; the `Unset — dispatch blocks` wording
  stays true only for explicit `-Complexity` creates. Cost: one or two `FindActiveAsync` queries per create.
- **D-3 Excluded from stamping, each for a named reason.** Explicit `AgentKind`/`ModelLevel` (an explicit pair is a
  single candidate the caller chose, `:736-746`); `AgentId` (a standing agent settles kind by itself, `:846-860`);
  `IgnoreModelDisabled` (422 with complexity, `:749-756`); `AgentTaskKind.Orchestrator` (the `MinOrchestratorLevel`
  clamp lives in `ResolveLevel` `:3981-3984` and not in `ResolveAgainstRolePolicy` `:432-446`; OD-7); specialist roles
  (`ValidateCellRole` refuses them and `LoadChainAsync` would return empty, `ComplexityChainService.cs:264-269`); live
  follow-ups (`liveFollowUp`, `:395`, `:485`: their inherited kind/level is the request). The settlement Commit
  delegate (`CreateCommitTaskAsync`, `:3833-3860`) is brought under the same rule in S-A: when the Commit row resolves,
  it walks `WalkAsync(DefaultComplexity, Worker, Commit, pinDecision, …)` and takes `Chosen`, else keeps today's
  resolution.
- **D-4 The effective cell DTO is computed exactly as create would compute it.** S-B calls
  `RoutingPinService.ResolveAsync(null, role, Ask(null, null, null, false))` for the stage pin, then
  `RoutingCandidates.Compose(decision, chain, label, null, null, ResolveAgainstRolePolicy)` — the same two calls
  `WalkAsync` makes (`ComplexityRoutingService.cs:212-219`). When the composed list is empty, the effective candidate is
  the RolePolicy pair with source `rolePolicy`. `ComplexityChainService` gains an optional `RoutingPinService? pins`
  constructor parameter (DI resolves it; the three test files that construct the service directly keep compiling:
  `ComplexityChainServiceTests.cs:303-312`, `ComplexityChainRoleTests`, `MutationAdmissionTests`). No cycle:
  `RoutingPinService` depends on `ComplexityRoutingService` only (`RoutingPinService.cs:36-46`). Rejected: duplicating
  the precedence in a new composer (a second copy of `Compose` drifts; the whole point is that the cell displays the
  rule the walker applies).
- **D-5 Effective data rides on the existing `?role=` view; the any-role list view is unchanged.** `ComplexityChainDto`
  gains an optional `effective` (null on the list view and on every any-role row) and `ComplexityChainListDto` gains
  `defaultComplexity`. The client already runs one `?role=` query per row (`complexityChains.ts:70-77`). Rejected: a
  new route (`GET /api/routing-matrix`) — one more query per row for the same rows.
- **D-6 The row editor replaces the held S3 pin editor; clearing the stage pin is part of the same action.** S-C2's
  `ComplexityChainRowEditor` writes Hard, Medium and Easy with three sequential `PUT`s through `usePutComplexityChain`
  and, when a stage pin exists and the "Clear the stage pin" checkbox is on (default on), sends
  `DELETE /api/routing-pins/{id}` after the third PUT succeeds. A PUT failure stops the sequence and reports which
  tiers were written. The only pin write the client gains is that DELETE (`useClearRoutingPin`). `RolePolicySection`'s
  copy "pins are set with scripts/routing-pin.ps1" (`RolePolicySection.tsx:47-50`) becomes a Configure button per row
  that opens the row editor. Rejected: a Strength radio or Required confirm (a cell has no strength; OD-2).
- **D-7 The script gets `-Complexity All` and `-ClearStagePin`, not a new script.** `complexity-chain.ps1 set -Role
  Review -Complexity All -Candidates Codex/Frontier [-ClearStagePin]` issues the three PUTs, then (with the switch) the
  GET-by-role and DELETE that `routing-pin.ps1 clear` already does by grain (`scripts/routing-pin.ps1:273-291`).
  `clear -Complexity All` clears the three cells. `get -Role X` prints the S-B overlay (`effective.source` and its
  list) beside the stored cell. `routing-pin.ps1` stays for card pins, forbid lists, dates and agents. Rejected:
  teaching `routing-pin.ps1` to write cells (its grain has no complexity axis and its body carries fields a cell cannot
  hold).
- **D-8 Migration is a script with a dry run, a backup and an explicit apply, never a startup hook.** S-E's
  `scripts/migrate-stage-pins-to-cells.ps1` reads the live pins, prints the per-role diff (pin candidates → the three
  cells; what the reason will be; which pins it refuses to migrate because they carry a forbid list, a date or an
  agent), writes a JSON backup of the pins it will touch, and does nothing else unless `-Apply` is given. `-ClearPins`
  (with `-Apply`) deletes each migrated pin only after its three PUTs returned 200. `-Restore <backup.json>` re-PUTs the
  pins from the backup and DELETEs the cells it wrote. Rejected: migrating in `Program.cs` on start (destroys the
  operator's recorded instruction with nobody watching, and the live server is not even on this code).
- **D-9 The instructions file gets a matrix section before the pins section, and cell writes regenerate it.**
  Section numbering is asserted literally in `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines`
  (`:34-62`), so the renumbering (matrix 5, pins 6, levels 7, standing 8) edits that test's literals with that reason.
  Matrix rows are bounded (14 roles × 1 line) and sit outside the truncation ladder (`Renderer.cs:44-85`), which drops
  card pins, holds, standing lines and runner features in that order; the delta gains `DiffMatrix` beside `DiffPins`
  (`OrchestratorInstructionsDelta.cs:20-26`) so an unchanged write neither rewrites nor notifies. `ComplexityChainService`
  gains an optional `IServiceProvider? services` and fires `OrchestratorInstructionsSignal.Fire(services, $"complexity-chain {role}/{tier}")`
  on upsert and clear, as the pin service does (`RoutingPinService.cs:48-49`).
- **D-10 No new hub event.** Cells and pins poll at 15 s (`complexityChains.ts:66`, `routingPins.ts:52`) and every
  client write invalidates both query families. Same reasoning as the 2026-10-13 plan D-9.
- **D-11 Lane and tests.** Narrow class filters for TUnit (one class per new behaviour group), file lists for Vitest,
  no whole-`Antiphon.Tests` lane, no E2E (the Routing tab has none). Checkpoints are the closed list in §5.

Operator decisions, written under the recommended default:

- **OD-1 Default tier for a dispatch without `-Complexity`: `Delegation:DefaultComplexity = Medium`.** New setting,
  validated as a defined `TaskComplexity`. Alternatives: `Hard` (every plain dispatch walks the top-cost row; the
  existing `ComplexityCreateTests.No_complexity_is_unchanged_when_fable_is_held` seeds a Hard any-role chain and would
  change meaning); require `-Complexity` on every dispatch (instruction-only; a forgotten flag silently falls to
  RolePolicy); a fourth "default" column per role (schema, DTO and UI column, and still a tier to name).
- **OD-2 A cell does not refuse a disagreeing explicit `-Kind`/`-Level`; it is simply not consulted (explicit path),
  and the create carries a warning naming the cell it bypassed.** Required pins remain the refusal mechanism
  (`RoutingPinService.cs:348-358`, 409 `routing_pin_conflict`). Cost: after S-E clears the 13 pins, `delegate.ps1 -Role
  Code -Kind Codex` runs Codex with a warning instead of today's 409. Alternative: a per-row `strength` with Required
  semantics (schema plus a 409 on the explicit path; the 422 for `-Complexity + -Kind` is the model) — a separate card
  if wanted.
- **OD-3 The 13 pins are migrated and cleared only by an explicit operator command after the dry run.** Nothing in
  S-A..S-F clears a pin on its own; until S-E `-Apply -ClearPins` runs, every cell is displayed beneath the pin that
  overrides it (S-B/S-C1) so the matrix never lies. Alternative: clear at migration time automatically (loses the
  operator's recorded reasons without a backup in hand).
- **OD-4 `PUT /api/complexity-chains/{role}/{tier}` stays last-writer-wins, stated and tested.** V-B6 writes the same
  cell twice and asserts the second body is what `GET` returns with a later `updatedAt` and no 409. The row editor
  refetches the row when opened, shows `updatedAt`, and invalidates on every write. Alternative: `expectedUpdatedAt` →
  409 `complexity_chain_stale` (changes the script, the migration, the UI and every orchestrator write; separate card,
  the 2026-10-13 plan's O-3).
- **OD-5 Card pins stay card-level exceptions, read-only in the matrix.** They are listed beneath the table as today
  (`RoutingSettingsTab.tsx:568-598`); S-C1 adds a per-row count ("2 card pins") in the role name cell. Alternative:
  rendering each card pin inside the three cells (noise for a card-scoped rule that applies to one card).
- **OD-6 A stamped create whose row is exhausted is Blocked with the chain sentence, not refused.** That is the
  CARD-0090 contract for every chain walk (`AgentTaskService.cs:1448-1452`), the dispatcher requeues it when a hold
  clears (`ComplexityDispatcherTests.Clearing_a_hold_requeues_a_routing_blocked_task`) and the attention feed names the
  cell. Cost: a plain dispatch whose only listed alias is held becomes a Blocked task instead of today's 409
  `model_disabled`. Alternative: force `RefuseIfExhausted` on stamped creates (409 `routing_exhausted`; no auto-resume;
  orchestrators learn a new refusal code on every ordinary dispatch).
- **OD-7 Orchestrator-kind creates are not stamped.** They keep RolePolicy plus the `MinOrchestratorLevel` clamp.
  Alternative: stamp them and apply the clamp inside the walker (the walker already drops non-Claude candidates for
  orchestrators, `ComplexityRoutingService.cs:500-504`, but has no level floor).

## 3. Slices

| Slice | What | Files (closed list) | Est. |
|---|---|---|---|
| **S-A Server: the role row governs every dispatch** | `DelegationSettings.DefaultComplexity` (`TaskComplexity`, default `Medium`, validator failure for an undefined value). In `CreateAsync`, before `:736`: the D-1/D-3 guard, then `request = request with { Complexity = default }` and `complexityDefaulted = true`. Created event detail: `"{Kind}/{Role} at {level} (matrix default {tier}) in …"` instead of `(role policy)` when stamped (`:1554-1560`). `CreateCommitTaskAsync` (`:3833-3860`): when `LoadChainAsync(Commit, default)` is not `none`, walk it and take `Chosen`. | `server/Application/Settings/DelegationSettings.cs` (new property beside `:375`; validator beside `:1325`), `server/Application/Services/AgentTaskService.cs` (`:736`, `:1554-1560`, `:3833-3860`). Tests: new `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` (V-A1–V-A8; fixture copied from `ComplexityCreateTests.cs:470-496`), existing `ComplexityCreateTests`, `RoutingPinCreateTests`, `RoutingPinCandidateCreateTests` run unchanged (R-A). | 75 min |
| **S-B Server: effective cell DTO carries the overlay** | `ComplexityEffectiveDto { source, candidates, stagePin, governsDefault }` on `ComplexityChainDto.Effective` (nullable, `?role=` view only); `ComplexityChainListDto.DefaultComplexity`. `ComplexityChainService` takes `RoutingPinService? pins = null`; `GetEffectiveAsync` composes per D-4 through a new pure static `ComplexityEffective.Compose(decision, chain, chainLabel, rolePolicyPair, isDefaultTier)`. V-B6 states last-writer-wins. | `server/Application/Dtos/ComplexityChainDtos.cs` (`:26-46`), `server/Application/Services/ComplexityChainService.cs` (`:28-40`, `:111-134`), new `server/Application/Services/ComplexityEffective.cs`. Tests: new `tests/Antiphon.Tests/Application/ComplexityEffectiveComposeTests.cs` (unit, V-B1–V-B4), new `tests/Antiphon.Tests/Application/ComplexityEffectiveHttpTests.cs` (reuses `ComplexityChainApiWebAppFactory` from `ComplexityChainHttpTests.cs:142`; V-B5, V-B6); `ComplexityChainRoleHttpTests`, `ComplexityChainServiceTests` run unchanged (R-B). | 60 min |
| **S-C1 Client: cells show the overlay** | `ComplexityChainDto.effective?`, `ComplexityChainListDto.defaultComplexity?` types. `MatrixCell` renders the effective list first with a source badge (`Required Human pin → Codex/Frontier (gpt-6-astra)` red; `Falls back to RolePolicy → ClaudeCode/High (opus)` grey; `Own rule`/`Inherits Any role`/`Configuration fallback` as today) and the stored cell beneath it; the default-tier column header reads `Medium (default for dispatches without -Complexity)`; an unset non-default cell reads `Unset — a -Complexity <tier> dispatch blocks`. `NON_COMPLEXITY_BOUNDARY` and `pinEffectCopy` rewritten; `cardPinCountLabel(role, pins)` (OD-5). msw default handler gains `defaultComplexity: 'Medium'`. | `client/src/api/complexityChains.ts` (`:19-38`), `client/src/features/settings/routingSettingsModel.ts` (`:20-26`, `:32-47`, `:113-119`), `client/src/features/settings/RoutingSettingsTab.tsx` (`:409-543`), `client/src/test/mocks/handlers.ts` (`:76-82`). Tests: `routingSettingsModel.test.ts` (V-C1–V-C3), `RoutingSettingsTab.test.tsx` (V-C4–V-C6; the three cases at `:347-360`, `:373-385`, `:447-458` are rewritten to the new copy with that reason), `complexityChains.test.tsx` (R-C1). | 60 min |
| **S-C2 Client: whole-row editor that can clear the stage pin** | New `ComplexityChainRowEditor.tsx` (Modal; one candidate list reused from `ComplexityChainCellEditor`'s draft/validation; Reason; "Clear the stage pin <X>" checkbox shown only when a stage pin exists, default on; confirm copy `rowReplaceCopy(role, pin)`; three sequential PUTs then optional DELETE; partial-failure report). `useClearRoutingPin()` in `routingPins.ts` (invalidates `routingPinKeys.all`, `rolePolicyKeys.all`, `complexityChainKeys.all`). `RoutingSettingsTab` holds the row-editor target; `RoleMatrixRow` gets a "Configure row" button; `RolePolicySection` gets `onConfigure?: (role) => void` and a Configure button per row, copy `:47-50` rewritten. | new `client/src/features/settings/ComplexityChainRowEditor.tsx`, `client/src/api/routingPins.ts` (`:44-54`), `client/src/features/settings/routingSettingsModel.ts` (`rowReplaceCopy`, `rowCellRequests`), `client/src/features/settings/RoutingSettingsTab.tsx`, `client/src/features/settings/RolePolicySection.tsx` (`:43-71`). Tests: new `ComplexityChainRowEditor.test.tsx` (V-C7–V-C11), new `client/src/api/routingPins.test.ts` (V-C12), cases in `routingSettingsModel.test.ts` (V-C13), `RolePolicySection.test.tsx` (V-C14); `ComplexityChainCellEditor.test.tsx` unchanged (R-C2). | 90 min |
| **S-D Script: one verb for a role-wide change** | `complexity-chain.ps1`: `-Complexity` accepts `All` for `set` and `clear` (three calls, Hard→Medium→Easy, stop on the first failure and say which tiers were written); `set -ClearStagePin` (GET `/api/routing-pins?role=<Role>`, DELETE the `cardId == null` row after the PUTs; refuses with `-Role Any`); `get -Role X` prints `effective: <source> <list>` from S-B beside the stored line. Header and `routing-pin.ps1` header point role-wide changes at this script. ASCII-only. | `scripts/complexity-chain.ps1` (`:9-13`, `:30-34`, `:155-184`, `:186-236`), `scripts/routing-pin.ps1` (`:1-28` header), `docs/ops-http.md` (new row beside `:139`), `docs/antiphon-api.md` (`:697-712`). Verification: M-2 (manual transcript against the local stack in the Code report; the script has no test runner). | 45 min |
| **S-E Migration (operator-run, once, reversible)** | New `scripts/migrate-stage-pins-to-cells.ps1 [-Role <R>] [-Apply] [-ClearPins] [-Backup <path>] [-Restore <backup.json>]`. Default is a dry run: table of `role | pin id | pin candidates | cells to write (Hard/Medium/Easy) | reason | action`; pins with `forbiddenAliases`, `notBefore`, `notAfter` or `agentId` are listed as `manual` and never touched; card pins are ignored. Backup JSON written before any write. `-Apply` PUTs the three cells per role with `provenance Human`, `reason = "<pin reason> (migrated from stage pin <id> on <date>)"`; `-ClearPins` DELETEs a pin only after its three PUTs returned 200. `-Restore` re-PUTs the backed-up pins (same body) and DELETEs the cells the backup lists. Runbook paragraph in `docs/orchestration-loop.md`. | new `scripts/migrate-stage-pins-to-cells.ps1`, `docs/orchestration-loop.md` (beside `:941-948`). Verification: M-3 (dry-run transcript against the live server in the Code report; the GET/PUT/DELETE routes it uses exist on the live build). Not run by Code; the operator runs `-Apply` after reading the diff (OD-3). | 60 min |
| **S-F Instructions and docs** | Snapshot: `MatrixSection(DefaultComplexity, Rows)` with one `MatrixInstructionRow(Role, Hard, Medium, Easy, Effective, Source)` per routable role from `ComplexityChainService.ListAsync(role)`; renderer section `## 5. Role × complexity matrix` (owner `GET /api/complexity-chains`; one line per role: `- Plan: Hard …; Medium (default) …; Easy …; effective Codex/Frontier (gpt-6-astra) via stage pin`), pins → 6, levels → 7, standing → 8; `DiffMatrix`; `ComplexityChainService` fires the signal (D-9); `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService`. Docs: `docs/orchestration-loop.md:880-913` ("Routing pins beat RolePolicy" becomes "The matrix governs every dispatch; a pin is the exception"), `:941-948` (the row editor can clear a stage pin), `:950-957` (default tier), `docs/antiphon-api.md:697-712` (`effective`, `defaultComplexity`, last-writer-wins), `docs/ops-http.md` row, `.claude/skills/antiphon-delegate/SKILL.md:147` (one sentence: omitted `-Complexity` means the default tier). | `server/Application/Services/OrchestratorInstructionsSnapshot.cs`, `OrchestratorInstructionsSnapshotBuilder.cs` (`:29-40`, new `ReadMatrixAsync`), `OrchestratorInstructionsRenderer.cs` (`:111-180`), `OrchestratorInstructionsDelta.cs` (`:20-26`), `ComplexityChainService.cs` (`:28-40`, `:228-232`, `:248-252`), `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (`:173`), the docs above. Tests: `OrchestratorInstructionsRendererTests` (V-F1, literal renumbering in `:34-62` and the `Sample()` fixture), `OrchestratorInstructionsRefreshTests` (V-F2: `[Arguments("complexity-chain")]` and a `CoveredWriteAsync` arm at `:351-392`), `OrchestratorInstructionsDeltaTests` (existing file; V-F3). | 75 min |

Order: S-A and S-B are independent and may run in parallel (different files; both touch nothing the other does).
S-C1 needs S-B landed (wire contract F-A). S-C2 needs S-C1. S-D needs S-B for `get`. S-F needs S-A (the default tier)
and S-B (the effective list) landed. S-E is not a Code dispatch that changes behaviour by itself; its `-Apply` is only
meaningful once S-A is **active on the live server** (§7) — before that the migrated cells route nothing and the
cleared pins would stop governing, so the migration runbook says so in its first line.

Commit and push after each slice. One Code task per slice, each in its own `-Worktree`, default placement.

**Collisions.** CARD-0097 landed S1/S2/S4/S5: S-B does not modify `RolePolicyReadService` (it is read, not reused: its
`Effective` projects the pin head and never composes the chain); S-C2 edits `RolePolicySection.tsx` (a button and one
sentence) and S-C1 edits `handlers.ts` (one field on the existing complexity-chains handler) — both landed files, no
in-flight task touches them (pipeline route: 0 in flight at ~06:25Z). CARD-0506: S1–S4 are on master; S5 is
`tests/Antiphon.E2E` only; no file overlap. The held CARD-0097 S3 (`StagePinEditor.tsx`, `usePutRoutingPin`,
`pinRequestFrom`, `requiredConfirmCopy`) is withdrawn by D-6; its Appendix A tests are superseded by V-C7–V-C14.
Shared-area rule: before dispatching S-C1/S-C2, the orchestrator re-reads `GET /api/agent-tasks/pipeline?unscoped=true`
for any Code task touching `client/src/features/settings`.

## 4. Edge cases (each bound to a test in the amendment)

- Unset row at the default tier → RolePolicy path, Queued, no Blocked event, Created event says `(role policy)` (V-A2).
- Set row, every candidate held → Blocked with the chain sentence naming `<Role>/Medium`; attention groups it under
  `chain:<Role>/Medium`; clearing the hold requeues it (V-A3, R-A dispatcher rows).
- Explicit `-Kind` or `-Level` with a set row → not stamped; warning names the bypassed cell; Required pin 409 unchanged
  (V-A4, R-A `RoutingPinCreateTests.An_explicit_kind_against_a_required_pin_is_409_routing_pin_conflict`).
- Stage Required pin plus a set row → pin wins, `complexity chain bypassed by …` warning, `RoutingPinId` stamped
  (V-A5, mirrors `ComplexityCreateTests.A_required_pin_plus_complexity_uses_the_pin_pair_and_warns_bypass`).
- Specialist role (Check) with an any-role Medium row → not stamped (V-A6).
- Orchestrator kind with a set Plan row → not stamped, `MinOrchestratorLevel` clamp still applies (V-A7).
- Settlement Commit delegate with a Commit/Medium cell → takes the cell's first available candidate (V-A8).
- `DefaultComplexity` undefined in configuration → validator failure naming the key (V-A9).
- Effective DTO: Required pin → `source pin`, pin candidates only; Preferred pin → `pin+chain`, pin first then cell
  deduplicated; no pin, cell → `role`/`any`/`config`; nothing → `rolePolicy` with the RolePolicy pair; a card pin is
  never composed; `governsDefault` true only on the default tier (V-B1–V-B5).
- Two PUTs on one cell → second wins, `updatedAt` later, no 409 (V-B6).
- Row editor: three PUTs in tier order; a 422 on Medium stops before Easy and the form names `Hard written, Medium
  failed`; DELETE pin only after all three succeed; checkbox hidden without a pin (V-C7–V-C11).
- Script `-Complexity All` stops on the first failing tier and prints which were written (M-2).
- Migration refuses a pin with a forbid list, a date or an agent; dry run writes nothing; `-ClearPins` without `-Apply`
  is a usage error (M-3).
- Instructions: an identical cell rewrite does not bump the version (V-F3); a cell write regenerates and notifies
  (V-F2).

## 5. Verification design

Full V/R/PC binding, fixtures and the wire contract are in
[the amendment](2026-10-14-card-0097-matrix-test-design.md). IDs are stable across both files.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S-A tests committed, production not yet | `tests/Antiphon.Tests -> bin-c097m-red/` | matrix-default-red | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A9 | 9 methods run; V-A1, V-A3, V-A5, V-A8, V-A9 fail (intentional red: no `DefaultComplexity`, no stamp); V-A2, V-A4, V-A6, V-A7 may pass (they assert today's path is kept) | 9 | 9 | false |
| CP-2 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` | matrix-default | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A9 | 9 methods, 0 failed/skipped | 9 | 9 | false |
| CP-3 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` (reused) | matrix-default-regression | `/*/*/ComplexityCreateTests/*` | R-A1 | all 14 methods, 0 failed/skipped | 14 | 8 | false |
| CP-4 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` (reused) | pin-create-regression | `/*/*/RoutingPinCreateTests/*` | R-A2 | all 13 methods, 0 failed/skipped | 13 | 8 | false |
| CP-5 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` (reused) | pin-list-create-regression | `/*/*/RoutingPinCandidateCreateTests/*` | R-A3 | all 17 methods, 0 failed/skipped | 17 | 8 | false |
| CP-6 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` (reused) | dispatcher-regression | `/*/*/ComplexityDispatcherTests/*` | R-A4 | all 9 methods, 0 failed/skipped | 9 | 8 | false |
| CP-7 | S-B tests committed, production not yet | `tests/Antiphon.Tests -> bin-c097m-bred/` | effective-red | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4 | compile-only stub of `ComplexityEffective.Compose` returning an empty list; 4 methods run, 4 fail | 4 | 6 | false |
| CP-8 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` | effective-compose | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4 | 4 methods, 0 failed/skipped | 4 | 6 | false |
| CP-9 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` (reused) | effective-http | `/*/*/ComplexityEffectiveHttpTests/*` | V-B5, V-B6 | 2 methods, 0 failed/skipped | 2 | 6 | false |
| CP-10 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` (reused) | chain-http-regression | `/*/*/ComplexityChainRoleHttpTests/*` | R-B1 | all 3 methods, 0 failed/skipped | 3 | 5 | false |
| CP-11 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` (reused) | chain-service-regression | `/*/*/ComplexityChainServiceTests/*` | R-B2 | all 10 methods, 0 failed/skipped | 10 | 5 | false |
| CP-12 | S-C1 | n/a | client-overlay | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-C1–V-C6, R-C1 | 3 files run; aggregate = 9 + 16 + 7 existing, less the 3 rewritten, plus the new cases the amendment lists, 0 failed/skipped | n/a | 5 | false |
| CP-13 | S-C2 | n/a | client-row-editor | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ComplexityChainRowEditor.test.tsx src/api/routingPins.test.ts src/features/settings/routingSettingsModel.test.ts src/features/settings/RolePolicySection.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx` | V-C7–V-C14, R-C2 | 5 files run, 0 failed/skipped | n/a | 6 | false |
| CP-14 | S-F | `tests/Antiphon.Tests -> bin-c097m-f/` | instructions-render | `/*/*/OrchestratorInstructionsRendererTests/*` | V-F1 | all 6 methods (8 executed results: one method carries 3 `[Arguments]`), 0 failed/skipped | 8 | 6 | false |
| CP-15 | S-F | `tests/Antiphon.Tests -> bin-c097m-f/` (reused) | instructions-refresh | `/*/*/OrchestratorInstructionsRefreshTests/*` | V-F2 | all 11 methods (17 executed results: `Covered_writes_each_regenerate_and_notify` carries 7 `[Arguments]` after V-F2), 0 failed/skipped | 17 | 10 | true |
| CP-16 | all client slices | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | lint gate (`client/src/lint.gate.test.ts`), whole client suite | `CLIENT TESTS EXIT CODE: 0` | n/a | 10 | true |
| CP-17 | S-F | `tests/Antiphon.Tests -> bin-c097m-f/` (reused) | instructions-delta | `/*/*/OrchestratorInstructionsDeltaTests/*` | V-F3 | all 5 methods (4 today + V-F3), 0 failed/skipped | 5 | 4 | false |

Lane: every row runs on the default placement (runner-defaults revision 2 = server2); no `-Runner`, no `-Platform`.
TUnit rows run one class filter each through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints --
run --plan <this file>`), which takes the build slot itself; `Antiphon.Tests` is never run whole here. Method counts
in `Expect` are the counts read from source on 2026-10-14 (`ComplexityCreateTests` 14, `RoutingPinCreateTests` 13,
`RoutingPinCandidateCreateTests` 17, `ComplexityDispatcherTests` 9, `ComplexityChainRoleHttpTests` 3,
`ComplexityChainServiceTests` 10, `OrchestratorInstructionsRendererTests` 6 methods, one expanded by 3 `[Arguments]`,
`OrchestratorInstructionsRefreshTests` 11 methods, one expanded by 6 `[Arguments]` today and 7 after V-F2,
`OrchestratorInstructionsDeltaTests` 4); Code
reports the executed count the runner prints. Green Vitest runs print only the aggregate line. S-D and S-E have no
automated row: M-2 and M-3 are transcripts in the Code report.

### Positive controls (pending; one per behaviour; Mutation is not run in this card)

| PC | Exact method | Case → mutation → expected red |
|---|---|---|
| PC-1 | `MatrixDefaultComplexityCreateTests.A_plain_create_walks_the_role_row_at_the_default_tier` | Plan/Medium cell `[Codex/Frontier]` → skip the stamp → `ClaudeCode/Frontier` from RolePolicy |
| PC-2 | `MatrixDefaultComplexityCreateTests.An_unset_row_keeps_the_role_policy_path` | no rows → stamp unconditionally → `Blocked` |
| PC-3 | `MatrixDefaultComplexityCreateTests.An_exhausted_default_row_blocks_with_the_cell_name` | all held → force `RefuseIfExhausted` → `RoutingExhaustedException` |
| PC-4 | `MatrixDefaultComplexityCreateTests.An_explicit_kind_is_not_stamped_and_warns` | `-Kind Codex` with a Plan row → stamp anyway → 422 `complexity cannot be combined` |
| PC-5 | `MatrixDefaultComplexityCreateTests.A_required_stage_pin_still_wins_over_the_row` | Required pin + row → drop the pin from `WalkAsync` → row candidate chosen, no bypass warning |
| PC-6 | `MatrixDefaultComplexityCreateTests.A_specialist_role_is_never_stamped` | Check with any-role Medium row → stamp → `ValidateCellRole`-shaped failure / `Blocked` |
| PC-7 | `MatrixDefaultComplexityCreateTests.An_orchestrator_create_keeps_role_policy_and_the_level_floor` | Orchestrator + Plan/Medium `[ClaudeCode/Low]` → stamp → `Low` ≠ `MinOrchestratorLevel` |
| PC-8 | `MatrixDefaultComplexityCreateTests.The_settlement_commit_delegate_takes_the_commit_row` | Commit/Medium `[Codex/Frontier]` → keep `ResolveAgentKind` only → `ClaudeCode` |
| PC-9 | `MatrixDefaultComplexityCreateTests.An_undefined_default_complexity_fails_validation` | `(TaskComplexity)999` → drop the validator line → `Succeeded` |
| PC-10 | `ComplexityEffectiveComposeTests.A_required_pin_replaces_the_cell_and_sources_pin` | Required pin + cell → return the chain → `source role` |
| PC-11 | `ComplexityEffectiveComposeTests.A_preferred_pin_prepends_and_sources_pin_plus_chain` | Preferred + cell → ignore the pin → first candidate is the cell head |
| PC-12 | `ComplexityEffectiveComposeTests.No_pin_and_no_cell_falls_back_to_role_policy` | empty → return empty → `source rolePolicy` missing |
| PC-13 | `ComplexityEffectiveComposeTests.Governs_default_is_true_only_on_the_default_tier` | Hard vs Medium → always true → red on Hard |
| PC-14 | `ComplexityEffectiveHttpTests.Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded` | card pin → compose `cardPin ?? stagePin` → `stagePin.id` is the card pin |
| PC-15 | `ComplexityEffectiveHttpTests.Put_twice_is_last_writer_wins_without_a_409` | second PUT → refuse on an existing Human row → 409 |
| PC-16 | `routingSettingsModel.test.ts` V-C1 | source `rolePolicy` → ignore `effective` → `Unset — dispatch blocks` |
| PC-17 | `RoutingSettingsTab.test.tsx` V-C4 | Required pin row → render stored list first → pin line not first |
| PC-18 | `RoutingSettingsTab.test.tsx` V-C5 | default column → drop the header suffix → `(default` missing |
| PC-19 | `ComplexityChainRowEditor.test.tsx` V-C7 | Save → PUT only Hard → two calls missing |
| PC-20 | `ComplexityChainRowEditor.test.tsx` V-C8 | Medium 422 → continue to Easy → three PUTs |
| PC-21 | `ComplexityChainRowEditor.test.tsx` V-C9 | pin present, box on → DELETE before the PUTs → order red |
| PC-22 | `ComplexityChainRowEditor.test.tsx` V-C10 | box off → DELETE anyway → one DELETE |
| PC-23 | `routingPins.test.ts` V-C12 | DELETE → invalidate pins only → no second complexity-chains GET |
| PC-24 | `RolePolicySection.test.tsx` V-C14 | Configure → no callback → `onConfigure` not called |
| PC-25 | `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (V-F1) | matrix section → omit it → `## 5. Role × complexity matrix` missing |
| PC-26 | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify("complexity-chain")` (V-F2) | cell upsert → drop the signal → `SubmittedBodies.Count == 0` |
| PC-27 | `OrchestratorInstructionsDeltaTests` V-F3 | identical rewrite → `DiffMatrix` always reports → a line appears |

### Cost

Ordinary green floor: 9 + 8 + 8 + 8 + 8 + 6 + 6 + 5 + 5 + 5 + 6 + 6 + 10 + 10 + 4 = 104 minutes (CP-2–CP-6, CP-8–CP-17).
Red-first: 9 + 6 = 15 minutes (CP-1, CP-7). Authoring: S-A 75, S-B 60, S-C1 60, S-C2 90, S-D 45, S-E 60, S-F 75
minutes. No production timeout or assertion is loosened by this plan; the three rewritten client cases and the two
renumbered renderer literals are listed with their reasons in the amendment.

## 6. Rollout and migration

1. **Land order.** S-A and S-B (parallel), then S-C1, S-D and S-F (S-D and S-F may run in parallel with S-C1: no
   shared files), then S-C2, then S-E's script. Land each as soon as its Review is confirmed.
2. **Restart.** Nothing above is active until the desktop server is restarted onto a build containing it (§7). After
   the restart, confirm `GET /api/version` equals the intended SHA and `GET /api/complexity-chains?role=Plan` returns
   `effective` and `defaultComplexity` (the S-B shape) before touching any pin.
3. **Migration (operator).** Run `pwsh -NoProfile -File scripts/migrate-stage-pins-to-cells.ps1` (dry run) against the
   live server, read the diff, keep the backup path, then `-Apply`, re-read `GET /api/complexity-chains?role=<each>` and
   the Routing tab (every cell now says `Required Human pin → …` above an own rule), then `-Apply -ClearPins`. The
   expected cell contents, from the live pins at ~06:25Z:

   | Role (pin id) | Hard = Medium = Easy |
   |---|---|
   | Plan (`bbdce6e6`) | ClaudeCode/Frontier (fable) |
   | TestDesign (`90e8d896`) | ClaudeCode/Frontier (fable) |
   | Code (`4683c049`) | ClaudeCode/High (opus) |
   | Review (`50328b61`) | Codex/Frontier (gpt-6-astra) |
   | Investigate (`de218932`), Mutation (`64e43198`), Debug (`909836ed`), Coverage (`858afdbb`), Docs (`11a17ef6`), Commit (`68d8d684`), Test (`6855ae74`), Deploy (`f890319f`), Merge (`e6d574aa`) | ClaudeCode/Frontier (fable), ClaudeCode/High (opus) |
   | Custom | no pin, no cell: RolePolicy fallback (`DefaultLevel` High, opus) |

   The reasons carried over are the two operator quotes the pins hold. Mutation's RolePolicy default is `Low`
   (`DelegationSettings.cs:356`); the migrated cell says Frontier then High because that is what the pin says today —
   the operator may edit that row afterwards in the matrix. Writing all three tiers identically means `-Complexity
   Hard|Easy` behave like the default until the operator differentiates them.
4. **What changes for callers after `-ClearPins`.** An explicit `-Kind`/`-Level` that disagrees with the row runs as
   asked with a warning instead of 409 `routing_pin_conflict` (OD-2); a plain dispatch whose row is exhausted is
   Blocked and auto-resumes (OD-6); `delegate.ps1 -Pin` still writes a card pin, which the cell shows as an exception
   (OD-5). Everything else (holds, quota, forbid lists on remaining pins, reroute) is unchanged.
5. **Rollback.** `-Restore <backup.json>` re-PUTs the 13 pins and DELETEs the 39 cells; S-A keeps working with the
   pins back in charge (a Required pin bypasses the row exactly as it bypasses a `-Complexity` cell today).

## 7. Live lag and restart sequencing

The desktop server is `893ddfcee` (2026-10-13 21:50 +0100) and `origin/master` is `899511125`; CARD-0097 S1/S2/S4/S5
and every slice here are inactive until a restart. Sequencing:

- Restart from the **main checkout** after `git pull --rebase`, with `pwsh -NoProfile -File scripts/restart-apphost.ps1
  -ExpectedServerSha <full sha>`; exit 3 is a lock/worktree refusal to inspect, exit 5 is "server build unverified" with
  the lock retained (`scripts/restart-apphost.ps1:299`), and a passing `/health` is not activation — read
  `GET /api/version`.
- Do **not** restart with tasks in flight or with a checkpoint run bound to a task: the checkpoint tool's owner read
  turns a vanished owner into exit 7 (`owning task ended or could not be verified`, `docs/testing-and-build.md:470`), new
  rows stay blocked, and the Code task reports a row it never ran. The pipeline route showed 0 in flight at ~06:25Z;
  the orchestrator re-reads it immediately before the restart and waits for in-flight Code checkpoints to finish.
- Restart once after S-A, S-B, S-C1, S-D, S-F have landed (S-C2 and S-E's script can follow in a second restart, or the
  same one if landed), then run S-E.

## 8. Risks

- **The matrix lies in the window between S-A and S-E.** Mitigated by S-B/S-C1 showing the pin inside every cell it
  overrides, and by OD-3 (pins are cleared only by the operator's command).
- **Fail-open must not become fleet-wide Blocked.** D-2's guard is the whole protection; V-A2 and V-A6 are its tests,
  and PC-2/PC-6 prove they go red when the guard is removed.
- **Behaviour change for exhausted plain dispatches** (OD-6): Blocked instead of 409 when the row's aliases are all
  held. The attention feed names the cell and the dispatcher requeues on hold clear; orchestrators already handle
  `[task … blocked]` notes.
- **Explicit asks bypass cells** (OD-2): the create warning names the cell; the instructions file (S-F) tells
  orchestrators to omit `-Kind`/`-Level` unless the operator asked for a specific model.
- **Last writer wins** on cells and pins (OD-4): seconds-wide window; the row editor and the migration script refetch
  before writing and the editor shows `updatedAt`.
- **Renumbered instruction sections** (D-9): any external reader that greps `## 5. Routing pins` must move to `## 6.`;
  the file header already says the file is generated and names the route per section.
- **Grok hold** until 2026-10-16 05:00Z: a cell naming Grok shows `held` and is skipped; harmless.
- **msw `onUnhandledRequest: 'error'`**: S-C2's DELETE needs a handler in each test that triggers it; a missing one is a
  red CP-13, not a silent gap.

## 9. Sessions that wait for input

This plan adds no session that waits for input and changes no release deadline; the CARD-1083 checklist does not
apply. S-E is an operator-run script, not a session.
