# CARD-0558: Stages as shareable per-project pipelines, and the definition object that names them

Plan task `a8f042bb`, 2026-09-17, inspected checkout `3a62074e` (master) plus the investigation
commit `40527766` cherry-picked onto this branch as `5108bc9b`. Investigation:
[2026-09-17-card-0558-stages-as-shareable-workflows.md](../../investigations/2026-09-17-card-0558-stages-as-shareable-workflows.md)
(task `7102ff54`). Design authority this plan builds on: CARD-0146 plan ("add roles; do not add
a stage enum", `2026-09-03-card-0146-stage-pipeline-handoff-plan.md:31`), CARD-0058 spec D4
(no DB-backed bundle content), CARD-0470 D-8 (self-hosted rollout), and the two pin-at-use
precedents `AgentTuiProfileRevision` / `RunAttempt.BoardWorkflowDefinitionId`.

This is a large architecture card. The honest scope is three Code dispatches plus one follow-up
card; §Implementation slices says which slice goes in which dispatch and why. Written under
stated defaults (D-1..D-12); `next: decide` names the two that are the operator's to confirm.

## Disposition in six lines

1. **Stage KINDS stay `AgentTaskRole`.** Investigate / Plan / TestDesign / Code / Review /
   Mutation remain the fixed vocabulary of what a stage IS. Nothing in this card adds an enum
   member to `AgentTaskRole` or `PipelineHandoffKind`, so the 202-test-file role footprint and
   every server-enforced role semantic (Mutation admission, land refusal, verification rounds,
   `RolePolicy`, pins, WIP) are untouched (D-1).
2. **A `PipelineDefinition` is a named, versioned, shareable ordered SUBSET of those kinds with
   per-stage `bundleKey` and allowed `next` edges.** Identity row plus immutable revision rows
   plus an active-revision pointer, the `AgentTuiProfile`/`Revision` shape. `Board.PipelineDefinitionId`
   overrides `Project.DefaultPipelineDefinitionId`, which falls back to a seeded built-in
   "Standard pipeline" that encodes today's six-stage order and every `next:` edge the bundles
   currently state in prose (D-2..D-5).
3. **The dormant `CardWorkflowRun` / `CardWorkflowStage` rows become the per-card snapshot of
   the resolved revision**, re-pointed away from gen-1 (`AgentId`, `WorkflowTemplateId`,
   `ExecutorType`, `ModelName`, `GateRequired`, `SystemPrompt` dropped; `Role`, `BundleKey`,
   `AllowedNextJson`, `PipelineDefinitionRevisionId` added). A run is created lazily by the
   first card-bound stage-role task and the stage pointer follows bound stage tasks, so
   `currentWorkflowStageName` lights up on `CardRow`, `CardModal` and the home rail with the
   plumbing that already exists (D-6..D-8).
4. **Prompt content stays git-owned.** A stage names a `stage-*` bundle KEY; the content, its
   hash version, the drift badge and the §10 review lane are unchanged. The `next:` menu in
   `StageHandoffContract` is rendered per stage from the definition; everything else a role
   means is code and stays code (D-9).
5. **Generations:** gen-1 BMAD engine is REMOVED in a separate follow-up card (this card only
   drops the two FKs into it); gen-2 `BoardWorkflowDefinition` / `WORKFLOW.md` is KEPT unchanged
   (tracker config + spawn prompt + hooks, not stages); gen-3 rows are REUSED as above. Queue
   assignment stops creating runs (D-10, D-11).
6. **Rollout is safe under self-hosting because no new `next:` token is introduced.** Three
   land-then-restart steps: schema + definitions + pointers (inert); run snapshot + launch/handoff
   wiring; client. In-flight tasks keep their launch bundles and settle under the unchanged alias
   map (D-12).

## Ground truth

| Card / handoff assumption | Observed on `3a62074e` | Consequence |
|---|---|---|
| Stages need a new stage concept. | `AgentTaskRoles.IsStage` (`AgentTaskEnums.cs:387-393`) and the EF `Stage` expression (`:398-404`) define the six stage roles; 12 surfaces key on them (investigation §3): `StageKeyFor` (`InstructionBundles.cs:221-233`), `PipelineHandoff` aliases/`Token`/`TryToStageRole` (`:24-45`, `:56-68`, `:75-100`), `StageHandoffContract` (`DelegationReportFormatter.cs:333-343`), `VisibleRoles` (`AgentTaskPipelineStatusService.cs:35-38`), `RolePolicy` (`DelegationSettings.cs:320-327`), `ComplexityRoutingService.RoutableRoles` (`:63-68`), `RoutingPin.Role` (`:36-42`), `delegate.ps1:23` ValidateSet, client `AgentTaskRole` union (`agentTasks.ts:14-47`) and `STAGE_LABEL` (`pipelineStageModel.ts:61-79`). | D-1: the role enum is the universe of kinds; a definition selects and orders a subset. No enum member is added, so none of the 12 surfaces is re-keyed. |
| The gen-3 run rows are reusable. | Shape yes: `CardWorkflowRun` (`CardId`, `Status`, `CurrentStageId`, timestamps, `Stages`) and `CardWorkflowStage` (`StageOrder`, `Name`, `Status`, `ResultSummary`, `FailureReason`), one-to-one `Card.ActiveWorkflowRunId` (`AppDbContext.cs:975`, `:1007-1009`), DTO projection in five services, client unions (`boards.ts:12`, `:71-73`), rendering (`CardModal.tsx:413-416`, `CardRow.tsx:107-109`), archive guard (`CardService.cs:916-927`), home-rail classifier (`HomeTaskService.cs:328-346`). Vocabulary no: `AgentId` Restrict FK to a standing `Agent` (`:1074-1077`), `WorkflowTemplateId` SetNull FK to gen-1 (`:1079-1082`), `WorkflowDefinitionSnapshot` YAML text, `ExecutorType`/`ModelName`/`GateRequired`/`SystemPrompt` on the stage (`CardWorkflowStage.cs:11-14`). Live DB: 1 run (Queued), 2 stages, 0 of 729 cards linked. | D-6: re-point by migration; the one legacy run and its two stages are deleted in the migration (they cannot be attributed to a revision and no card references them). Table names stay. |
| Stage order lives somewhere in code. | Only in prose: six `next:` lines (`grep "^next:" server/Bundles/stage-*.md`), `orchestrator.md:33-37`, the §1 diagram (`docs/orchestration-loop.md:118-160`). The server records what the delegate wrote (`AgentTaskReplyService.cs:665-668`: `TryParse` → `CapHandoff` → `task.NextStage`) and projects a ready row from it (`BuildReady`, `AgentTaskPipelineStatusService.cs:209-283`). | D-3: order and edges become revision content; the built-in revision transcribes the six prose lines exactly. D-9: the menu is rendered from the stage row, and settlement stays an enrichment, never a gate. |
| A definition object exists somewhere to extend. | None referenced by board or project. `BoardWorkflowDefinition` is board-OWNED (`BoardId` FK cascade, `IX_BoardWorkflowDefinitions_BoardId_IsActive`, `AppDbContext.cs:1397-1420`); gen-1 `WorkflowTemplate` is referenced only by `Agent.DefaultWorkflowTemplateId` (`Agent.cs:18`, `:190`) and the orchestrator preset (`AgentPresets.cs:23`, `:41`). | D-2: new `PipelineDefinition` + `PipelineDefinitionRevision`; new nullable FKs on `Board` and `Project`. |
| The prompt is the bundle. | Three places (investigation §3): the embedded `stage-<role>.md` (552-2,525 bytes, `wc -c`), C# literals (`StageHandoffContract`, `VerificationProfileBlock` `:257-282`, Shared-write commit line `:224-228`), and server-enforced role semantics (`AgentTaskLandService.cs:78-79`, `CommitOnSettleEligibility.cs:12`, `InterimVerificationPolicy.CapHandoff` `:48-51`, `RolePolicy`, stage-wide pins). `AgentBundleAttachment.cs` doc: "This table is the ONLY thing the database is allowed to know about bundles." | D-9: a stage carries a bundle KEY, validated against `InstructionBundles.All`; content never enters the DB. The role-semantics limit is stated in the definition's own docs. |
| "Sharing across projects" is missing. | Stage-role tasks exist on 5 boards, all six roles, 1,519 rows; the glance is fleet-wide (no board filter in `BuildAsync`). Sharing is total and involuntary. | The gap is the referenceable, versioned object, not distribution. D-2 gives every board an explicit effective pipeline (board → project → built-in), which today is implicit everywhere. |
| Versioning precedent to follow. | `AgentTuiProfile` (identity, `ActiveRevisionId`, composite FK to `(ProfileId, Id)`, `AppDbContext.cs:653-679`) + `AgentTuiProfileRevision` (immutable content rows, `IX_..._ProfileId_RevisionNumber` unique, `:681-715`); `AgentSession.TuiProfileRevisionId` pins the revision. `BoardWorkflowDefinition` uses one row per version with `IsActive`; `RunAttempt.BoardWorkflowDefinitionId` pins the row (`RunAttempt.cs:11`). | D-4: TUI-profile shape, because a SHARED definition needs an identity that survives revisions (a board points at the identity; the active pointer moves). |
| Self-hosted rollout is a special dance. | CARD-0470 D-8 (`2026-09-09-card-0470-code-mutation-split-plan.md:109-117`) needed a temporary Debug worker because the OLD server could not parse `next: mutation`. Here the alias map (`PipelineHandoff.cs:24-45`) is unchanged and no token is added. Warm reuse keeps launch-time bundles; drift is recomputed at `AgentTaskDispatcher.cs:4645`. | D-12: three land+restart steps, none needing a compatibility worker. |
| Gen-1 removal is "33 server + 31 client files". | `grep -rl "WorkflowEngine\|WorkflowTemplate\|StageExecution\|GateDecision\|IStageExecutor\|TemplateGroup\|ModelRouting\b" server` = 43 files (a superset: `AgentService.cs`, `AppDbContext.cs`, `Program.cs`, `AgentDtos.cs` are shared); 30 client files; 26 server test files; 4 E2E tests (`WorkflowOutputTests.cs`); `WorkflowEngineTests` 15 + `WorkflowEngineParsingTests` 13 tests. Gen-2 depends on gen-1's parser: `WorkflowDefinitionLoader.cs:156` (`ParseYamlHooks`), `AgentSessionService.cs:2918-2920`, and the legacy non-markdown branch in `CardService.cs:1548-1565` / `OrchestratorService.cs:1023-1037` (`ParseYamlDefinition` on board content). | D-10: gen-1 removal is a follow-up card with its own Investigate to partition exclusive from shared files; the parser stays (renamed) for gen-2. This card drops only `CardWorkflowRun.WorkflowTemplateId` and `Agent.WorkflowRuns`. |
| Queue assignment is the only run creator. | `AgentService.AssignCardAsync` (`:826-872`) calls `CardWorkflowRunFactory.CreateFromAgentDefaultAsync` (`:21-37`), which loads a gen-1 template; `DELETE .../queue/{cardId}` nulls the link; agent delete removes the agent's runs (`:787-815`); `CardLifecycleTransitions.cs:70-80` nulls on Review/Done/Canceled dequeue. `POST /api/agents/{id}/queue` has one client caller (`agents.ts:703`). | D-11: queue assignment assigns only; the factory is rewritten to take a revision; agent delete no longer touches runs (Card cascade owns them); `CardLifecycleTransitions` stops nulling the run and instead completes it (D-8). |
| Launch composes bundles by role. | `AgentTaskDispatcher.cs:4121-4122` composes `InstructionBundles.ForDelegate(task.Kind, task.Role, attachedBundleKeys)`; `:4645` recomputes the desired composition for drift. `attachedBundleKeys` come from `AgentBundleAttachments.LoadAsync` (`:3419`, `:3650`). | D-9: `ForDelegate` gains an optional `stageBundleKey`; both call sites pass the task's stage-row key; `StageKeyFor` stays the fallback. |
| Task creation knows the card and board. | `AgentTaskCardBinder.BindAsync` (`AgentTaskService.cs:487`) yields `CardId` only; the open gate runs per project+role (`DelegationOpenGate.cs:63-67`); the task row is initialised at `:966-979`. | D-7: `EnsureActiveRunAsync(cardId)` runs after binding and before the gate transaction; it loads `Card`+`Board`+`Project` once for stage-role Worker tasks with a card. |
| Migrations. | CLI-generated only (`docs/project-context.md:125`); latest `20260917012003_AddVerificationProfile`; migration tests live in `tests/Antiphon.Tests/Migrations/` (`CommitOnSettleMigrationTests.cs`). | One migration `AddPipelineDefinitions` (S1); a migration test in the same folder. |
| Seeding. | `DatabaseSeeder` re-asserts gen-1 templates every boot (`:47-51`, `:90-119`); `DatabaseSeederTests` pins that. | D-5: the built-in pipeline is seeded from a code constant; a content change appends a revision and moves the active pointer; operator definitions are never touched by the seeder. |

## Decisions

### D-1. Stage kinds are `AgentTaskRole`; a definition orders a subset

A `PipelineStageSpec` is `(Role: AgentTaskRole, BundleKey: string, AllowedNext: string[])`.
`Role` must satisfy `AgentTaskRoles.IsStage`; a role appears at most once per revision; a
revision has 1..N stages. `AllowedNext` entries are canonical handoff tokens
(`PipelineHandoff.Token`): a stage role present in the same revision, or `land` / `decide` /
`none`. Self-loops are allowed (Review → review is the CARD-0544 Interim → Final edge).

**Rejected:** free-form stage names keyed per workflow. Every routing, policy, pin, WIP, land
and verification surface is keyed by `AgentTaskRole`; a stage that is not a role has none of
them (investigation §4). CARD-0146 recorded the same rule.

**Rejected:** adding new roles in this card (e.g. "Docs" as a stage). Any role can be promoted
later with the CARD-0470 recipe; this card changes the definition layer only.

### D-2. `PipelineDefinition` + `PipelineDefinitionRevision`, referenced from Board and Project

Names use "Pipeline" because the repo already has three things called "workflow":
`Domain.ValueObjects.WorkflowDefinition` (gen-1 YAML record), `BoardWorkflowDefinition` (gen-2),
and the `Workflow` entity (gen-1). "Pipeline stage" is the CARD-0146 term
(`PipelineHandoff`, `AgentTaskPipelineStatusService`, `orchestration-loop.md` §1). The API word
is `pipeline-definitions`.

```
PipelineDefinitions
  Id              uuid PK (ValueGeneratedNever)
  Name            varchar(200), unique (IX_PipelineDefinitions_Name)
  Description     varchar(2000)
  Source          int  (PipelineDefinitionSource: BuiltIn=0, Custom=1)
  ActiveRevisionId uuid? -> composite FK (Id, ActiveRevisionId) -> Revisions(DefinitionId, Id), NoAction
  ArchivedAt / ArchivedReason / ArchivedBy   (archive is what delete means; Restrict FKs below)
  CreatedAt / UpdatedAt

PipelineDefinitionRevisions
  Id              uuid PK
  DefinitionId    uuid FK -> PipelineDefinitions, Cascade
  RevisionNumber  int > 0, unique per definition (IX_..._DefinitionId_RevisionNumber)
  StagesJson      jsonb, required, immutable after insert (AppDbContext immutability guard,
                  the SourceLandingOperationId pattern)
  ContentHash     char(8): first 8 hex of SHA-256 of the canonical StagesJson (the bundle
                  version rule, so a revision renders as `<name> r<N> v<hash8>`)
  ChangeNote      varchar(400)?
  CreatedAt

Boards.PipelineDefinitionId          uuid? FK Restrict   (IX_Boards_PipelineDefinitionId)
Projects.DefaultPipelineDefinitionId uuid? FK Restrict   (IX_Projects_DefaultPipelineDefinitionId)
```

Resolution `PipelineResolution.ResolveForCardAsync(card)`: `board.PipelineDefinitionId`, else
`project.DefaultPipelineDefinitionId`, else the built-in; then that definition's active
revision. An archived definition is refused at pointer-write time (409
`pipeline_definition_archived`); an existing pointer to a later-archived definition still
resolves (archive hides, it never breaks a run). The effective pipeline is exposed on
`GET /api/boards/{id}` as `pipeline: { definitionId, name, revisionId, revisionNumber, hash,
source: "board" | "project" | "builtin" }` and on `GET /api/projects/{id}` for the default.

**Rejected:** a `pipeline:` block in gen-2 `WORKFLOW.md` front matter. Gen-2 is owned by one
board and mirrored under one repo's `.antiphon/boards/<id>/` (`WorkflowFileStore.cs:17-18`);
"shared across projects" would degenerate into copies with no identity, boards on projects
without `LocalRepositoryPath` cannot mirror (`:39`), and the run would pin a per-board row
rather than a shared revision.

**Rejected:** definitions as repo files under `server/Bundles` (the bundle model applied to
order). Same identity problem, plus a built-in default must exist for a project with no file.
The git-reviewed property the operator wants for the DEFAULT is kept by seeding the built-in
from a code constant (D-5).

### D-3. Stage rows are jsonb on the revision, not a third table

`StagesJson` is `[{ "role": "Code", "bundleKey": "stage-code", "allowedNext": ["review","code","decide"] }, …]`
with enum member names on disk (the `RoutingPin.CandidatesJson` convention, `RoutingPin.cs:25-29`).
Consumers read the whole array; revisions are immutable; there is no partial update, so rows
buy nothing. The PER-CARD stage rows (`CardWorkflowStages`) stay a table because they carry
mutable status (D-8).

**Rejected:** `PipelineDefinitionStages` table. Two extra joins on every resolution and launch
for data that never changes after insert.

### D-4. TUI-profile versioning shape; revisions are append-only; the active pointer moves

`POST /api/pipeline-definitions/{id}/revisions` validates (D-1, D-9), inserts revision N+1 and
sets `ActiveRevisionId` in one transaction. There is no PUT on a revision. A run pins
`PipelineDefinitionRevisionId` (Restrict) and never follows the pointer (pin-at-use rule,
`RunAttempt.BoardWorkflowDefinitionId`, `AgentSession.TuiProfileRevisionId`). Changing a board
or project pointer affects only runs created afterwards; `POST /api/cards/{id}/workflow-run/resnapshot`
is the explicit way to move an open card (D-7).

### D-5. One seeded built-in, owned by code; operators clone it

`PipelineDefinitions.StandardPipeline` (a static in `server/Application/Services/PipelineDefinitions.cs`,
fixed id `c0000000-0000-0000-0000-000000000001`, `Source = BuiltIn`) transcribes today's order
and every prose `next:` edge:

| Order | Role | BundleKey | AllowedNext (from the bundle's `next:` line) |
|---|---|---|---|
| 0 | Investigate | `stage-investigate` | plan, investigate, decide, none |
| 1 | Plan | `stage-plan` | test-design, code, decide, investigate |
| 2 | TestDesign | `stage-test-design` | code, plan, decide |
| 3 | Code | `stage-code` | review, code, decide |
| 4 | Review | `stage-review` | land, review, code, decide |
| 5 | Mutation | `stage-mutation` | none, decide |

`DatabaseSeeder.SeedPipelineDefinitionsAsync` inserts the identity row if absent, and appends a
revision (moving the pointer) only when the active revision's `ContentHash` differs from the
code constant's hash. It never edits a `Custom` definition. `PUT`/revision writes on a
`BuiltIn` definition are refused (409 `pipeline_definition_builtin`); `POST .../clone` copies
the active revision into a new `Custom` definition. The seeder test pins: seeded once; boot
twice → still one revision; constant changed → revision 2 active, revision 1 intact.

**Rejected:** letting operators edit the built-in in place. The seeder would then either
overwrite operator edits on every boot (gen-1's behaviour) or never ship a corrected default.

### D-6. `CardWorkflowRun` / `CardWorkflowStage` re-pointed, tables and DTO fields kept

Migration `AddPipelineDefinitions` (one migration, S1):

```
CardWorkflowRuns
  - DROP FK/column AgentId; DROP FK/column WorkflowTemplateId; DROP WorkflowDefinitionSnapshot
  - ADD PipelineDefinitionId uuid NOT NULL FK Restrict
  - ADD PipelineDefinitionRevisionId uuid NOT NULL FK Restrict (immutable after insert)
  - KEEP WorkflowName (= definition name at snapshot), Status, CurrentStageId, FailureReason,
    CreatedAt/UpdatedAt/StartedAt/CompletedAt
  - ADD partial unique index IX_CardWorkflowRuns_CardId_Open ON (CardId) WHERE Status IN (0,1)
    (Queued, Running): at most one open run per card, enforced by the database
  - DELETE the 1 legacy row (and its 2 stages) before the NOT NULL adds: it is a gen-1 snapshot
    ("Document Project", CARD-0001, Done, unlinked) that no revision can own
CardWorkflowStages
  - DROP ExecutorType, ModelName, GateRequired, SystemPrompt
  - ADD Role int NOT NULL (AgentTaskRole), BundleKey varchar(100) NOT NULL,
    AllowedNextJson jsonb NOT NULL
  - KEEP StageOrder, Name (= role member name), Status, ResultSummary, FailureReason, timestamps
  - ADD unique index IX_CardWorkflowStages_RunId_Role
Agents      - DROP navigation WorkflowRuns (no column)
AgentTasks  - ADD CardWorkflowStageId uuid? FK -> CardWorkflowStages SetNull
              (IX_AgentTasks_CardWorkflowStageId)
```

`CardWorkflowRunStatus` and `CardWorkflowStageStatus` enums are unchanged so the client unions
(`boards.ts:12`), 25 fixtures and five DTO projections compile untouched. Values this card never
writes are documented as reserved: run `WaitingForHumanReview`, `Failed`; stage `WaitingForHumanReview`.

**Rejected:** renaming the tables to `CardPipelineRun`. 17 server files, three client unions and
25 fixtures would churn for no semantic gain; the DTO field names (`activeWorkflowRunId`,
`workflowRunStatus`, `currentWorkflowStageName`) are already what the card asked to make real.

### D-7. A run is created lazily by the first card-bound stage task, or explicitly

`CardWorkflowRunService`:

- `EnsureActiveRunAsync(cardId, ct)`: returns the open run, else resolves the pipeline (D-2),
  snapshots it (D-6 rows, `Status = Queued`, `CurrentStageId` = first stage), saves. A unique
  violation on `IX_CardWorkflowRuns_CardId_Open` is caught and the winner re-read.
- `ResnapshotAsync(cardId, ct)`: marks the open run `Canceled` (`FailureReason = "resnapshot"`)
  and creates a new one from the CURRENT resolution. Explicit, never automatic.
- `CompleteAsync(card, terminalStatus)`: run → `Completed` (card Done) or `Canceled`
  (card Canceled, card archived).

Callers: `AgentTaskService.CreateAsync` for `Kind == Worker && IsStage(role) && binding.CardId != null`
(after `:487`, before the gate transaction at `:1055`); `POST /api/cards/{id}/workflow-run`
(idempotent, 200 with the run) and `POST /api/cards/{id}/workflow-run/resnapshot`;
`CardLifecycleTransitions` on Done/Canceled (replacing the null-out at `:77-78`);
`CardService.ArchiveAsync` cancels instead of refusing (`:916-927` becomes a cancel, because
there is no engine to stop and refusing would block archiving every InProgress card once runs
are real).

No backfill of the 729 existing cards: closed cards need no run; open cards get one on their
next stage task. The companion cards CARD-0552 creates get a run whose pointer jumps to
Mutation (D-8 skip rule); a `SourceLanding` Mutation task therefore links to a stage row like
any other stage task.

**Rejected:** creating the run at card creation or on the first column move. Two more writers
(card create, orchestrator card-spawn) for rows that only stage tasks read.

### D-8. The stage pointer follows bound stage tasks; status mirrors task activity

Writes, all on the existing task lifecycle transitions (no engine, no tick):

| Event | Run.Status | Stage row | CurrentStageId |
|---|---|---|---|
| snapshot | Queued | all Pending | stage 0 |
| stage task Dispatched/Working (`task.CardWorkflowStageId = S`) | Running | S → Running; earlier-ordered Pending stages → Skipped | S |
| task Succeeded, `NextStage` maps to role T in this run | Queued | S → Completed, `ResultSummary` = `NextHandoff` | T |
| task Succeeded, `NextStage` is land/decide/none, null, or a role NOT in this run | Queued | S → Completed | S |
| task Failed | Queued | S → Failed, `FailureReason` | S |
| task Blocked / Canceled | Queued | unchanged | S |
| card Done | Completed | | |
| card Canceled or archived | Canceled | | |
| resnapshot | old Canceled; new Queued | | |

`Running` therefore means "a stage task is live", which keeps `HomeTaskService.ClassifyCard`
(`:341-345`) and the archive guard truthful. `Stage` on the home rail (`HomeTaskDtos.cs:63-64`)
now shows the NEXT stage after a settle (the pointer), which is the "what is this card waiting
for" reading the rail wants. A re-entered stage (Review → code) goes Completed → Running again.

`AgentTask.CardWorkflowStageId` is set at create when the run has a stage row for the task's
role (Worker kind only; a sub-orchestrator with role Plan is not a stage, `InstructionBundles.cs:203-206`).
A stage-role task whose role is NOT in the run's definition is still created (never a gate) with
the existing `binding.Warning` channel carrying `pipeline: role X is not in <name> r<N>`.

### D-9. Prompt content stays git-owned; the definition carries keys and menus only

- `PipelineStageSpec.BundleKey` must exist in `InstructionBundles.All` and start with `stage-`
  (`InstructionBundles.IsStageBundleKey`). Adding a new stage prompt is: add
  `server/Bundles/stage-<name>.md` in a PR (hash-versioned, reviewed under §10), then reference
  it from a revision. The catalog test (`InstructionBundleTests.cs:132-142`) keeps pinning keys.
- `InstructionBundles.ForDelegate(kind, role, attached, stageBundleKey = null)`: when
  `stageBundleKey` is given and present in the catalog it replaces `StageKeyFor(role)`; when it
  names a bundle that no longer ships (renamed in a later PR) the role default is used and a
  `Warning`-severity `AgentTaskEvent` names the missing key (the attachment "dropped with a
  warning" rule, `AgentBundleAttachment.cs`, but never silently for a STAGE bundle). Both
  dispatcher call sites (`:4122`, `:4645`) pass `task.CardWorkflowStage?.BundleKey`, so drift
  after a resnapshot shows on the existing badge.
- `DelegationReportFormatter.StageHandoffContractFor(IReadOnlyList<string> allowedTokens)`
  renders `next: <review|code|decide>` from the stage row; the literal six-token form remains
  the fallback for a stage task with no stage row (helpers, sub-orchestrators, pre-rollout rows).
  The alias line is unchanged.
- The completion header gains a `pipeline=off` bit after `next=` when a parsed stage-role
  `next:` is outside the run's definition. `BuildReady` still projects the row (the orchestrator
  decides) and `AgentTaskPipelineReadyDto` gains `OffPipeline: bool = false` (trailing defaulted
  member, the CARD-0552 D-4 convention).

Explicit limits, written into the definition's doc section: `VerificationProfileBlock`, the
Shared-write commit line, Mutation admission, land refusal, `CommitOnSettleEligibility`,
`RolePolicy` tiers, WIP slots and pins are code keyed by `AgentTaskRole`. A definition cannot
change what a role MEANS to the server; it chooses which roles a card passes through, in what
order, under which standing prompt.

**Rejected:** DB-owned prompt text (a `PromptOverride` column). CARD-0058 D4 rejected per-project
bundle content; the hash stamp and drift badge would have nothing to hash; the §10 weekly review
edits prompts through git as Review cards.

**Rejected:** enforcing `AllowedNext` at settlement. CARD-0146: the handoff block is enrichment,
never a settlement gate; an off-menu `next:` is information for the orchestrator, not a failure.

### D-10. Gen-1 is removed by a follow-up card; gen-2 is kept; this card touches gen-1 twice

This card drops `CardWorkflowRun.WorkflowTemplateId` and `Agent.WorkflowRuns` (S1) and stops
`AssignCardAsync` calling the factory (D-11). Everything else gen-1 (`WorkflowEngine`,
`IStageExecutor`/`AgentExecutor`, `WorkflowTemplateService`, `CascadeService`, gates,
artifacts, `TemplateGroup`, `ModelRouting`, the `/workflows` nav, `DashboardPage`,
`WorkflowDetailPage`, `TemplateManager`, `Agent.DefaultWorkflowTemplateId` and the preset's
`FullFeaturePipelineTemplateId`, `SeededWorkflowTemplates`, 28 test files, 4 E2E tests, the
`Workflows`/`Stages`/`StageExecutions`/`GateDecisions`/`WorkflowTemplates`/`TemplateGroups`/
`ModelRoutings`/`ArtifactSectionReviews` tables) is untouched here and removed by
**follow-up card "Remove the gen-1 BMAD workflow engine"** whose Investigate must partition the
43-file grep into exclusive vs shared and decide the legacy non-markdown board-content branch in
`CardService.cs:1548-1565` / `OrchestratorService.cs:1023-1037`. `WorkflowDefinitionParser`
survives that removal (gen-2 needs `ParseYamlHooks`).

Gen-2 (`BoardWorkflowDefinition`, `WORKFLOW.md`, tracker block, hooks, `RunAttempt` pin) is
unchanged. Its owner doc gets one sentence pointing at pipelines for stage order.

**Rejected:** removing gen-1 in this card. It triples the Code round, puts the new tables at the
mercy of 28 test files of unrelated churn, and its own risks (dropping eight tables, the parser
split) deserve their own Review.

**Rejected:** keeping gen-1 indefinitely. 0 rows ever, reachable from the nav, and it holds the
word "Workflows" in the UI that this card's feature will need.

### D-11. Queue assignment assigns; it no longer creates runs

`AgentService.AssignCardAsync` keeps `AssignedAgentId` / `AgentQueuePosition` writes and
events and drops the factory call; `DELETE .../queue/{cardId}` stops nulling
`ActiveWorkflowRunId`; agent delete stops loading runs (`:787-815` shrinks to the card
unassignment). `CardWorkflowRunFactory` becomes `CreateFromRevision(card, definition, revision, now)`
(pure, no DB reads) used by `CardWorkflowRunService`. `ProjectCascade` keeps its run/stage
deletes minus the `AgentId` concern.

### D-12. Rollout under self-hosting: three land+restart steps, no compatibility worker

| Step | Lands | Running old server meanwhile | After restart |
|---|---|---|---|
| A (S1+S2+S6a) | schema, seed, definition CRUD, board/project pointers, script, API docs | inert: no code path reads the new tables until B | seeder creates the built-in; `GET /api/boards/{id}` shows `pipeline.source = builtin` |
| B (S3+S4+S6b) | run snapshot, task link, launch key, per-stage menu, header bit, ready `OffPipeline` | tasks created before restart carry no stage id and compose by role (today's behaviour); in-flight tasks keep launch bundles and settle under the unchanged alias map | the next card-bound stage task snapshots a run; warm reuse drift recomputes with the stage key |
| C (S5) | client settings panel + pickers | none (client only; rebuild `client/dist`) | operator can create/clone/point |
| Follow-up card | gen-1 removal | | |

Invariant that makes this safe: **no new `next:` token and no new role**. A report written under
the new per-stage menu is a strict subset of what the old parser accepts. Rollout probe after B
(rollout-owned, reported pending by Code): dispatch a stage task on a scratch card, confirm
`cardWorkflowStageId` on the task, `currentWorkflowStageName` on the card, the composed stamp
naming the stage key, and the header `next=` bit unchanged in shape.

## Out of scope (explicit)

- Per-definition tier, WIP or pin overrides (`RolePolicy` stays server-wide; pins stay
  stage-wide/per-card). A later card can add `PipelineStageSpec.ModelLevel` once the precedence
  against pins/chains/RolePolicy is decided.
- Per-definition display labels for stages; `STAGE_LABEL` stays keyed by role.
- New stage kinds or roles; enforcing `AllowedNext`; any engine, gate or automatic dispatch.
- Backfilling runs for closed cards; rewriting historical reports or headers.
- Gen-1 removal (follow-up card); gen-2 changes.
- Board-filtered pipeline glance (fleet-wide today; a definition per board makes the filter
  more useful, but it is a separate UI card).

## Implementation slices

| Slice | Files / work | Tests |
|---|---|---|
| **S1 Schema** | New `server/Domain/Entities/PipelineDefinition.cs`, `PipelineDefinitionRevision.cs`; `server/Domain/Enums/PipelineDefinitionSource.cs`; `server/Domain/ValueObjects/PipelineStageSpec.cs` (+ `PipelineStagesJson` serialise/parse/hash). Modify `Board.cs`, `Project.cs`, `CardWorkflowRun.cs`, `CardWorkflowStage.cs`, `Agent.cs` (drop `WorkflowRuns`), `AgentTask.cs` (`CardWorkflowStageId` + nav), `AppDbContext.cs` (entities, indexes, filtered unique, immutability guards, drop gen-1 FK config at `:1079-1082`). Migration `AddPipelineDefinitions` via CLI (data step deletes the legacy run/stages first). `ProjectCascade.cs`. | `tests/Antiphon.Tests/Migrations/PipelineDefinitionMigrationTests.cs` (up/down on a seeded DB with the legacy run; filtered unique index refuses a second open run); `KanbanPersistenceTests`, `ProjectDeletionTests` updated for the new columns. |
| **S2 Definitions** | `server/Application/Services/PipelineDefinitions.cs` (built-in constant + hash), `PipelineDefinitionService.cs` (list/get/create/clone/add-revision/archive; validation D-1/D-9), `PipelineResolution.cs`, `DatabaseSeeder.SeedPipelineDefinitionsAsync`; DTOs `PipelineDefinitionDtos.cs`; `server/Api/Endpoints/PipelineDefinitionEndpoints.cs` (`GET/POST /api/pipeline-definitions`, `GET /{id}`, `POST /{id}/revisions`, `/{id}/clone`, `/{id}/archive`, `/{id}/unarchive`); `BoardService`/`ProjectService` + their update DTOs gain the pointer fields and expose `pipeline`; `Program.cs` registrations; `scripts/pipeline-definition.ps1` (list, get, clone, revise `-StagesFile`, set-board, set-project). | `PipelineDefinitionServiceTests` (validation matrix: non-stage role, duplicate role, unknown/non-stage bundle key, next outside the revision, empty stages, built-in write refusal, clone copies active revision, archive refused while referenced? no: archive allowed, pointer-write refused), `PipelineResolutionTests` (board > project > built-in, archived-but-referenced still resolves), `DatabaseSeederTests` (D-5 three cases), endpoint tests in the existing `WebApplicationFactory` style, `PipelineDefinitionScriptTests` against the stub (the `RoutingPinScriptTests` pattern). |
| **S3 Run snapshot** | `CardWorkflowRunFactory.cs` rewritten (pure), new `CardWorkflowRunService.cs` (D-7), `AgentTaskService.CreateAsync` call, `CardEndpoints` (`POST /api/cards/{id}/workflow-run`, `/resnapshot`), `CardLifecycleTransitions.cs` (complete instead of null), `CardService.ArchiveAsync` (cancel instead of refuse), `AgentService.cs` (D-11), `CardWorkTransitionService` untouched. | `CardWorkflowRunServiceTests` (ensure idempotent; concurrent ensure → one run; resnapshot cancels+creates; Done → Completed; Canceled/archive → Canceled), `AgentTaskCreateSnapshotsRunTests` (stage role + card → run + stage id; helper role → none; Orchestrator kind → none; off-definition role → created with warning), `AgentServiceIntegrationTests` (queue assignment creates no run), `CardLifecycleTransitions` tests, `HomeTaskServiceIntegrationTests` (Running only while a stage task is live). |
| **S4 Launch + handoff wiring** | `InstructionBundles.ForDelegate` (+`stageBundleKey`, `IsStageBundleKey`), `AgentTaskDispatcher.cs` `:4122` and `:4645`, `DelegationReportFormatter.StageHandoffContractFor` + header bit, `AgentTaskReplyService.cs` `:665-668` (stage row transitions per D-8 table), dispatcher `Dispatched` transition (stage Running + Skipped rule), `AgentTaskPipelineStatusService.BuildReady` (`OffPipeline`), `AgentTaskPipelineDtos.cs`, `AgentTaskDtos.cs` (`cardWorkflowStageId`), client type parity only (`agentTasks.ts`). | `InstructionBundleTests` (explicit key wins; missing key falls back + warning event; non-stage key refused at validation), `DelegateBundleLaunchTests` (launch composes from the stage row; drift after resnapshot), `DelegationReportFormatterTests` (menu subset; fallback literal), `AgentTaskReplyService`/`PipelineHandoff` tests (each D-8 row), `AgentTaskPipelineStatusTests` (`OffPipeline` true/false; existing 29 unchanged), `MutationDispatchTests` (SourceLanding task links to the companion run's Mutation stage). |
| **S5 Client** | `client/src/api/pipelineDefinitions.ts`; `client/src/features/settings/PipelineDefinitionsPanel.tsx` (list, revisions, clone, editor with ordered stage rows: role select limited to stage roles, bundle key select from `GET /api/agents/bundles` (`AgentEndpoints.cs:62`, already used by the attachments modal via `agents.ts:541`) filtered to `stage-*`, allowed-next multiselect, change note); board settings picker (`inherit` / named) and project settings picker; `CardModal` stage line gains `<definition> r<N>` in a tooltip; MSW handlers. | Vitest: panel renders and saves a revision; picker writes the pointer; card stage tooltip. `pwsh -File scripts/test-client.ps1`. |
| **S6 Docs** | S6a with A: `docs/antiphon-api.md` (routes), `docs/ops-http.md` (one row), `docs/agent-card-lifecycle.md` (run status ↔ card status table). S6b with B: `docs/orchestration-loop.md` §1 (definition selects the subset; menu per stage; `pipeline=off`), `server/Bundles/README.md` ("which bundle" now reads the stage row, role map is the fallback), `docs/workflow-tracker-block.md` (one pointer sentence), `AGENTS.md` table row only if a new owner doc is created (default: no, §1 of the loop doc owns it). | Doc-substring pins in a new `PipelineDefinitionDocumentationTests` (the `CommitOnSettleDocumentationTests` pattern, `tests/Antiphon.Tests/Application/`). |

**Dispatch grouping (default D-12):** A = S1+S2+S6a (Worktree, Code, Frontier; land; restart).
B = S3+S4+S6b (Worktree, Code, Frontier; land; restart; rollout probe). C = S5 (Worktree, Code,
High; land; client rebuild). Follow-up card filed at settlement for gen-1 removal. A and B could
be one dispatch if the operator prefers one restart; C cannot precede A.

## TestDesign handoff

TestDesign is a separate stage. The verification design must pin, at minimum:

- D-1/D-9 validation matrix as unit tests on `PipelineDefinitionService` with exact error codes.
- D-5 seeder idempotency and revision append (three boots).
- D-6 migration: legacy row deletion, NOT NULL adds, the filtered unique index, `Down`.
- D-7 concurrency: two concurrent `EnsureActiveRunAsync` on one card yield one run.
- D-8 every row of the transition table, including Skipped, re-entry and off-definition `next:`.
- D-9 launch composition from the stage key at both dispatcher sites, drift after resnapshot,
  fallback with a Warning event when the key vanished; the per-stage menu and its fallback.
- D-12 the invariant "no new token": `PipelineHandoff` alias table unchanged (existing pins),
  and a rollout probe B-1 owned by the operator after the B restart.
- Regression: the 29 `AgentTaskPipelineStatusTests`, 38 `InstructionBundleTests`,
  15 `DelegateBundleLaunchTests`, 4 `MutationDispatchTests` stay green unedited except where a
  named arm is extended.
