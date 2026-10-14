# CARD-0097 plan (rework) — the Role × complexity matrix as the single routing surface

Task `c491a627` (Plan, Frontier) wrote this plan at base `054795510c1249ffbf65c552ac4e6d3e87defb99`; task `785ac731`
(Plan, Frontier) repaired it on 2026-10-14 for Final Review `2f218eeb` findings F01–F12, continuing from
`2f4d94fb0edc4b14758a613b4605a3ce2e1d3790` on branch `feat/card-task-785ac731` (landing owner stays `c491a627`).
`origin/master` at repair time is `15a9071fa50033a5e7910f806180f36e3c92082a`; `git diff --stat HEAD origin/master`
lists only `tools/Antiphon.Checkpoints/CheckpointApp.cs`, `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs`
and the three documents, so the file:line citations below were re-read in this worktree and hold for master `15a9071fa`.
Live reads at repair time (~08:40Z) against the desktop server: `GET /api/version` (`893ddfcee`, capabilities `land-v2`,
`operator-shutdown-v1`), `GET /api/projects` (Antiphon project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, board
`8988ca03-7414-47ad-b0b6-51556c701703`), `GET /api/agent-tasks/pipeline?unscoped=true` (`taskScope: "null"`, no
activity) versus `GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-…` (`taskScope: "project"`, active work), and
`GET /api/agent-tasks?boardId=8988ca03-…&status=Working|Dispatched` (a CARD-1234 Review task `27930977` Working on
branch `feat/card-task-27930977`; a CARD-0506 S5 Review task `1cb6b989` Dispatched). The pin and cell census from
the first write (13 stage-wide Human Required pins, no card pins; three empty any-role rows) was re-confirmed by the
Final Review and is not re-derived here. Lane: default placement (runner-defaults revision 2 puts work on server2);
no `-Runner`, no `-Platform`; the checkpoints below are TUnit or Vitest and run on Linux.

This plan supersedes CARD-0097 S3 (the `StagePinEditor` over pins, held in
[the 2026-10-13 test design](2026-10-13-card-0097-test-design.md) Appendix A). S1, S2, S4 and S5 of the 2026-10-13
plan are landed on master (`acfb1c325`, `7549067d6`, `47d807c14`, `00ec864ba`) and are built on, not changed.
The verification companion is [the test-design amendment](2026-10-14-card-0097-matrix-test-design.md).

## 0. Outcome

**The operator instruction (2026-10-14) is: "we should make sure the pins are set on Role x complexity matrix …
whenever we ask you to change routing we should be changing these."** The investigation confirmed three gaps:

1. **The matrix governs no dispatch today.** A cell is loaded only when a create carries `-Complexity`
   (`server/Application/Services/AgentTaskService.cs:923-932`); without it the create resolves through
   `ResolveLevel`/`ResolveAgentKind` (`:1072-1073`, `:3976-3986`, `:4011-4045`) and the pin overlay (`:803-837`).
   Zero board tasks since 2026-10-07 carried a complexity (investigation §2.3).
2. **The 13 operator routing changes are pins, not cells.** `RoutingPins` is a separate table the chain service never
   reads (`server/Application/Services/ComplexityChainService.cs:111-134` reads cells, any-role row and config only).
3. **A Required pin with a candidate replaces the cell outright** (`server/Application/Services/RoutingCandidates.cs:58-63`),
   so cells written today would still be bypassed, and the matrix UI says so only in a row banner
   (`client/src/features/settings/RoutingSettingsTab.tsx:463-473`) while an empty cell reads `Unset — dispatch blocks`
   (`client/src/features/settings/routingSettingsModel.ts:46`).

The fix is nine slices: add the default tier setting (S-0), make ordinary Worker creates and the two server-created
children walk the role row at that tier (S-A), make the effective cell say what will run and why (S-B), make the
existing default-create previews read that same answer (S-B2), show the overlay and edit a whole row from the matrix
(S-C1, S-C2), give the script a row verb (S-D), migrate the 13 pins into cells behind an operator-run, ledgered dry run
(S-E), and teach the generated instructions and the docs the new precedence (S-F). Nothing here is active on the
desktop server until it is restarted onto a build that contains these commits (§7).

## 1. Ground truth: what the brief and the card assume versus what the code does

| Assumed | Code today | Evidence |
|---|---|---|
| A cell is consulted only when a create passes `-Complexity` | True. `if (request.Complexity is { } complexity)` opens the walk; the final `else` is `ResolveLevel`/`ResolveAgentKind` | `AgentTaskService.cs:923-932`, `:1070-1073` |
| Precedence card pin > stage pin > chain (role cell, any-role, config) > Blocked | True for `-Complexity`. `ResolveAsync` picks `cardPin ?? stagePin`; `LoadChainAsync` reads cell, any-role, config, else `([], null, "config", null, "none")` | `RoutingPinService.cs:306-310`; `ComplexityRoutingService.cs:312-344` |
| `LoadChainAsync` refuses specialist roles | False. It does not validate the role; it returns whatever any-role row or config list exists for the tier. Only `ComplexityChainService.ValidateCellRole` refuses specialists, and only on the chain API | `ComplexityRoutingService.cs:317-344`; `ComplexityChainService.cs:255-269`; `AgentTaskEnums.cs:431-432` |
| A Required pin with a candidate bypasses the chain; Preferred prepends | True. `pin.Applied && Required && pinSlots.Count > 0` returns the pin slots; otherwise pin slots first, then chain, deduplicated | `RoutingCandidates.cs:58-74`; `RoutingPinCandidateCreateTests.Required_list_plus_complexity_bypasses_the_chain` (`:363`) |
| An unset cell under `-Complexity` Blocks the task | True: `routingExhausted` → `Status = Blocked`, `FailureReason` = the exhausted sentence | `AgentTaskService.cs:1004-1016`, `:1448-1453`; `ComplexityCreateTests.All_cells_empty_blocks_with_the_D3_sentence_and_a_parent_note` |
| A routing pin can only narrow what the caller asked | False. The pin overlay fills `AgentId` from an agent-only pin (`AgentId = … request.AgentId ?? pinDecision.AgentId`) **after** the pin decision, and the standing-agent block then sets `AgentKind = pinned.Kind`. A stamp decided before `:770` would not see that agent | `AgentTaskService.cs:770-781`, `:826-837`, `:840-860`; `RoutingPinService.UpsertAsync:172-177`, `:213-227` |
| A single-candidate Required pin stamps `RoutingPinId` | False. `routingPinId` is set only when `routingWalk.Walked` (`:944-945`) or the composed pin list is walked (`:1036-1037`); a single-candidate pin arrives at the role-policy branch as the overlaid explicit pair and stores `RoutingPinId` null | `AgentTaskService.cs:944-945`, `:1036-1037`, `:1070-1073`, `:1364-1367` |
| No `-Complexity` falls to RolePolicy with `DefaultLevel = High` | True; `Kind` null on every shipped entry; Mutation/Test/Deploy are `Low`, Docs/Commit `Medium`, Investigate/Debug/Coverage/Merge `High` | `DelegationSettings.cs:349-373`, `:375` |
| `ResolveAgainstRolePolicy` withholds unusable policy values | False. It composes `kind ?? policy.Kind ?? ClaudeCode` and `level ?? policy.Level ?? DefaultLevel` with no `DelegatableKinds` or `Enum.IsDefined` check. `RolePolicyReadService` does withhold (`Supported`, `Usable`, `Defined`) and is tested for it | `ComplexityRoutingService.cs:432-446`; `RolePolicyReadService.cs:51-69`, `:78-98`, `:104-105`, `:133-137`; `RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values` |
| The settlement Commit delegate goes through `CreateAsync` | False: `CreateCommitTaskAsync` resolves kind/level from RolePolicy plus the Commit pin via `RoutingCandidates.Compose` with `chain: null`, takes `Candidates[0]` without a hold or quota check, catches routing exceptions into a warning, and constructs `Status = Queued` | `AgentTaskService.cs:3811-3870`, `:3898-3906` |
| The conflict resolver is routed like other Merge work | False: `CreateMergeTaskAsync` builds a Worker/Merge task with `ModelLevel = ResolveLevel(Worker, Merge, null)`, no pin decision, no `Complexity`, no `AgentKind` assignment | `AgentTaskService.cs:3706-3797` (`:3770`) |
| `ComplexityChainService` composes nothing from pins | True: its constructor takes `AppDbContext`, `TimeProvider`, `ComplexityRoutingService`, settings, logger; no pin service | `ComplexityChainService.cs:28-40` |
| `PUT /api/complexity-chains/{role}/{complexity}` has no concurrency token | True: body is `{candidates, provenance, reason?, notAfter?}`; compare `expectedRevision` on runner defaults | `ComplexityChainDtos.cs:6-10`; `RunnerDefaultSettingsService.cs:133-136` |
| The instructions snapshot has no matrix section | True: sections 1–7 are caps, hosts, defaults, holds, pins, levels, standing; the builder reads `RoutingPins` and nothing from `ComplexityChains` | `OrchestratorInstructionsRenderer.cs:111-183`; `OrchestratorInstructionsSnapshotBuilder.cs:30-40`, `:203-247` |
| The instructions body is bounded by a byte cap for any snapshot | Partly. `Fit` drops card pins, then holds, then standing lines, then runner features, and finally returns `Compose` even when still over `cap` (`:85-86`). `MaxBytes` is 4,096–65,536, default 16,384 | `OrchestratorInstructionsRenderer.cs:43-87`, `:261`; `OrchestratorInstructionsSettings.cs:9-11`, `:24` |
| A cell write regenerates the instructions file | False: `RoutingPinService` fires `OrchestratorInstructionsSignal` on upsert/clear; `ComplexityChainService` has no `IServiceProvider` and fires nothing | `RoutingPinService.cs:48-49`, `:270`; `ComplexityChainService.cs:28-40`, `:228-232`, `:248-252` |
| An instructions notice that fails to enqueue is retried | False. `QueueNoticesAsync` writes `session.OrchestratorInstructionsVersion = version` and saves **before** `queue.EnqueueAsync`; `Accept` then skips a session whose column equals the current version, and `ReconcileCoreAsync` returns early when the body version is unchanged. A throw from `EnqueueAsync` therefore leaves the column current with no row until the next content change | `OrchestratorInstructionsService.cs:161-162`, `:268-278`; `OrchestratorInstructionsRecipients.cs:92-110` |
| `complexity-chain.ps1` has no test runner | False. `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs` runs the script with `pwsh` against an `HttpListener` stub (`StubApi`: `LastMethod`, `LastPath`, `LastBody`, `RequestCount`; `ANTIPHON_API` pointed at the stub) | `ComplexityChainScriptTests.cs:17-60`, `:150-235`; `TestHelpers/EphemeralHttpListener.cs:11` |
| `complexity-chain.ps1 set` writes one cell per call and cannot touch pins | True: `-Complexity` is `ValidateSet('Hard','Medium','Easy')`; one PUT per call; `routing-pin.ps1 clear` looks the stage pin up by grain and DELETEs by id | `scripts/complexity-chain.ps1:30-31`, `:204-226`; `scripts/routing-pin.ps1:273-291` |
| A checkpoint row can reuse a build by writing "(reused)" | False. The importer accepts `<project> -> bin-x/`, `CP-n`, `CP-n (-NoBuild)` or `n/a`; `StripWrappingBackticks` strips only a cell that both starts and ends with a backtick, and the build regex is anchored. `CP-n` requires the same `After` group as row n | `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:89`, `:200-229`, `:379-385`; `docs/testing-and-build.md:400`, `:411` |
| `?unscoped=true` lists the whole project pipeline | False. It is the null-project bucket (`taskScope: "null"`); the Antiphon work is under `?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` (`taskScope: "project"`) | live reads above; AGENTS.md (CARD-0881) |
| The row editor can import the cell editor's draft helpers | False. `DraftCandidate` and `toDraft` are module-private (`ComplexityChainCellEditor.tsx:59-75`). The exported helpers are in `routingSettingsModel.ts` (`candidateListError :238`, `nextUnusedCandidate :258`, `CHAIN_KIND_OPTIONS :142`, `MAX_CHAIN_CANDIDATES :150`) | those files |
| `delegate.ps1` predicts the routing before the create | False for routing: it echoes the resolved kind from the create response (`:1207-1212`); its "base preview" is the landing base. The two stale predictors are the Role policy section's `Effective next create` column and the delegate modal's tier preview, both fed by `GET /api/role-policy` | `scripts/delegate.ps1:1207-1212`, `:1240-1247`; `RolePolicySection.tsx:59`, `:93-95`; `DelegateModal.tsx:45-54`, `:108-114`, `:136` |
| The 13 live pins are Plan/TestDesign ClaudeCode/Frontier, Code ClaudeCode/High, Review Codex/Frontier, nine others ClaudeCode/Frontier then ClaudeCode/High | Confirmed by the Final Review's live read (ids in §6). Custom has no pin. All `Required`, `Human`, empty `forbiddenAliases`, null dates and agent | Final Review `2f218eeb`, evidence item 4 |
| Queued chain tasks re-walk the live matrix when their alias is held | True for list-governed tasks (`Complexity` or `RoutingPinId` set); a single-candidate Required pin is not rerouted by a tick | `AgentTaskDispatcher.cs:716-741`, `:1749-1778`, `:1791-1792`; `ComplexityRoutingService.cs:31-32` |
| A reroute ends matrix governance | True: `task.Complexity = null; task.RoutingPinId = null` | `AgentTaskService.cs:2813-2816`; `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` |
| CARD-0097 S3 is still a pin editor waiting for O-1 | Superseded: this plan's S-C2 edits cells and clears the stage pin; `StagePinEditor.tsx`, `usePutRoutingPin`, `pinRequestFrom`, `requiredConfirmCopy` are not built | 2026-10-13 test design Appendix A |
| CARD-1234 does not touch this plan's files | False. Its branch (`feat/card-task-27930977`, Review in flight at repair time) edits `AgentTaskService.ResolveLevel`/`ResolveAgentKind` (`:3975-4045`, new 422 codes `role_policy_level_invalid`, `role_policy_kind_unsupported`), the `DelegationSettingsValidator` RolePolicy loop (`:1313-1340`), `DelegateModal.tsx`, `docs/antiphon-api.md`, `docs/orchestration-loop.md`, `docs/testing-and-build.md` and `Antiphon.Tests.csproj` | `git diff --stat origin/master FETCH_HEAD` after `git fetch origin feat/card-task-27930977` |
| CARD-0506 collides on `client/src/features/settings` | S1–S4 landed (`f43fc6103` … `6e5dae6d5`); S5 is `tests/Antiphon.E2E` only (Review task `1cb6b989` Dispatched at repair time); the Concurrency tab files are not touched here | `git log origin/master --grep CARD-0506`; live task read |
| The live server runs master | False: `893ddfcee`; S1/S2 of CARD-0097 and this plan are inactive until a restart | live `GET /api/version` |

## 2. Decisions

Design decisions (D-n) are the plan's; operator decisions (OD-n) are recorded under the brief's recommended defaults so
the operator can overrule any one before Code is dispatched. Repair-round changes are marked (F-nn).

- **D-1 Stamp `Complexity` at create, after the pin overlay and the standing-agent block (F01).** The stamp point is
  immediately before `var id = Guid.NewGuid()` (`AgentTaskService.cs:917`), after `:770-781` (pin decision), `:803-837`
  (pin overlay) and `:840-860` (standing agent). The predicate `MatrixDefaultApplies(request, liveFollowUp)` is a
  `static` method over the **post-overlay** request: `Complexity is null && AgentKind is null && ModelLevel is null
  && AgentId is null && !IgnoreModelDisabled && Kind == Worker && !AgentTaskRoles.IsSpecialist(Role) && !liveFollowUp`.
  When it holds, `_complexityRouting is not null` and `LoadChainAsync(role, DefaultComplexity).ResolvedFrom != "none"`,
  S-A sets `request = request with { Complexity = _settings.DefaultComplexity }` and `complexityDefaulted = true`;
  the existing `:923` branch then walks with the already-resolved `pinDecision`. Consequences the predicate fixes by
  construction: a single-candidate Required pin has already been overlaid into `AgentKind`/`ModelLevel` at `:826-837`,
  so it is an explicit pair and is **not** stamped (today's path, `RoutingPinId` null, Created detail `(explicit
  override)` plus the `[pin=…]` note); an agent-only pin has filled `AgentId` at `:831` and the standing agent's kind at
  `:859`, so it is not stamped either; a Required candidate list of two or more (`pinComposed.Walked`) skips the
  overlay, is stamped, and the walk composes the pin slots (`RoutingCandidates.cs:58-63`) with the bypass warning
  (`:933-941`) and `RoutingPinId` stamped because `Walked` is true. Rejected: a stamp before `:736` (the review's F01:
  the pin introduces the agent later). Rejected: a predicate that re-reads the pin decision (the overlay already
  encodes it). Rejected: reading cells inside `ResolveLevel` (the single-pair path at `:1070-1120` applies forbid,
  quota and hold to one pair; the walker's skips, re-walk and Blocked machinery would need a second copy).
- **D-2 The fail-open guard is the resolved source, not the cell's existence.** The stamp happens only when
  `LoadChainAsync` returns `ResolvedFrom` ≠ `none` (role cell, any-role row or a non-empty config default). An unset
  row keeps today's RolePolicy path. `LoadChainAsync` does not validate the role (ground truth above), so the
  specialist clause in D-1 is the only thing keeping Check/Distill/Diagnose off the matrix (F12a). Cost: one or two
  `FindActiveAsync` queries per ordinary create.
- **D-3 Server-created children route through one helper (F02, F03).** `ResolveServerChildRoutingAsync(AgentTaskRole
  role, Guid? cardId, CancellationToken ct)` returns `(AgentKind Kind, AgentModelLevel Level, TaskComplexity?
  Complexity, Guid? RoutingPinId, string? BlockedReason, string? Warning)` and is used by both `CreateCommitTaskAsync`
  and `CreateMergeTaskAsync`. Rules, in order: (1) pin decision as today (`_routingPins.ResolveAsync(cardId, role,
  Ask(null,null,null,false))`); a throw from the pin or chain read is caught into `Warning = "Routing read failed
  ({type}); using role policy."` with today's RolePolicy pair (routing-read failure keeps the existing fallback shape);
  (2) when `LoadChainAsync(role, DefaultComplexity).ResolvedFrom == "none"`, today's resolution (`Compose(decision,
  chain: null …)`, head of the list, `RoutingPinId` when walked) — the absent-row compatibility case; (3) otherwise
  `WalkAsync(DefaultComplexity, Worker, role, decision, cardId, null, false, ct)`: `Chosen` → that pair, `Complexity =
  DefaultComplexity`, `RoutingPinId` when `Walked && decision.Applied`; no `Chosen` and the decision is a Required pin
  that was not walked → today's pin pair, unstamped (the dispatcher holds it, `A_required_pinned_chain_task_stays_Queued_with_Held_when_the_pin_is_held`);
  no `Chosen` otherwise → `BlockedReason = walk.ExhaustedSentence(decision) + " A human must choose; do not pick a
  kind yourself."`, `Complexity = DefaultComplexity`. The caller constructs the child with `Status = Blocked`,
  `FailureReason = BlockedReason`, adds a `Blocked` event and calls `EnqueueBlockedParentNoteAsync(task, reason)`
  (`:4589-4613`; the child's `ParentSessionId` is the settled/conflicted task's). A resolved row with no survivor is
  never replaced by the RolePolicy pair. Rejected: leaving `CreateMergeTaskAsync` outside (the single-surface claim
  would be false for a producer the operator meets after every conflicted land).
- **D-4 The effective cell DTO is computed as create would compute it, and withholds what RolePolicy would withhold (F04).**
  S-B calls `RoutingPinService.ResolveAsync(null, role, Ask(null, null, null, false))` for the stage decision, then
  `RoutingCandidates.Compose(decision, chain, label, null, null, resolve)` — the same two calls `WalkAsync` makes
  (`ComplexityRoutingService.cs:211-219`). The RolePolicy pair passed to `Compose` and used when the composed list is
  empty comes from a new `RolePolicyReadService.PolicyTarget(DelegationSettings, AgentTaskRole)` → `(AgentKind Kind,
  AgentModelLevel Level, string Alias)?`, extracted from `Row`/`Effective` (`RolePolicyReadService.cs:51-69`,
  `:78-84`) and null when the kind is outside `DelegatableKinds` or the level is undefined. A null target gives
  `source: "rolePolicy"`, `candidates: []`, `withheldReason: "<role> policy names <kind>/<level>, which delegation
  cannot launch"`. `ComplexityChainService` gains an optional `RoutingPinService? pins` constructor parameter (DI
  resolves it; the three test files that construct the service directly keep compiling). No cycle: `RoutingPinService`
  depends on `ComplexityRoutingService` only (`RoutingPinService.cs:34-46`). Rejected: calling
  `ResolveAgainstRolePolicy` for the fallback (it fabricates an alias for Raw/999; ground truth above).
- **D-4b Default-create previews read the default cell (F04).** `RolePolicyReadService.ReadAsync` composes each row's
  `Effective` from S-B's `GetEffectiveAsync(role, DefaultComplexity)`: source `pin` when a stage pin supplies the head,
  `matrix` when the default cell does, `rolePolicy` otherwise; kind/level/alias from the first candidate, or null with
  the withheld reason. `RolePolicyEffectiveDto` gains `Source` value `matrix` and `WithheldReason`. The Role policy
  section's `Effective next create` column and `DelegateModal.serverRoleLevel` (`:45-54`) consume it (`matrix` →
  `effective.modelLevel`, `tier-source-matrix`). The route stays `GET /api/role-policy`; `RolePolicyReadService` gains
  `ComplexityChainService` and `IOptions<DelegationSettings>` dependencies (registered scoped, `Program.cs:728`).
  Exclusions D-1 lists (explicit kind/level, agent, orchestrator) are not previewed: the column header reads `Effective
  next ordinary Worker create`. Rejected: relabelling the column "ignores the matrix" (the operator reads that tab to
  learn where work goes; a column that disagrees with the matrix is the lie this card removes).
- **D-5 Effective data rides on the existing `?role=` view; the any-role list view is unchanged.** `ComplexityChainDto`
  gains an optional `effective` (null on the list view and on any-role rows) and `ComplexityChainListDto` gains
  `defaultComplexity`. Rejected: a new route (`GET /api/routing-matrix`).
- **D-6 The row editor replaces the held S3 pin editor; clearing the stage pin is part of the same action (F09).**
  S-C2's `ComplexityChainRowEditor` keeps its own draft list built from the exported `routingSettingsModel.ts` helpers
  (`CHAIN_KIND_OPTIONS`, `MAX_CHAIN_CANDIDATES`, `candidateListError`, `nextUnusedCandidate`); `ComplexityChainCellEditor.tsx`
  is not modified and exports nothing new. It writes Hard, Medium and Easy with three sequential `PUT`s through
  `usePutComplexityChain` and, when a stage pin exists and the "Clear the stage pin" checkbox is on (default on),
  sends `DELETE /api/routing-pins/{id}` after the third PUT succeeds. A PUT failure stops the sequence and reports
  which tiers were written. The only pin write the client gains is that DELETE (`useClearRoutingPin`).
  `RolePolicySection`'s copy "pins are set with scripts/routing-pin.ps1" (`:47-50`) becomes a Configure button per
  row. Rejected: a Strength radio or Required confirm (a cell has no strength; OD-2).
- **D-7 The script gets `-Complexity All` and `-ClearStagePin`; `-ClearStagePin` requires `All` (F06).**
  `complexity-chain.ps1 set -Role Review -Complexity All -Candidates Codex/Frontier [-ClearStagePin]` issues the
  three PUTs (Hard, Medium, Easy; stop at the first non-2xx and print `Hard written; Medium refused: <error>; Easy not
  written`, exit 1), then with the switch GETs `/api/routing-pins?role=<Role>` and DELETEs the row whose `cardId` is
  null. `-ClearStagePin` with `-Complexity` other than `All`, or with `-Role Any`, or without `-Role`, exits 1 before
  any request. `clear -Complexity All` clears the three cells. `get -Role X` prints `effective: <source> <list>` from
  S-B beside the stored line. `routing-pin.ps1` stays for card pins, forbid lists, dates and agents. Verification is
  the existing script harness (`ComplexityChainScriptTests`), not a transcript.
- **D-8 Migration is a ledgered script with a dry-run diff bound to the apply (F05, F06).** `scripts/migrate-stage-pins-to-cells.ps1`:
  - **Dry run (default).** GET pins, GET `?role=` cells for each pinned role. Prints the per-role table (`role | pin id |
    pin candidates | cells before (Hard/Medium/Easy) | cells to write | reason | action`). Pins with `forbiddenAliases`,
    `notBefore`, `notAfter` or `agentId` are `manual`; card pins are ignored. Writes `<backup>` JSON: `migrationId`,
    `createdAt`, `apiBase`, `pins[]` (full bodies as returned), `cellsBefore[]` (one entry per role×tier with the GET
    body, or `null` for absent), `plan[]` (intended PUT bodies), `diffSha256` (hash of the printed table), `ledger: []`.
  - **`-Apply -Backup <file>`.** Refuses (exit 1, zero writes) when the backup's `diffSha256` differs from a fresh dry
    run (drift), when any target cell is non-empty and the backup's ledger does not record it as written by this
    migration (`-Resume` is the only way past that), or when the server's `GET /api/complexity-chains?role=` lacks
    `effective` (S-B not active). Each PUT appends a ledger entry `{role, tier, status, at, body}` to the backup before
    the next request; a non-2xx stops the run with the ledger partial. Running `-Apply` again with the same backup
    skips ledgered tiers (idempotent; `-Resume` names the intent).
  - **`-Apply -ClearPins -Backup <file>`.** For each role whose three tiers are ledgered: GET `?role=` and compare each
    tier's stored candidates to the ledgered body; on a mismatch keep the pin and exit 1 naming the cell; on a match
    DELETE the pin and ledger `pinCleared`. A DELETE failure stops the run.
  - **`-Restore -Backup <file>`.** Restores `cellsBefore` (PUT the before body, DELETE when it was absent) for every
    ledgered cell, then re-PUTs each cleared pin from `pins[]`; ledgers `restored`. Rerunnable.
  Rejected: a backup that lists only the cells to delete (loses a Human cell written by S-C2 or a prior partial run).
  Rejected: migrating in `Program.cs` on start. OD-4 stays last-writer-wins; the ledger is the migration's own
  progress record, not an API token.
- **D-9 The instructions file gets a bounded matrix section before the pins section, and cell writes regenerate it (F11).**
  Section 5 `## 5. Role × complexity matrix` (`Owner: GET /api/complexity-chains`; `default <tier>`), one line per
  routable role: `- Plan: Hard a, b; Medium (default) c; Easy unset; effective c via stage pin`. Bounds: at most three
  aliases per tier then `+N`; a line longer than 160 bytes is cut at 157 and ends with `…`; 14 lines. The section
  enters the truncation ladder as its own step between card pins and holds: full rows, else the single line `… 14
  rows: GET /api/complexity-chains`. Pins → 6, levels → 7, standing → 8; the literal headings asserted in
  `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines`
  (`:34-62`) are edited with that reason. `OrchestratorInstructionsDelta` gains `DiffMatrix` beside `DiffPins`
  (`:159-181` pattern) so an unchanged write neither rewrites nor notifies. `ComplexityChainService` gains an optional
  `IServiceProvider? services` and fires `OrchestratorInstructionsSignal.Fire(services, $"complexity-chain {role}/{tier}")`
  on upsert and clear, as the pin service does (`RoutingPinService.cs:48-49`).
- **D-10 No new hub event.** Cells and pins poll at 15 s (`complexityChains.ts:66`, `routingPins.ts:52`) and each
  client write invalidates both query families.
- **D-11 Lane and tests.** Narrow class filters for TUnit (one class per behaviour group), file lists for Vitest, no
  whole-`Antiphon.Tests` lane, no E2E (the Routing tab has none). Script behaviour is bound through the existing
  `pwsh`-plus-`HttpListener` harness. Checkpoints are the closed list in §5.
- **D-12 The notice version column is written after a successful enqueue (F10).** In `QueueNoticesAsync`
  (`OrchestratorInstructionsService.cs:261-278`) the `session.OrchestratorInstructionsVersion = version` write and its
  `SaveChangesAsync` move after `queue.EnqueueAsync` returns. A failed enqueue then leaves the column behind, and the
  next regeneration selects the session again (`Accept`, `OrchestratorInstructionsRecipients.cs:106-108`) with
  `oldVersion` = the column it actually has. Recovery is "next covered change", stated as such; there is no retry
  loop. Rejected: re-notifying lagging columns on an unchanged body (`:161-162`; sessions launched after a version read
  the file at launch and legitimately lag).

Operator decisions, written under the recommended default (unchanged by this repair):

- **OD-1 Default tier for a dispatch without `-Complexity`: `Delegation:DefaultComplexity = Medium`.** New setting,
  validated as a defined `TaskComplexity`. Alternatives: `Hard` (the existing
  `ComplexityCreateTests.No_complexity_is_unchanged_when_fable_is_held` seeds a Hard any-role chain and would change
  meaning); require `-Complexity` on each dispatch (instruction-only; a forgotten flag falls to RolePolicy); a fourth
  "default" column per role.
- **OD-2 A cell does not refuse a disagreeing explicit `-Kind`/`-Level`; it is not consulted (explicit path), and the
  create carries a warning naming the cell it bypassed.** Required pins remain the refusal mechanism
  (`RoutingPinService.cs:345-358`, 409 `routing_pin_conflict`). Cost: after S-E clears the 13 pins, `delegate.ps1 -Role
  Code -Kind Codex` runs Codex with a warning instead of today's 409.
- **OD-3 The 13 pins are migrated and cleared only by an explicit operator command after the dry run.** Nothing in
  S-0..S-F clears a pin on its own; until S-E `-Apply -ClearPins` runs, each cell is displayed beneath the pin that
  overrides it (S-B/S-C1).
- **OD-4 `PUT /api/complexity-chains/{role}/{tier}` stays last-writer-wins, stated and tested.** V-B6 writes the same
  cell twice and asserts the second body is what `GET` returns with `updatedAt >= U1` and no 409 (the assertion is
  order-or-equal; a strict later timestamp would need a controlled clock in the HTTP factory). The row editor refetches
  the row when opened, shows `updatedAt`, and invalidates on each write.
- **OD-5 Card pins stay card-level exceptions, read-only in the matrix.** Listed beneath the table as today
  (`RoutingSettingsTab.tsx:568-598`); S-C1 adds a per-row count in the role name cell.
- **OD-6 A stamped create whose row is exhausted is Blocked with the chain sentence, not refused.** The CARD-0090
  contract for a chain walk (`AgentTaskService.cs:1448-1453`); the dispatcher requeues it when a hold clears
  (`ComplexityDispatcherTests.Clearing_a_hold_requeues_a_routing_blocked_task`). Cost: a plain dispatch whose only
  listed alias is held becomes a Blocked task instead of today's 409 `model_disabled`. D-3 applies the same outcome to
  the Commit and Merge children.
- **OD-7 Orchestrator-kind creates are not stamped.** They keep RolePolicy plus the `MinOrchestratorLevel` clamp.
  Disclosed consequence (Final Review item 3): an Orchestrator/Code create takes the Code pin's High today and, after
  `-ClearPins`, RolePolicy's Frontier; a bare `ignoreModelDisabled` Review create likewise follows RolePolicy once its
  pin is gone. V-A7 and the V-A6 `IgnoreModelDisabled` row bind the exclusion; §6.4 states the consequence.

## 3. Slices

| Slice | What | Files (closed list) | Est. |
|---|---|---|---|
| **S-0 Setting** | `DelegationSettings.DefaultComplexity` (`TaskComplexity`, default `Medium`) and one validator line (`Delegation:DefaultComplexity must be Hard, Medium or Easy`) placed beside the `ComplexityChains` loop (`:1325`). Lands after CARD-1234 (its branch edits the same validator region, `:1313-1340`). | `server/Application/Settings/DelegationSettings.cs` (property beside `:375`; validator beside `:1325`). Tests: new `tests/Antiphon.Tests/Application/MatrixDefaultComplexitySettingsTests.cs` (V-A9, two methods). | 20 min |
| **S-A Server: the role row governs ordinary dispatch and the two server-created children** | `MatrixDefaultApplies` (D-1) and the stamp before `:917`; Created event detail `"{Kind}/{Role} at {level} (matrix default {tier}) in …"` when stamped (`:1556-1560`); OD-2 warning `explicit kind/level: the {Role}/{tier} row was not consulted` when a row exists and the request is explicit; `ResolveServerChildRoutingAsync` (D-3) used by `CreateCommitTaskAsync` (`:3833-3870`) and `CreateMergeTaskAsync` (`:3770`), including the Blocked construction and parent note. Scaffold commit first (CP-3): property-free predicate returning `false`, helper returning today's resolution. | `server/Application/Services/AgentTaskService.cs` (`:917`, `:1556-1560`, `:3706-3797`, `:3811-3935`). Tests: new `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` (V-A1–V-A8, V-A10–V-A16; fixture from `ComplexityCreateTests.cs:466-487`, seeds from `RoutingPinServiceTests.cs:228-248` and `CommitOnSettlePolicyTests.cs:180-199`); `ComplexityCreateTests`, `RoutingPinCreateTests`, `RoutingPinCandidateCreateTests`, `ComplexityDispatcherTests`, `CommitOnSettlePolicyTests` run unchanged (R-A1–R-A5). Needs S-0. | 90 min |
| **S-B Server: effective cell DTO carries the overlay** | `ComplexityEffectiveDto { source, candidates, stagePin, governsDefault, withheldReason }` on `ComplexityChainDto.Effective` (nullable, `?role=` view only); `ComplexityChainListDto.DefaultComplexity`. `ComplexityChainService` takes `RoutingPinService? pins = null`; `GetEffectiveAsync` composes per D-4 through a pure static `ComplexityEffective.Compose(decision, chain, chainLabel, chainResolvedFrom, policyTarget, isDefaultTier)`. `RolePolicyReadService.PolicyTarget` extracted (behaviour-preserving for `Row`). | `server/Application/Dtos/ComplexityChainDtos.cs` (`:26-46`), `server/Application/Services/ComplexityChainService.cs` (`:28-40`, `:111-134`), new `server/Application/Services/ComplexityEffective.cs`, `server/Application/Services/RolePolicyReadService.cs` (`:51-69`, new static). Tests: new `ComplexityEffectiveComposeTests.cs` (V-B1–V-B4, V-B10), new `ComplexityEffectiveHttpTests.cs` (factory from `ComplexityChainHttpTests.cs:142`; V-B5, V-B6); `ComplexityChainRoleHttpTests`, `ComplexityChainServiceTests`, `RolePolicyReadServiceTests` unchanged (R-B1–R-B3). Needs S-0; parallel with S-A (no shared files). | 70 min |
| **S-B2 Server+client: default-create previews read the default cell** | `RolePolicyReadService.ReadAsync` composes `Effective` from `GetEffectiveAsync(role, DefaultComplexity)` (D-4b); `RolePolicyEffectiveDto.Source` gains `matrix`, plus `WithheldReason`. Client: `rolePolicyModel.effectiveLabel` renders `matrix` (`Matrix Medium → ClaudeCode/High (opus)`), the column header becomes `Effective next ordinary Worker create`, `DelegateModal.serverRoleLevel` accepts `matrix` (`tier-source-matrix`). | `server/Application/Services/RolePolicyReadService.cs` (`:19-49`, `:78-98`), `server/Application/Dtos/RolePolicyDtos.cs` (`:35-40`), `client/src/api/rolePolicy.ts` (source union), `client/src/features/settings/rolePolicyModel.ts` (`effectiveLabel :29`), `client/src/features/settings/RolePolicySection.tsx` (`:59`), `client/src/features/delegations/DelegateModal.tsx` (`:45-54`, `:109`). Tests: new `tests/Antiphon.Tests/Application/RolePolicyMatrixPreviewTests.cs` (V-B7–V-B9); `RolePolicyEndpointTests` unchanged (R-B4); Vitest `rolePolicyModel.test.ts` (V-C15), `RolePolicySection.test.tsx` (V-C16), `DelegateModal.test.tsx` (V-C17). Needs S-B and CARD-1234 landed (`DelegateModal.tsx`, `RolePolicyEndpointTests.cs` are on its branch). | 60 min |
| **S-C1 Client: cells show the overlay** | `ComplexityChainDto.effective?`, `ComplexityChainListDto.defaultComplexity?` types. `MatrixCell` renders the effective list first with a source badge (`Required Human pin → Codex/Frontier (gpt-6-astra)` red; `Falls back to RolePolicy → ClaudeCode/High (opus)` grey; `Falls back to RolePolicy → not launchable: <withheldReason>` red; `Own rule`/`Inherits Any role`/`Configuration fallback` as today) and the stored cell beneath it; the default-tier column header reads `Medium (default for an ordinary Worker dispatch without -Complexity)`; an unset non-default cell reads `Unset — a -Complexity <tier> dispatch blocks`. `NON_COMPLEXITY_BOUNDARY` and `pinEffectCopy` rewritten; `cardPinCountLabel(role, pins)` (OD-5). msw default handler gains `defaultComplexity: 'Medium'`. | `client/src/api/complexityChains.ts` (`:19-38`), `client/src/features/settings/routingSettingsModel.ts` (`:20-26`, `:32-47`, `:113-119`), `client/src/features/settings/RoutingSettingsTab.tsx` (`:409-545`), `client/src/test/mocks/handlers.ts` (`:76-82`). Tests: `routingSettingsModel.test.ts` (V-C1–V-C3), `RoutingSettingsTab.test.tsx` (V-C4–V-C6; the three cases at `:347`, `:373`, `:447` rewritten to the new copy with that reason), `complexityChains.test.tsx` (R-C1). Needs S-B. | 60 min |
| **S-C2 Client: whole-row editor that can clear the stage pin** | New `ComplexityChainRowEditor.tsx` (Modal; own draft list from the exported model helpers; Reason; "Clear the stage pin <X>" checkbox shown only when a stage pin exists, default on; confirm copy `rowReplaceCopy(role, pin)`; three sequential PUTs then optional DELETE; partial-failure report). `useClearRoutingPin()` in `routingPins.ts` (invalidates `routingPinKeys.all`, `rolePolicyKeys.all`, `complexityChainKeys.all`). `RoutingSettingsTab` holds the row-editor target; `RoleMatrixRow` gets a "Configure row" button; `RolePolicySection` gets `onConfigure?: (role) => void` and a Configure button per row, copy `:47-50` rewritten. | new `client/src/features/settings/ComplexityChainRowEditor.tsx`, `client/src/api/routingPins.ts` (`:44-54`), `client/src/features/settings/routingSettingsModel.ts` (`rowReplaceCopy`, `rowCellRequests`), `client/src/features/settings/RoutingSettingsTab.tsx`, `client/src/features/settings/RolePolicySection.tsx` (`:43-71`). Tests: new `ComplexityChainRowEditor.test.tsx` (V-C7–V-C11), new `client/src/api/routingPins.test.ts` (V-C12), cases in `routingSettingsModel.test.ts` (V-C13), `RolePolicySection.test.tsx` (V-C14); `ComplexityChainCellEditor.test.tsx` unchanged and its source untouched (R-C2). Needs S-C1 and S-B2 (both edit `RolePolicySection.tsx`). | 90 min |
| **S-D Script: one verb for a role-wide change** | D-7 in `complexity-chain.ps1` (`-Complexity` accepts `All`; `-ClearStagePin` switch with its three refusals; `get -Role X` prints `effective:`). `routing-pin.ps1` header points role-wide changes at this script. ASCII-only. No docs in this slice (S-F owns them). | `scripts/complexity-chain.ps1` (`:9-13`, `:30-34`, `:155-184`, `:186-236`), `scripts/routing-pin.ps1` (`:1-28` header). Tests: `tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs` gains a `ScriptedApi` stub (request sequence, per-path scripted status/body) and V-D1–V-D7; its nine existing methods run unchanged (R-D). Needs S-B for `get`. | 60 min |
| **S-E Migration script (operator-run, ledgered, restorable)** | D-8: new `scripts/migrate-stage-pins-to-cells.ps1 [-Role <R>] [-Apply] [-ClearPins] [-Resume] [-Restore] -Backup <path>`. ASCII-only. Not run against the live server by Code; the operator runs it after §7. | new `scripts/migrate-stage-pins-to-cells.ps1`. Tests: new `tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs` (same harness recipe; in-memory pin/cell store in the stub so read-backs see the writes; V-E1–V-E11). Parallel with S-D (different files). | 90 min |
| **S-F Instructions and docs** | Snapshot: `MatrixSection(DefaultComplexity, Rows)` with one `MatrixInstructionRow(Role, Hard, Medium, Easy, Effective, Source)` per routable role from `ComplexityChainService.ListAsync(role)`; renderer section and ladder step per D-9; `DiffMatrix`; `ComplexityChainService` fires the signal (D-9); D-12 reorder in `QueueNoticesAsync`; `BridgeQueueHarness` registers `ComplexityRoutingService` and `ComplexityChainService` beside `ModelAvailability` (`:173`). Docs: `docs/orchestration-loop.md:880-913` ("Routing pins beat RolePolicy" becomes "The matrix governs an ordinary Worker dispatch; a pin is the exception"), `:936` (the row saying the section has no editor), `:943-948` (the row editor can clear a stage pin), `:950-960` (default tier), the S-E runbook paragraph, `docs/antiphon-api.md:697-714` (`effective`, `defaultComplexity`, `withheldReason`, `matrix` source on `/api/role-policy`, last-writer-wins), `docs/ops-http.md:139` (role-policy row names the matrix source), `.claude/skills/antiphon-delegate/SKILL.md:147` (omitted `-Complexity` on an ordinary Worker dispatch means the default tier). | `server/Application/Services/OrchestratorInstructionsSnapshot.cs`, `OrchestratorInstructionsSnapshotBuilder.cs` (`:30-40`, new `ReadMatrixAsync` beside `:203`), `OrchestratorInstructionsRenderer.cs` (`:43-87`, `:149-183`), `OrchestratorInstructionsDelta.cs` (`:20-27`, new `DiffMatrix`), `OrchestratorInstructionsService.cs` (`:261-278`), `ComplexityChainService.cs` (`:28-40`, `:228-232`, `:248-252`), `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (`:173`), the docs above. Tests: `OrchestratorInstructionsRendererTests` (V-F1 literal renumbering in `:34-62` and `Sample()`; V-F7 byte bound), `OrchestratorInstructionsRefreshTests` (V-F2 `[Arguments("complexity-chain")]` and a `CoveredWriteAsync` arm at `:351-392`; V-F4, V-F5, V-F6, V-F8), `OrchestratorInstructionsDeltaTests` (V-F3). Needs S-A, S-B, S-D, S-E. Before dispatch the orchestrator diffs the CARD-1213 and CARD-1234 branches against master for `docs/orchestration-loop.md` and `docs/antiphon-api.md` and defers while either is in flight. | 90 min |

Order: S-0 (after CARD-1234 lands) → S-A ∥ S-B → S-B2 (after CARD-1234) ∥ S-C1 ∥ S-D ∥ S-E → S-C2 (after S-C1 and
S-B2) → S-F (after S-A, S-B, S-D, S-E). Dependency reasons: S-B reads `settings.DefaultComplexity` (S-0); S-C1 needs
S-B's wire contract (F-A); S-C2 needs S-C1's `MatrixCell` and S-B2's `RolePolicySection` edit; S-D's `get` needs F-A;
S-F documents S-D's and S-E's verbs. S-E's `-Apply` is only meaningful once S-A and S-B are **active on the live
server** (§7); its runbook paragraph says so in its first line.

Commit and push after each slice. One Code task per slice, each in its own `-Worktree`, default placement.

**Collisions.** The census is read with the Antiphon project id: `GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f`
and `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=Queued|Dispatched|Working`; the
`?unscoped=true` read is the null-project bucket and is not evidence. CARD-1234 (branch `feat/card-task-27930977`)
shares `DelegationSettings.cs` (validator), `AgentTaskService.cs` (`ResolveLevel`/`ResolveAgentKind`, which S-A calls
but does not edit), `DelegateModal.tsx`, `docs/antiphon-api.md`, `docs/orchestration-loop.md`: S-0, S-B2 and S-F
wait for it to land and rebase onto that master. CARD-1213 is in the Review column and edits
`docs/orchestration-loop.md` (Final Review F09): S-F defers while its Review or land is in flight. CARD-0506 S5 is
`tests/Antiphon.E2E` only. Shared-area rule: before dispatching S-C1/S-C2/S-B2, re-read the project-scoped pipeline
for Code tasks touching `client/src/features/settings` or `client/src/features/delegations`.

## 4. Edge cases (each bound to a test in the amendment)

- Unset row at the default tier → RolePolicy path, Queued, no Blocked event, Created detail `(role policy)` (V-A2).
- Set row, every candidate held → Blocked with the chain sentence naming `<Role>/Medium`; queued parent note row with
  `SourceTaskId`, `NoteHeader`, `ContentDigest`; clearing the hold requeues it (V-A3, R-A4).
- Explicit `-Kind` or `-Level` with a set row → not stamped; warning names the row; Required pin 409 unchanged (V-A4,
  R-A2 `An_explicit_kind_against_a_required_pin_is_409_routing_pin_conflict`).
- Single-candidate Required stage pin plus a set row → not stamped; pin pair; `Complexity` null; `RoutingPinId` null;
  Created detail `(explicit override)` with the `[pin=…]` note (V-A5a). Required candidate list (2+) plus a set row →
  stamped; pin slots walked; `complexity chain bypassed by …`; `RoutingPinId` set (V-A5b).
- Stage or card agent-only Required pin plus a disagreeing cell → not stamped; `AgentId` = the standing agent;
  `AgentKind` = the agent's kind; `Complexity` null (V-A10, V-A11).
- The predicate excludes specialist roles, orchestrator kind, any agent id, an explicit kind, an explicit level,
  `ignoreModelDisabled` and live follow-ups (V-A6, eight `[Arguments]` rows).
- Orchestrator kind with a set Plan row → not stamped, `MinOrchestratorLevel` clamp still applies (V-A7).
- Settlement Commit child: row with an available head → that pair; held head with an available second → the second
  with the skipped warning; all held → Blocked with the sentence, a Blocked event and a parent note; Required
  single-candidate pin → the pin pair unstamped; routing read failure → RolePolicy with the warning; no row → today's
  pair (V-A8, V-A13, V-A14, V-A15).
- Server-created Merge child → same helper: row candidate, else Blocked, else today's RolePolicy High (V-A12).
- `DefaultComplexity` undefined in configuration → validator failure naming the key (V-A9).
- Effective DTO: Required pin → `source pin`; Preferred pin → `pin+chain`; no pin, cell → `role`/`any`/`config`;
  nothing → `rolePolicy` with the policy pair; policy kind Raw or level 999 → `rolePolicy`, empty candidates,
  `withheldReason`; a card pin is not composed; `governsDefault` true only on the default tier (V-B1–V-B5, V-B10).
- Two PUTs on one cell → second wins, `updatedAt >= U1`, no 409 (V-B6).
- `GET /api/role-policy` effective: stage pin → `pin`; default cell → `matrix`; nothing → `rolePolicy`; withheld → null
  target with reason (V-B7–V-B9).
- Row editor: three PUTs in tier order; a 422 on Medium stops before Easy and the form names `Hard written; Medium
  failed`; DELETE pin only after all three succeed; checkbox hidden without a pin (V-C7–V-C11).
- Script `-Complexity All` stops on the first failing tier and prints which were written; `-ClearStagePin` refuses
  without `All`, with `Any`, or without a role, before any request; DELETE targets the `cardId == null` row only and
  only after three 2xx (V-D1–V-D7).
- Migration: dry run writes nothing and records before-images; manual pins untouched; apply refuses drift and
  non-empty targets; ledger records each PUT; a failed PUT stops; clear deletes only after read-back equality; restore
  reinstates before-images and pins; a second apply is a no-op (V-E1–V-E11).
- Instructions: an identical cell rewrite keeps the file bytes, version and sends no notice (V-F3, V-F8); a cell write
  regenerates and the idle recipient's transcript holds one complete `UserPrompt` equal to the queued body (V-F2,
  V-F4); a busy recipient gets it after turn end (V-F5); a failed enqueue leaves the column behind and the next write
  names that column as `oldVersion` (V-F6); a full legal matrix stays under 4,096 bytes with the pointer line and
  renders all 14 rows at 16,384 (V-F7).

## 5. Verification design

Full V/R/PC binding, fixtures and the wire contract are in
[the amendment](2026-10-14-card-0097-matrix-test-design.md). IDs are stable across both files.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S-0 tests committed, validator line not yet | `tests/Antiphon.Tests -> bin-c097m-0red/` | setting-red | `/*/*/MatrixDefaultComplexitySettingsTests/*` | V-A9 | 2 methods run; `An_undefined_default_complexity_fails_validation` fails (intentional red), `The_default_instance_validates` passes | 2 | 6 | false |
| CP-2 | S-0 | `tests/Antiphon.Tests -> bin-c097m-0/` | setting | `/*/*/MatrixDefaultComplexitySettingsTests/*` | V-A9 | 2 methods, 0 failed/skipped | 2 | 6 | false |
| CP-3 | S-A tests and scaffold committed, stamping not yet | `tests/Antiphon.Tests -> bin-c097m-ared/` | matrix-default-red | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A8, V-A10–V-A16 | the class compiles against the scaffold and all 16 methods run (23 results: V-A6 carries 8 rows); the rows the amendment lists as red fail by the named assertion, none by build or fixture error | 16 | 12 | false |
| CP-4 | S-A | `tests/Antiphon.Tests -> bin-c097m-a/` | matrix-default | `/*/*/MatrixDefaultComplexityCreateTests/*` | V-A1–V-A8, V-A10–V-A16 | 16 methods (23 results), 0 failed/skipped | 16 | 12 | false |
| CP-5 | S-A | CP-4 | matrix-default-regression | `/*/*/ComplexityCreateTests/*` | R-A1 | all methods, 0 failed/skipped (count by the command below) | 14 | 8 | false |
| CP-6 | S-A | CP-4 | pin-create-regression | `/*/*/RoutingPinCreateTests/*` | R-A2 | all methods, 0 failed/skipped | 13 | 8 | false |
| CP-7 | S-A | CP-4 | pin-list-create-regression | `/*/*/RoutingPinCandidateCreateTests/*` | R-A3 | all methods, 0 failed/skipped | 17 | 8 | false |
| CP-8 | S-A | CP-4 | dispatcher-regression | `/*/*/ComplexityDispatcherTests/*` | R-A4 | all methods, 0 failed/skipped | 9 | 8 | false |
| CP-9 | S-A | CP-4 | commit-on-settle-regression | `/*/*/CommitOnSettlePolicyTests/*` | R-A5 | all methods, 0 failed/skipped | 1 | 6 | false |
| CP-10 | S-B tests committed, production not yet | `tests/Antiphon.Tests -> bin-c097m-bred/` | effective-red | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4, V-B10 | compile-only stub of `ComplexityEffective.Compose` returning an empty list and `PolicyTarget` returning null; 5 methods run, 5 fail | 5 | 6 | false |
| CP-11 | S-B | `tests/Antiphon.Tests -> bin-c097m-b/` | effective-compose | `/*/*/ComplexityEffectiveComposeTests/*` | V-B1–V-B4, V-B10 | 5 methods, 0 failed/skipped | 5 | 6 | false |
| CP-12 | S-B | CP-11 | effective-http | `/*/*/ComplexityEffectiveHttpTests/*` | V-B5, V-B6 | 2 methods, 0 failed/skipped | 2 | 6 | false |
| CP-13 | S-B | CP-11 | chain-http-regression | `/*/*/ComplexityChainRoleHttpTests/*` | R-B1 | all methods, 0 failed/skipped | 3 | 5 | false |
| CP-14 | S-B | CP-11 | chain-service-regression | `/*/*/ComplexityChainServiceTests/*` | R-B2 | all methods, 0 failed/skipped | 10 | 5 | false |
| CP-15 | S-B | CP-11 | role-policy-read-regression | `/*/*/RolePolicyReadServiceTests/*` | R-B3 | all methods and `[Arguments]` rows, 0 failed/skipped | 7 | 5 | false |
| CP-16 | S-B2 | `tests/Antiphon.Tests -> bin-c097m-b2/` | role-policy-matrix | `/*/*/RolePolicyMatrixPreviewTests/*` | V-B7–V-B9 | 3 methods, 0 failed/skipped | 3 | 6 | false |
| CP-17 | S-B2 | CP-16 | role-policy-endpoint-regression | `/*/*/RolePolicyEndpointTests/*` | R-B4 | all methods, 0 failed/skipped | 1 | 5 | false |
| CP-18 | S-B2 | n/a | client-role-policy-preview | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/rolePolicyModel.test.ts src/features/settings/RolePolicySection.test.tsx src/features/delegations/DelegateModal.test.tsx` | V-C15–V-C17 | 3 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 5 | false |
| CP-19 | S-C1 | n/a | client-overlay | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-C1–V-C6, R-C1 | 3 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 5 | false |
| CP-20 | S-C2 | n/a | client-row-editor | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ComplexityChainRowEditor.test.tsx src/api/routingPins.test.ts src/features/settings/routingSettingsModel.test.ts src/features/settings/RolePolicySection.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx` | V-C7–V-C14, R-C2 | 5 files run, nonzero aggregate printed, 0 failed/skipped | n/a | 6 | false |
| CP-21 | S-D | `tests/Antiphon.Tests -> bin-c097m-d/` | script-row-verb | `/*/*/ComplexityChainScriptTests/*` | V-D1–V-D7, R-D | all methods (9 existing + 7 new), 0 failed/skipped | 16 | 8 | false |
| CP-22 | S-E | `tests/Antiphon.Tests -> bin-c097m-e/` | migration-script | `/*/*/MigrateStagePinsScriptTests/*` | V-E1–V-E11 | 11 methods, 0 failed/skipped | 11 | 10 | false |
| CP-23 | S-F | `tests/Antiphon.Tests -> bin-c097m-f/` | instructions-render | `/*/*/OrchestratorInstructionsRendererTests/*` | V-F1, V-F7 | all methods and `[Arguments]` rows, 0 failed/skipped | 7 | 6 | false |
| CP-24 | S-F | CP-23 | instructions-refresh | `/*/*/OrchestratorInstructionsRefreshTests/*` | V-F2, V-F4, V-F5, V-F6, V-F8 | all methods and `[Arguments]` rows (`Covered_writes_each_regenerate_and_notify` carries 7 after V-F2), 0 failed/skipped | 15 | 12 | true |
| CP-25 | S-F | CP-23 | instructions-delta | `/*/*/OrchestratorInstructionsDeltaTests/*` | V-F3 | all methods, 0 failed/skipped | 5 | 4 | false |
| CP-26 | all client slices | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | lint gate (`client/src/lint.gate.test.ts`), whole client suite | `CLIENT TESTS EXIT CODE: 0` and the aggregate line reports a nonzero passed count that Code records | n/a | 10 | true |

Lane: every row runs on the default placement (runner-defaults revision 2 = server2); no `-Runner`, no `-Platform`.
TUnit rows run one class filter each through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints --
run --plan <this file>`), which takes the build slot itself; `Antiphon.Tests` is not run whole here. A `CP-n` Build
cell reuses that row's output with `--no-build` and shares its `After` group (`PlanTableImporter.cs:200-211`).
`Min` on regression rows is the method count read from source on 2026-10-14 with
`grep -c "^\s*\[Test\]" tests/Antiphon.Tests/Application/<Class>.cs`; Code re-runs that command for each regression
class when reporting and records the executed count the runner prints. Green Vitest runs print only the aggregate
line; Code records it. `[Arguments]`-expanded results are counted with `grep -c "\[Arguments(" <file>`.

### Positive controls (pending; one per behaviour; Mutation is not run in this card)

| PC | Exact method | Case → mutation → expected red |
|---|---|---|
| PC-1 | `MatrixDefaultComplexityCreateTests.A_plain_create_walks_the_role_row_at_the_default_tier` | Plan/Medium cell `[Codex/Frontier]` → skip the stamp → `ClaudeCode/Frontier` from RolePolicy |
| PC-2 | `MatrixDefaultComplexityCreateTests.An_unset_row_keeps_the_role_policy_path` | no rows → stamp unconditionally → `Blocked` |
| PC-3 | `MatrixDefaultComplexityCreateTests.An_exhausted_default_row_blocks_with_the_cell_name` | all held → force `RefuseIfExhausted` → `RoutingExhaustedException` |
| PC-4 | `MatrixDefaultComplexityCreateTests.An_explicit_kind_is_not_stamped_and_warns` | `-Kind Codex` with a Plan row → stamp anyway → 422 `complexity cannot be combined` |
| PC-5 | `MatrixDefaultComplexityCreateTests.A_required_candidate_list_plus_a_row_is_stamped_and_walks_the_pin` (V-A5b) | list + row → pass `Decision.None` to `WalkAsync` → row candidate chosen, no bypass warning |
| PC-6 | `MatrixDefaultComplexityCreateTests.The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled` (row `Check`) | Check → drop the specialist clause → `true` |
| PC-7 | `MatrixDefaultComplexityCreateTests.An_orchestrator_create_keeps_role_policy_and_the_level_floor` | Orchestrator + Plan/Medium `[ClaudeCode/Low]` → stamp → `Low` ≠ `MinOrchestratorLevel` |
| PC-8 | `MatrixDefaultComplexityCreateTests.The_settlement_commit_child_takes_the_commit_row` | Commit/Medium `[Codex/Frontier]` → keep today's `Compose(chain: null)` only → `ClaudeCode` |
| PC-9 | `MatrixDefaultComplexitySettingsTests.An_undefined_default_complexity_fails_validation` | `(TaskComplexity)999` → drop the validator line → `Succeeded` |
| PC-10 | `ComplexityEffectiveComposeTests.A_required_pin_replaces_the_cell_and_sources_pin` | Required pin + cell → return the chain → `source role` |
| PC-11 | `ComplexityEffectiveComposeTests.A_preferred_pin_prepends_and_sources_pin_plus_chain` | Preferred + cell → ignore the pin → first candidate is the cell head |
| PC-12 | `ComplexityEffectiveComposeTests.No_pin_and_no_cell_falls_back_to_role_policy` | empty → return empty → `source rolePolicy` missing |
| PC-13 | `ComplexityEffectiveComposeTests.Governs_default_is_true_only_on_the_default_tier` | Hard vs Medium → true for both → red on Hard |
| PC-14 | `ComplexityEffectiveHttpTests.Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded` | in `GetEffectiveAsync`, resolve the decision with the role's first active card pin's `CardId` (`_db.RoutingPins.Where(p => p.Role == role && p.CardId != null && p.ClearedAt == null).Select(p => p.CardId).FirstOrDefaultAsync()`) instead of `null` → `stagePin.id == C` → red |
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
| PC-27 | `OrchestratorInstructionsDeltaTests.An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` (V-F3) | identical rewrite → `DiffMatrix` reports on equal input → a line appears |
| PC-28 | `MatrixDefaultComplexityCreateTests.A_stage_agent_only_pin_binds_the_standing_agent_and_is_not_stamped` (V-A10) | stage agent-only pin + Codex cell → evaluate the predicate before the pin overlay → `AgentKind Codex` ≠ the agent's kind |
| PC-29 | `MatrixDefaultComplexityCreateTests.An_exhausted_commit_row_blocks_the_commit_child` (V-A13) | all held → fall back to the RolePolicy pair → `Status Queued` |
| PC-30 | `MatrixDefaultComplexityCreateTests.The_server_created_merge_child_takes_the_merge_row` (V-A12) | Merge/Medium `[Codex/Frontier]` → keep `ResolveLevel` only → `ClaudeCode/High` |
| PC-31 | `ComplexityEffectiveComposeTests.An_unsupported_policy_target_is_withheld` (V-B10) | policy kind Raw → call `ResolveAgainstRolePolicy` → `candidates` non-empty |
| PC-32 | `RolePolicyMatrixPreviewTests.A_default_cell_sources_matrix_in_the_role_policy_effective` (V-B7) | Code/Medium cell + no pin → keep today's `Effective(stagePin, kind, level, alias)` → `source rolePolicy` |
| PC-33 | `ComplexityChainScriptTests.ClearStagePin_requires_All_and_a_named_role` (V-D3) | `-Complexity Hard -ClearStagePin` → drop the refusal → `RequestCount > 0` |
| PC-34 | `ComplexityChainScriptTests.ClearStagePin_deletes_the_stage_pin_only_after_three_2xx` (V-D4) | 422 on Easy → DELETE anyway → a DELETE in the sequence |
| PC-35 | `MigrateStagePinsScriptTests.Apply_refuses_a_nonempty_target_cell_without_Resume` (V-E4) | Human Medium cell present → drop the precondition → a PUT recorded |
| PC-36 | `MigrateStagePinsScriptTests.ClearPins_reads_back_each_cell_and_deletes_only_on_match` (V-E7) | read-back differs → skip the comparison → DELETE recorded |
| PC-37 | `MigrateStagePinsScriptTests.Restore_reinstates_before_images_and_pins` (V-E9) | prior Human cell → DELETE instead of PUT the before body → DELETE where a PUT was expected |
| PC-38 | `OrchestratorInstructionsRendererTests.A_full_legal_matrix_stays_under_the_byte_cap_and_is_elided_before_holds` (V-F7) | 42 cells × 8 candidates at 4,096 → drop the per-line cap and the ladder step → `Bytes > 4096` |
| PC-39 | `OrchestratorInstructionsRefreshTests.A_matrix_write_reaches_an_idle_standing_orchestrator_as_one_complete_UserPrompt` (V-F4) | → insert the `UserPrompt` row with the body truncated to 10 chars (fake adapter override) → equality red |
| PC-40 | `OrchestratorInstructionsRefreshTests.A_failed_notice_enqueue_leaves_the_version_column_behind_and_the_next_write_notifies` (V-F6) | → restore the column write before `EnqueueAsync` → `oldVersion` names the failed version |

### Cost

Ordinary green rows (CP-2, CP-4–CP-9, CP-11–CP-26): 6+12+8+8+8+8+6+6+6+5+5+5+6+5+5+5+6+8+10+6+12+4+10 = 160 minutes.
Red-first rows (CP-1, CP-3, CP-10): 6+12+6 = 24 minutes. Authoring: S-0 20, S-A 90, S-B 70, S-B2 60, S-C1 60, S-C2 90,
S-D 60, S-E 90, S-F 90 minutes. No production timeout or assertion is loosened by this plan; the three rewritten client
cases and the renumbered renderer literals are listed with their reasons in the amendment.

## 6. Rollout and migration

1. **Land order.** CARD-1234 first (not this plan's work). Then S-0; S-A and S-B (parallel); S-B2, S-C1, S-D and S-E
   (parallel); S-C2; S-F. Land each as soon as its Review is confirmed.
2. **Restart.** Nothing above is active until the desktop server is restarted onto a build containing it (§7). After
   the restart, confirm `GET /api/version` equals the intended SHA and `GET /api/complexity-chains?role=Plan` returns
   `effective` and `defaultComplexity` (the S-B shape) before touching any pin; the migration script refuses when that
   shape is absent (D-8).
3. **Migration (operator).** `pwsh -NoProfile -File scripts/migrate-stage-pins-to-cells.ps1 -Backup <path>` (dry
   run) against the live server; read the diff; `-Apply -Backup <path>`; re-read `GET /api/complexity-chains?role=<each>`
   and the Routing tab (each migrated cell now says `Required Human pin → …` above an own rule); then `-Apply
   -ClearPins -Backup <path>`. The expected cell contents, from the live pins:

   | Role (pin id) | Hard = Medium = Easy |
   |---|---|
   | Plan (`bbdce6e6`) | ClaudeCode/Frontier (fable) |
   | TestDesign (`90e8d896`) | ClaudeCode/Frontier (fable) |
   | Code (`4683c049`) | ClaudeCode/High (opus) |
   | Review (`50328b61`) | Codex/Frontier (gpt-6-astra) |
   | Investigate (`de218932`), Mutation (`64e43198`), Debug (`909836ed`), Coverage (`858afdbb`), Docs (`11a17ef6`), Commit (`68d8d684`), Test (`6855ae74`), Deploy (`f890319f`), Merge (`e6d574aa`) | ClaudeCode/Frontier (fable), ClaudeCode/High (opus) |
   | Custom | no pin, no cell: RolePolicy fallback (`DefaultLevel` High, opus) |

   The reasons carried over are the two operator quotes the pins hold. Mutation's RolePolicy default is `Low`
   (`DelegationSettings.cs:357`); the migrated cell says Frontier then High because that is what the pin says today —
   the operator may edit that row afterwards. Writing all three tiers identically means `-Complexity Hard|Easy` behave
   like the default until the operator differentiates them.
4. **What changes for callers after `-ClearPins`.** An explicit `-Kind`/`-Level` that disagrees with the row runs as
   asked with a warning instead of 409 `routing_pin_conflict` (OD-2); a plain dispatch whose row is exhausted is
   Blocked and auto-resumes (OD-6); a settlement Commit child or a conflict-resolver Merge child whose row is exhausted
   is Blocked with a note to the settled task's caller (D-3); an Orchestrator-kind create and an `ignoreModelDisabled`
   create follow RolePolicy, not the row (OD-7, V-A7, V-A6); `delegate.ps1 -Pin` still writes a card pin, which the
   cell shows as an exception (OD-5). Holds, quota, forbid lists on remaining pins and reroute are unchanged.
5. **Rollback.** `-Restore -Backup <path>` reinstates each ledgered cell's before-image (absent cells are deleted, prior
   bodies re-PUT) and re-PUTs the cleared pins from the backup; with the pins back, a Required pin bypasses the row as
   it bypasses a `-Complexity` cell today. Cells written by the row editor after the backup and not ledgered are left
   as they are and listed by the script.

## 7. Live lag and restart sequencing

The desktop server is `893ddfcee` (2026-10-13 21:50 +0100) and `origin/master` is `15a9071fa`; CARD-0097 S1/S2/S4/S5
and this plan's slices are inactive until a restart. Sequencing:

- Restart from the **main checkout** after `git pull --rebase`, with `pwsh -NoProfile -File scripts/restart-apphost.ps1
  -ExpectedServerSha <full sha>`; exit 3 is a lock/worktree refusal to inspect, exit 5 is "server build unverified" with
  the lock retained (`scripts/restart-apphost.ps1:299`), and a passing `/health` is not activation — read
  `GET /api/version`.
- Do **not** restart with tasks in flight or with a checkpoint run bound to a task: the checkpoint tool's owner read
  turns a vanished owner into exit 7 (`docs/testing-and-build.md:470`). The zero-in-flight gate is read with the
  project id (`?projectId=d4ea7ae9-…`) and the board task list, never `?unscoped=true`; the orchestrator re-reads it
  immediately before the restart and waits for in-flight Code checkpoints to finish.
- Restart once after S-0, S-A, S-B, S-B2, S-C1, S-D, S-F have landed (S-C2 and S-E's script can follow in a second
  restart, or the same one if landed), then run S-E.

## 8. Risks

- **The matrix lies in the window between S-A and S-E.** Mitigated by S-B/S-C1/S-B2 showing the pin inside every cell
  and preview it overrides, and by OD-3 (pins are cleared only by the operator's command).
- **Fail-open must not become fleet-wide Blocked.** D-2's guard and D-1's predicate are the protection; V-A2 and V-A6
  are its tests, and PC-2/PC-6 are the controls that go red when either is removed.
- **Behaviour change for exhausted plain dispatches and server-created children** (OD-6, D-3): Blocked instead of
  409, or instead of a silently queued RolePolicy pair. The attention feed names the cell and the dispatcher requeues
  on hold clear; orchestrators already handle `[task … blocked]` notes.
- **Explicit asks bypass cells** (OD-2): the create warning names the cell; the instructions file (S-F) tells
  orchestrators to omit `-Kind`/`-Level` unless the operator asked for a specific model.
- **Last writer wins** on cells and pins (OD-4): seconds-wide window; the row editor and the migration script refetch
  before writing and the editor shows `updatedAt`.
- **Renumbered instruction sections** (D-9): an external reader that greps `## 5. Routing pins` must move to `## 6.`;
  the file header already says the file is generated and names the route per section.
- **Instructions byte budget** (D-9): the matrix is bounded per line and elided to a pointer under pressure; V-F7 binds
  it at 4,096 and 16,384.
- **Notice loss on a failed enqueue** (D-12): recovery is the next covered change, not a retry; V-F6 binds the column
  semantics.
- **Grok hold** until 2026-10-16 05:00Z: a cell naming Grok shows `held` and is skipped; harmless.
- **msw `onUnhandledRequest: 'error'`**: S-C2's DELETE needs a handler in each test that triggers it; a missing one is a
  red CP-20, not a silent gap.

## 9. Sessions that wait for input

This plan adds no session that waits for input and changes no release deadline; the CARD-1083 checklist does not
apply. S-E is an operator-run script, not a session. With parking disabled, an input-waiting session has no automatic
release deadline; nothing here changes that.
