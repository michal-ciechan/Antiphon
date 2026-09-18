# CARD-0558: Stages as shareable per-project pipelines, and the definition object that names them

Plan task `a8f042bb`, 2026-09-17, inspected checkout `3a62074e` (master) plus the investigation
commit `40527766` cherry-picked onto this branch as `5108bc9b`. Investigation:
[2026-09-17-card-0558-stages-as-shareable-workflows.md](../../investigations/2026-09-17-card-0558-stages-as-shareable-workflows.md)
(task `7102ff54`). Design authority this plan builds on: CARD-0146 plan ("add roles; do not add
a stage enum", `2026-09-03-card-0146-stage-pipeline-handoff-plan.md:31`), CARD-0058 spec D4
(no DB-backed bundle content), CARD-0470 D-8 (self-hosted rollout), and the two pin-at-use
precedents `AgentTuiProfileRevision` / `RunAttempt.BoardWorkflowDefinitionId`.

**Amended 2026-09-18 (Plan task `f012a1d3`, same inspected checkout `3a62074e`; master had not
moved).** The operator answered both open decisions: D-10 is REVERSED (gen-1 BMAD engine removal
is in this card, as its own dispatch group D) and D-12 is CONFIRMED (A/B/C as planned, each landed
and restarted separately). The amendment adds a re-verified gen-1 inventory to §Ground truth,
rewrites D-10, adds D-13 (removal boundary), D-14 (sequencing) and D-15 (migration), slices
S7a..S7c, and a group-D verification inventory. Everything else (`PipelineDefinition` /
`PipelineDefinitionRevision` shape, `CardWorkflowRun` re-pointing, git-owned prompt content,
A/B/C contents) is unchanged.

This is a large architecture card. The honest scope is four Code dispatches; §Implementation
slices says which slice goes in which dispatch and why. Written under stated defaults
(D-1..D-15); no decision is open.

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
5. **Generations:** gen-1 BMAD engine is REMOVED in this card as its own dispatch group D,
   sequenced after B and before C (D-10 reversed by the operator; boundary in D-13, sequencing in
   D-14, migration in D-15); gen-2 `BoardWorkflowDefinition` / `WORKFLOW.md` is KEPT unchanged
   (tracker config + spawn prompt + hooks, not stages); gen-3 rows are REUSED as above. Queue
   assignment stops creating runs (D-11).
6. **Rollout is safe under self-hosting because no new `next:` token is introduced.** Four
   land-then-restart steps: schema + definitions + pointers (inert); run snapshot + launch/handoff
   wiring; gen-1 removal (a pure deletion once B has re-pointed the run path); client. In-flight
   tasks keep their launch bundles and settle under the unchanged alias map (D-12).

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
| Self-hosted rollout is a special dance. | CARD-0470 D-8 (`2026-09-09-card-0470-code-mutation-split-plan.md:109-117`) needed a temporary Debug worker because the OLD server could not parse `next: mutation`. Here the alias map (`PipelineHandoff.cs:24-45`) is unchanged and no token is added. Warm reuse keeps launch-time bundles; drift is recomputed at `AgentTaskDispatcher.cs:4645`. | D-12: four land+restart steps (A, B, D, C), none needing a compatibility worker. |
| Gen-1 removal is "33 server + 31 client files" (investigation §6). | Re-verified on `3a62074e`, 2026-09-18, by reachability rather than by name. **Exclusive server files: 55**, deleted outright: 5 services (`WorkflowEngine` 915 lines, `CascadeService`, `WorkflowTemplateService`, `FeatureStatusService`, `CostTrackingService`), `IStageExecutor`, 4 endpoint files (`Workflow`/`Gate`/`Cascade`/`ArtifactEndpoints`), the executor stack (`AgentExecutor`, `MockExecutor`, `EventEmittingToolWrapper`, `ToolRegistry`, `AgentToolAIFunction`, `CachingChatClient`, `Infrastructure/Agents/Tools/*` 9 files; no test and no reference outside the stack), 2 hosted services that poll `db.Workflows` (`GitHubMonitorService.cs:82`, `ExternalChanges/ChangeDetectionService.cs:87`), `Domain/StateMachine/WorkflowStateMachine`, 9 entities (`Workflow`, `Stage`, `StageExecution`, `GateDecision`, `WorkflowTemplate`, `TemplateGroup`, `ModelRouting`, `ArtifactSectionReview`, `CostLedgerEntry`), 4 enums (`WorkflowStatus`, `StageStatus`, `CascadeAction`, `GateAction`), 13 DTO files; 5,128 lines. **Shared server files trimmed: 24** (D-13 middle column). **Client: 52 exclusive files, 8,080 lines** (`features/workflow/**` 22, `features/dashboard/**` 9, `features/artifact/**` 12, `api/{workflows,gates,artifacts,audit,cascade,conversation}.ts`, `TemplateManager.tsx`, `streamingStore.ts`, `useStreamingEvents.ts`; the only importers outside those directories are `App.tsx`, `App.test.tsx`, `SettingsPage.tsx`, `ProviderConfig.tsx` (provider hooks only)), plus ~28 shared client files: `defaultWorkflowTemplateId` in 16 test fixtures and 1 story, the `/workflows` route and nav (`Layout.tsx:73`), six gen-1 rows in `useSignalRInvalidation.ts:27-56`, and five npm packages used only there (`@tiptap/pm`, `@tiptap/react`, `@tiptap/starter-kit`, `tiptap-markdown`, `mermaid`). **Tests: 6 files deleted** (`WorkflowEngineTests` 15, `WorkflowEngineParsingTests` 13, `WorkflowStateMachineTests` 23, `SeededWorkflowTemplates` helper, E2E `WorkflowDeleteTests` 6 + `WorkflowOutputTests` 4 = 61 tests), ~20 test files trimmed of template fixtures. The investigation undercounted because it grepped two names; the audit/cost ledger edges, the tool stack, the two pollers and the state machine are reachable only from the gen-1 graph. | D-10 reversed: removal is this card's group D. D-13 fixes the boundary by reachability. |
| Gen-1 has zero live use. | Live DB 2026-09-18: `Workflows` 0, `Stages` 0, `StageExecutions` 0, `GateDecisions` 0, `ArtifactSectionReviews` 0, `CostLedgerEntries` 0; seed-only rows `WorkflowTemplates` 3, `TemplateGroups` 1, `ModelRoutings` 10; `Agents.DefaultWorkflowTemplateId` set on 3 (all from the orchestrator preset, `AgentPresets.cs:23,41`); `AuditRecords` 1,453 rows, **0** with any gen-1 FK (1,452 `SessionDisconnected` from `AntiphonHub.cs:77`, 1 `ToolInvocation` from `AgentTuiProfileService.cs:2070`); `CardWorkflowRuns` 1 (the CARD-0001 row A deletes). `BoardWorkflowDefinitions`: 3 rows, all Markdown front matter, 0 legacy YAML, 0 containing `stages:`. No script, bundle, skill, doc other than `docs/antiphon-api.md` and `docs/project-context.md` naming examples, or E2E test navigates to `/workflows` or `/api/settings/templates` (`scripts/perf/page-load-probe.py:55` lists the route as a perf probe; `scripts/verify-agent-tui-profile.ps1:200` echoes the agent field). | Nothing to migrate; the migration deletes seed rows only (D-15). |
| Audit and cost ledger are gen-1. | Half. `AuditRecords` is LIVE (the two writers above, `DataRetentionService.cs:59` archives it, `AuditArchiveEndpointTests` pins `DELETE /api/audit/archive`) but carries three nullable gen-1 FKs (`WorkflowId`, `StageId`, `StageExecutionId`, `AppDbContext.cs:536-549`) that `AuditService.RecordEventAsync` takes as parameters (`:108-118`) and `QueryAsync` filters on (`:224-240`). `CostLedgerEntries` REQUIRES `WorkflowId`/`StageId` (`CostLedgerEntry.cs:40-41`), is written only by `AgentExecutor` through `CostTrackingService`, read only by `GET /api/audit/cost-summary` and `/cost-ledger`, 0 rows. `GET /api/audit/conversation` reads by `workflowId`. `LlmProviders` (3 rows, `ProviderConfig.tsx`, `docs/agent-credentials.md:31`, `LlmClientFactory` reads `LlmSettings` not the table) has no gen-1 edge once `ModelRouting` goes. | D-13: keep `AuditRecords`, `AuditService`, `GET /api/audit` and `DELETE /api/audit/archive` minus the gen-1 columns, parameters and filters; delete `CostLedgerEntries`, `CostTrackingService`, `/cost-summary`, `/cost-ledger`, `/conversation`; keep `LlmProvider` and its panel; delete `ModelRouting`. `AuditEventType` values are not renumbered. |
| The gen-1 YAML parser is shared with gen-2. | `WorkflowDefinitionParser.ParseYamlHooks` (`:68`) is the only gen-2 need (`WorkflowDefinitionLoader.cs:156`). `ParseYamlDefinition` (`:11`) is called by the legacy non-Markdown board-content branch in `CardService.BuildPrompt` (`:1555`), `OrchestratorService.BuildPrompt` (`:1026`), `AgentSessionService.ParseHooks` (`:2918`, only when the content has both `hooks:` and `stages:`), `CardWorkflowRunFactory.cs:37` (rewritten in S3) and `SettingsEndpoints.cs:70` (via `WorkflowEngine.ParseYamlDefinition`, `:51`). Seven test files seed `BoardWorkflowDefinition.Content` with gen-1 YAML (`name:/stages:/executorType:`; `AgentChannelServiceIntegrationTests:365,509`, `AgentControlServiceIntegrationTests:1916`, `AgentServiceIntegrationTests:1705`, `AgentSessionServiceIntegrationTests:1371`, `BoardServiceIntegrationTests:568`, `OrchestratorServiceIntegrationTests:1401`, `RealCliStubBServerHarness:228`) but no test asserts the appended `Workflow: … (executorType)` prompt lines. `WorkflowDefinitionLoader.Parse` (`:126-138`) refuses content not starting with `---`, so YAML content cannot enter through the API. | D-13: delete `ParseYamlDefinition`, the `WorkflowDefinition`/`StageDefinition` records and the three legacy branches (`BuildPrompt` returns the plain prompt for non-Markdown content; `ParseHooks` falls through to `ParseYamlHooks`, which reads the root `hooks:` key regardless of sibling keys). `WorkflowHooks`/`WorkspaceHookDefinition` and `ParseYamlHooks` stay. The seven fixtures stay unedited and must stay green: that is the proof the branch removal is unobservable. |
| Readiness and presets reference templates. | `ProjectSetupService.WorkflowTemplateCheck` (`:696-720`, `ReadinessKeys.WorkflowTemplate = "workflow-template"`, level Required) fails a project's readiness when no template row exists; `AgentPresetDto.DefaultWorkflowTemplateId` (`ProjectSetupDtos.cs:93`), `AgentPresets.FullFeaturePipelineTemplateId` (`:23`), `AgentService.EnsureWorkflowTemplateExistsAsync` (`:1027-1034`) plus `Include` (`:77`, `:1011`) and two mappers (`:1161`, `:1246`), `AgentDto`/`CreateAgentRequest`/`UpdateAgentRequest`/catalog (`AgentDtos.cs:21,86,299,349`). The client renders no readiness row keyed on `workflow-template` specifically; `AgentCreateModal.tsx:237` and `ProjectSetupModal.tsx:288` show a preset hint, `AgentSettingsModal.tsx:189` echoes the field on update. | D-13: the readiness check, the preset field, the agent field and the FK go together; `ProjectReadinessTests:409-421,619`, `AgentPresetsTests:23,93,111`, `InstructionBundleTests:123-128` pins are deleted, not rewritten, and one positive pin replaces them (readiness has no `workflow-template` key). |
| Queue assignment is the only run creator. | `AgentService.AssignCardAsync` (`:826-872`) calls `CardWorkflowRunFactory.CreateFromAgentDefaultAsync` (`:21-37`), which loads a gen-1 template; `DELETE .../queue/{cardId}` nulls the link; agent delete removes the agent's runs (`:787-815`); `CardLifecycleTransitions.cs:70-80` nulls on Review/Done/Canceled dequeue. `POST /api/agents/{id}/queue` has one client caller (`agents.ts:703`). | D-11: queue assignment assigns only; the factory is rewritten to take a revision; agent delete no longer touches runs (Card cascade owns them); `CardLifecycleTransitions` stops nulling the run and instead completes it (D-8). |
| Launch composes bundles by role. | `AgentTaskDispatcher.cs:4121-4122` composes `InstructionBundles.ForDelegate(task.Kind, task.Role, attachedBundleKeys)`; `:4645` recomputes the desired composition for drift. `attachedBundleKeys` come from `AgentBundleAttachments.LoadAsync` (`:3419`, `:3650`). | D-9: `ForDelegate` gains an optional `stageBundleKey`; both call sites pass the task's stage-row key; `StageKeyFor` stays the fallback. |
| Task creation knows the card and board. | `AgentTaskCardBinder.BindAsync` (`AgentTaskService.cs:487`) yields `CardId` only; the open gate runs per project+role (`DelegationOpenGate.cs:63-67`); the task row is initialised at `:966-979`. | D-7: `EnsureActiveRunAsync(cardId)` runs after binding and before the gate transaction; it loads `Card`+`Board`+`Project` once for stage-role Worker tasks with a card. |
| Migrations. | CLI-generated only (`docs/project-context.md:125`); latest `20260917012003_AddVerificationProfile`; migration tests live in `tests/Antiphon.Tests/Migrations/` (`CommitOnSettleMigrationTests.cs`). | One migration `AddPipelineDefinitions` (S1); a migration test in the same folder. |
| Seeding. | `DatabaseSeeder` re-asserts gen-1 templates every boot (`:47-51`, `:90-119`); `DatabaseSeederTests` pins that (3 tests: 2 template, 1 routing). The provider seed and config sync (`:49-50`, `:175-260`) are live. | D-5: the built-in pipeline is seeded from a code constant; a content change appends a revision and moves the active pointer; operator definitions are never touched by the seeder. S7a removes the template/group/routing passes and rewrites `DatabaseSeederTests` to pin providers + config sync. |

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

### D-10. Gen-1 is removed in this card as dispatch group D; gen-2 is kept

*Operator decision 2026-09-18, reversing the plan's default.* Everything gen-1 (`WorkflowEngine`,
`IStageExecutor`/`AgentExecutor` and its tool stack, `WorkflowTemplateService`, `CascadeService`,
gates, artifacts, `FeatureStatusService`, `CostTrackingService`/`CostLedgerEntries`,
`TemplateGroup`, `ModelRouting`, `WorkflowStateMachine`, the two `db.Workflows` pollers, the
`/workflows` nav, `DashboardPage`, `WorkflowDetailPage`, `TemplateManager`, the streaming store,
`Agent.DefaultWorkflowTemplateId` and the preset's `FullFeaturePipelineTemplateId`, the
`workflow-template` readiness check, the seeder's template/group/routing passes,
`SeededWorkflowTemplates`, the nine gen-1 tables) is removed by slices S7a..S7c in one Code
dispatch, group D, sequenced after B and before C (D-14). What A and B already own (the
`CardWorkflowRun.WorkflowTemplateId` FK, `Agent.WorkflowRuns`, `CardWorkflowRunFactory`, queue
assignment, the CARD-0001 legacy run row) stays where the plan put it; D never edits the run
path. The partition of the tree into delete / trim / keep is D-13; the migration is D-15.

Gen-2 (`BoardWorkflowDefinition`, `WORKFLOW.md`, tracker block, hooks, `RunAttempt` pin) is
unchanged. Its owner doc gets one sentence pointing at pipelines for stage order. The one gen-2
dependency on gen-1 code, `WorkflowDefinitionParser.ParseYamlHooks`, survives (D-13).

**Rejected (was the default):** a follow-up card. The operator's reasons are the investigation's
own: 0 rows ever, reachable from the nav, and it holds the word "Workflows" in the UI. The cost
the default was avoiding (one more Code round, nine table drops, the parser split) is contained
by making D a pure deletion after B instead of interleaving it with the new build.

**Rejected:** folding removal into A or C. A is schema-heavy in the same files (`AppDbContext`,
`DatabaseSeeder`, `AgentService`, `Program.cs`) and would double its review surface for no
shared logic; C is client-only while D is 55 server files to 52 client files, and D must land
before C (D-14).

**Rejected:** keeping gen-1 indefinitely. Same three reasons.

### D-11. Queue assignment assigns; it no longer creates runs

`AgentService.AssignCardAsync` keeps `AssignedAgentId` / `AgentQueuePosition` writes and
events and drops the factory call; `DELETE .../queue/{cardId}` stops nulling
`ActiveWorkflowRunId`; agent delete stops loading runs (`:787-815` shrinks to the card
unassignment). `CardWorkflowRunFactory` becomes `CreateFromRevision(card, definition, revision, now)`
(pure, no DB reads) used by `CardWorkflowRunService`. `ProjectCascade` keeps its run/stage
deletes minus the `AgentId` concern.

### D-12. Rollout under self-hosting: four land+restart steps, no compatibility worker

*Confirmed by the operator 2026-09-18 for A/B/C; D added by D-10/D-14.*

| Step | Lands | Running old server meanwhile | After restart |
|---|---|---|---|
| A (S1+S2+S6a) | schema, seed, definition CRUD, board/project pointers, script, API docs | inert: no code path reads the new tables until B | seeder creates the built-in; `GET /api/boards/{id}` shows `pipeline.source = builtin` |
| B (S3+S4+S6b) | run snapshot, task link, launch key, per-stage menu, header bit, ready `OffPipeline` | tasks created before restart carry no stage id and compose by role (today's behaviour); in-flight tasks keep launch bundles and settle under the unchanged alias map | the next card-bound stage task snapshots a run; warm reuse drift recomputes with the stage key |
| D (S7a+S7b+S7c) | gen-1 removal: 55 server + 52 client files deleted, ~24 + ~28 trimmed, migration `RemoveGen1WorkflowEngine`, docs, `client/dist` rebuild | old server keeps serving `/workflows` and `/api/settings/templates` until the restart; nothing else reads the tables the migration will drop | `/api/workflows*`, `/api/settings/templates*` 404; nav has no Workflows; seeder no longer writes templates; `Agents.DefaultWorkflowTemplateId` gone |
| C (S5) | client settings panel + pickers | none (client only; rebuild `client/dist`) | operator can create/clone/point |

Invariant that makes this safe: **no new `next:` token and no new role**. A report written under
the new per-stage menu is a strict subset of what the old parser accepts. Rollout probe after B
(rollout-owned, reported pending by Code): dispatch a stage task on a scratch card, confirm
`cardWorkflowStageId` on the task, `currentWorkflowStageName` on the card, the composed stamp
naming the stage key, and the header `next=` bit unchanged in shape.

### D-13. Removal boundary: reachability from the gen-1 graph, not the word "workflow"

Rule: a type is DELETED when every reference to it comes from another deleted type or from a
gen-1 table; TRIMMED when it has live references and a gen-1 edge; KEPT untouched when it has no
gen-1 edge once the deletions are done. Applied on `3a62074e`:

| Delete (exclusive) | Trim (shared; only the gen-1 edge goes) | Keep (no edge after removal) |
|---|---|---|
| Services `WorkflowEngine`, `CascadeService`, `WorkflowTemplateService`, `FeatureStatusService`, `CostTrackingService`; `Interfaces/IStageExecutor`; endpoints `WorkflowEndpoints` (incl. `GET /api/projects/{id}/feature-status/{name}`), `GateEndpoints`, `CascadeEndpoints`, `ArtifactEndpoints`; `Infrastructure/Agents/{AgentExecutor,MockExecutor,EventEmittingToolWrapper,ToolRegistry,AgentToolAIFunction,CachingChatClient}.cs` and `Infrastructure/Agents/Tools/**`; `Infrastructure/GitHub/GitHubMonitorService.cs`; `Infrastructure/ExternalChanges/**`; `Domain/StateMachine/WorkflowStateMachine.cs`; entities `Workflow`, `Stage`, `StageExecution`, `GateDecision`, `WorkflowTemplate`, `TemplateGroup`, `ModelRouting`, `ArtifactSectionReview`, `CostLedgerEntry`; enums `WorkflowStatus`, `StageStatus`, `CascadeAction`, `GateAction`; DTOs `WorkflowDto`, `WorkflowDetailDto`, `StageDto`, `StageDefinitionDto`, `WorkflowDeleteInfoDto`, `CreateWorkflowRequest`, `ConversationEntryDto`, `FeatureStatusDto`, `CreateWorkflowTemplateRequest`, `UpdateWorkflowTemplateRequest`, `WorkflowTemplateDto`, `TemplateGroupDto`, `ModelRoutingDto`. Client `features/workflow/**`, `features/dashboard/**`, `features/artifact/**`, `api/{workflows,gates,artifacts,audit,cascade,conversation}.ts`, `features/settings/TemplateManager.tsx`, `stores/streamingStore.ts`, `hooks/useStreamingEvents.ts`. Tests `WorkflowEngineTests`, `WorkflowEngineParsingTests`, `Domain/StateMachine/WorkflowStateMachineTests`, `TestHelpers/SeededWorkflowTemplates`, E2E `WorkflowDeleteTests`, `WorkflowOutputTests`. | `AuditRecord` + `AuditRecordDto.cs` (drop `WorkflowId`/`StageId`/`StageExecutionId`, navs, `CostLedgerEntryDto`, `CostSummaryDto`); `AuditService` (drop `RecordLlmCallAsync`, `RecordToolInvocationAsync`, `GetConversationAsync`; `RecordEventAsync` and `QueryAsync` lose the gen-1 ids); `AuditEndpoints` (keep `GET /api/audit`, `DELETE /api/audit/archive`); call sites `AgentTuiProfileService.cs:2070`, `AntiphonHub.cs:77`; `SettingsEndpoints` (keep the `/api/settings/providers` group only); `LlmProvider` (drop `ModelRoutings`), `LlmProviderService` (drop routing methods); `Agent`, `AgentDtos` (4 sites), `AgentPresets` (4 sites), `AgentService` (6 sites), `ProjectSetupService` (`WorkflowTemplateCheck`, `anyTemplate`), `ProjectSetupDtos` (`ReadinessKeys.WorkflowTemplate`, preset field); legacy YAML branches in `CardService.BuildPrompt`, `OrchestratorService.BuildPrompt`, `AgentSessionService.ParseHooks`; `WorkflowDefinitionParser` (drop `ParseYamlDefinition`); `ValueObjects/WorkflowDefinition.cs` → `WorkflowHooks.cs` keeping `WorkflowHooks` + `WorkspaceHookDefinition`; `ProjectCascade.cs:54,142` (`WorkflowCount`); `AppDbContext` (9 `DbSet`s; config `:289-360`, `:390-514`, `:557-596`, `:881-884`, `:1513-1530`; the `AuditRecord` FKs `:536-549`); `DatabaseSeeder` (drop template/group/routing passes and their ids; keep providers + config sync); `Program.cs` (registrations `:298`, `:473-480`, `:630-631`, hosted `:644-645`, maps `:854-858`, the startup-gate comment `:47`); `E2E/Fixtures/AntiphonAppFixture` (drop `UseMockExecutor` and the executor swap `:687-696`; `OutputDistillationApplyCanaryTests:58`). Client `App.tsx`, `App.test.tsx`, `shared/Layout.tsx:73`, `SettingsPage.tsx` (+test), `api/settings.ts` (keep `LlmProviderDto`, provider hooks, `TestProviderResult`), `api/agents.ts`, `api/projectSetup.ts`, `AgentCreateModal.tsx`, `AgentSettingsModal.tsx`, `ProjectSetupModal.tsx` (+test), `hooks/useSignalRInvalidation.ts` (drop `WorkflowStatusChanged`, `StageCompleted`, `GateReady`, `GateActioned`, `ArtifactUpdated`, `CascadeTriggered`), `test/mocks/handlers.ts`, 12 agent + 4 home test fixtures, `stories/card0417/ReplyStyleNarrow.stories.tsx`, `package.json` (+ lockfile); `scripts/perf/page-load-probe.py:55`, `scripts/verify-agent-tui-profile.ps1:200`; ~20 server test files (template fixtures only). | `LlmProviders` table, `ProviderConfig.tsx`, `/api/settings/providers/*`, `LlmSettings`, `LlmClientFactory`, `AgentDraftGenerator`; `AuditRecords` table and `AuditEventType` (values untouched; 2-16 documented as reserved); `GitHubService`, `GitHubRepoCache`, `GitHubRepoCacheWarmupService`, `GitHubEndpoints`; `GitSettings` (`PollIntervalSeconds` becomes unread; left for a config-cleanup card); `WorkflowDefinitionParser.ParseYamlHooks`, `WorkflowDefinitionLoader`, `WorkflowDefinitionVersionGate`, gen-2 entirely; `CardWorkflowRun`/`CardWorkflowStage`/`CardWorkflowRunFactory` (A and B own them); `StageOutcome*`, `OrchestrationStage` (live, CARD-0470); `AgentSessionRuntime.cs:128` (`AgentTextDelta` is the live session-dock event, unrelated); the seven test fixtures that seed legacy YAML board content; `docs/features/**` history; `AgentTaskCostWalk`/`DelegationCost` (live cost tracking, unrelated to the ledger). |

**Rejected:** removing `LlmProvider`/`ProviderConfig` because it shipped with gen-1
(`20260316232235_AddLlmProviderAndModelRouting`). It is the documented custody surface for the
server's own LLM key (`docs/agent-credentials.md:31`), has a `/test` route and 3 live rows, and
its only gen-1 edge is `ModelRouting`.

**Rejected:** dropping `AuditRecords` or renumbering `AuditEventType`. 1,453 live rows, two live
writers, the retention job. Column drops only; the enum keeps its members so no row is
re-interpreted.

**Rejected:** keeping `ParseYamlDefinition` in case a board still has YAML content. 0 such rows,
and `WorkflowDefinitionLoader.Parse` already refuses content that does not start with `---`, so
the legacy branch cannot be reached through the API.

**Rejected:** editing the seven legacy-YAML test fixtures. They are `BoardWorkflowDefinition`
rows used as inert board decoration by tests about other things; leaving them is the regression
proof, editing them widens D's review for cosmetics.

### D-14. Group D lands after B and before C

| Order | Reason |
|---|---|
| after A | A's migration drops `CardWorkflowRuns.WorkflowTemplateId` and its FK config (`AppDbContext.cs:1079-1082`) and deletes the CARD-0001 run; D's migration then drops `WorkflowTemplates` without touching the run tables. D first would force D to edit the run path A/B own. |
| after B | B rewrites `CardWorkflowRunFactory` (the last production reader of `ParseYamlDefinition` outside the three legacy branches) and stops `AssignCardAsync` loading a template; the tests B rewrites (`AgentServiceIntegrationTests:582-665`, `BoardServiceIntegrationTests:35-77`, E2E `AgentE2ETests`, `KanbanPersistenceTests:87-148`, `ProjectReadinessTests:673`, `ProjectSetupServiceTests:223`) are exactly the ones D would otherwise rewrite twice. After B, D is a deletion with a compile-fix loop and no behaviour change on the run path. |
| before C | C adds a `pipelines` tab to `SettingsPage.tsx`; D removes the `templates` tab from the same file and makes `projects` the default. Landing D first makes C's edit an addition on a settled file, and C never has to decide what to do with the `Workflows` nav slot. |

Rollout: D is one land+restart plus a `client/dist` rebuild. The migration is irreversible for
seed data (D-15), but the old server keeps running until the restart, and nothing reads the
dropped tables except code deleted in the same commit. After the restart `GET /api/workflows`
is 404 and the rebuilt bundle has no route for it.

**Rejected:** D in parallel with C in a second worktree. The operator's standing preference is
sequential dispatch, and the overlap (`SettingsPage.tsx`, `App.tsx`, `package.json`) would need a
hand merge in either direction.

**Rejected:** D before A. Row 1; and D would then be the one to delete the CARD-0001 legacy run,
which A's migration already does alongside the schema change that makes it unrepresentable.

### D-15. Migration `RemoveGen1WorkflowEngine` (CLI-generated, one migration)

```
DROP FK/IX/columns  AuditRecords.WorkflowId, .StageId, .StageExecutionId
DROP FK/IX/column   Agents.DefaultWorkflowTemplateId        (3 live agents keep every other column)
DROP TABLE          CostLedgerEntries, ArtifactSectionReviews, GateDecisions, StageExecutions,
                    Stages, Workflows, ModelRoutings, WorkflowTemplates, TemplateGroups
                    (dependency order; 0 rows in the first six, 10 + 3 + 1 seed rows in the last three)
```

`Down` is what the CLI generates: tables and columns come back empty. The seed rows are not
restored (the seeder that wrote them is deleted in the same commit); accepted because no live
path ever read them. `AppDbContextModelSnapshot` is regenerated by the CLI, and the existing
`HasPendingModelChanges` guard (`AgentTaskInternalDecisionMigrationTests` pattern) catches a
model/migration mismatch. A migration test in `tests/Antiphon.Tests/Migrations/` applies Up on
a database seeded with three templates, one group, ten routings, an agent pointing at a
template and an `AuditRecord`, asserts the nine tables are gone, the agent survives without the
column, the audit row survives with its `EventType`; then applies Down and asserts the tables
exist and are empty. `CardWorkflowRuns` is not mentioned here: A already removed its gen-1 FK.

## Out of scope (explicit)

- Per-definition tier, WIP or pin overrides (`RolePolicy` stays server-wide; pins stay
  stage-wide/per-card). A later card can add `PipelineStageSpec.ModelLevel` once the precedence
  against pins/chains/RolePolicy is decided.
- Per-definition display labels for stages; `STAGE_LABEL` stays keyed by role.
- New stage kinds or roles; enforcing `AllowedNext`; any engine, gate or automatic dispatch.
- Backfilling runs for closed cards; rewriting historical reports or headers.
- Gen-2 changes. Removing `LlmProvider`/`ProviderConfig`; renumbering `AuditEventType`;
  removing the now-unread `GitSettings.PollIntervalSeconds` (a config-cleanup card).
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
| **S7a Gen-1 server removal** (group D) | `git rm` the 55 exclusive server files and 4 server test files in D-13's first column, whole directories for `Infrastructure/Agents/Tools/` and `Infrastructure/ExternalChanges/`. Trim the shared server files in D-13's middle column. Resulting signatures: `AuditService.RecordEventAsync(eventType, summary, clientIp, userId, gitTagName, fullContentJson, ct)`, `QueryAsync(from, to, minCost, maxCost, skip, take, ct)`; `SettingsEndpoints` maps only `/api/settings/providers`; `ValueObjects/WorkflowDefinition.cs` becomes `WorkflowHooks.cs`; `DatabaseSeeder.SeedAsync` = providers + config sync + the S2 pipeline seed; `ProjectSetupService` readiness list loses `WorkflowTemplateCheck`; `AgentPresetDto`/`AgentDto`/`CreateAgentRequest`/`UpdateAgentRequest` lose the template field (trailing-member removal, all call sites named). Migration `RemoveGen1WorkflowEngine` (D-15) generated by the CLI. `E2E/Fixtures/AntiphonAppFixture` loses `UseMockExecutor`. | New `tests/Antiphon.Tests/Migrations/RemoveGen1WorkflowEngineMigrationTests.cs` (D-15 Up/Down); new `tests/Antiphon.Tests/Application/Gen1RemovalGuardTests.cs` (`WebApplicationFactory`: `GET /api/workflows`, `/api/workflows/{guid}`, `/api/settings/templates`, `/api/settings/template-groups`, `/api/audit/conversation`, `/api/audit/cost-summary` → 404; `GET /api/settings/providers`, `GET /api/audit` → 200; `DELETE /api/audit/archive` still archives); `DatabaseSeederTests` rewritten to pin provider seed + config sync (the three template/routing tests go); `ProjectReadinessTests` (drop `:409-421`, `:619`; add "readiness has no `workflow-template` key"); `AgentPresetsTests`, `InstructionBundleTests:123-128`, `ProjectSetupServiceTests` (drop template pins); one new arm in `AgentSessionServiceIntegrationTests`: legacy YAML content carrying both `hooks:` and `stages:` still yields hooks through `ParseYamlHooks`; fixture trims in ~20 files; `DataRetentionServiceTests`, `AuditArchiveEndpointTests` and the seven legacy-YAML fixtures stay green unedited. |
| **S7b Gen-1 client removal** (group D) | Delete the 52 exclusive client files. `App.tsx` (two lazy imports, `workflows` and `workflow/:id` routes, the `useStreamingEvents` call), `App.test.tsx` (mocks, route row), `shared/Layout.tsx:73`, `SettingsPage.tsx` (drop `templates` tab and panel; default tab `projects`), `api/settings.ts` (keep `LlmProviderDto`, provider hooks, `TestProviderResult`; drop template, template-group and model-routing types/hooks), `api/agents.ts:133-134,366,401`, `api/projectSetup.ts:64`, `AgentCreateModal.tsx:237`, `AgentSettingsModal.tsx:189`, `ProjectSetupModal.tsx:288`, `hooks/useSignalRInvalidation.ts` (six gen-1 rows; keep `WorkflowReloaded`, `BoardChanged`, `CardChanged`), `test/mocks/handlers.ts:58,71`, 12 agent + 4 home test fixtures, `ReplyStyleNarrow.stories.tsx`; `package.json` drops `@tiptap/pm`, `@tiptap/react`, `@tiptap/starter-kit`, `tiptap-markdown`, `mermaid` (+ lockfile via `npm install`); rebuild `client/dist`. | Vitest: `App.test` route table without `/workflows`; a `Layout` test that the nav has no Workflows item; `SettingsPage.test` third case renamed (no templates fetch to assert against) plus default tab = Projects; `useSignalRInvalidation.test` gen-1 rows removed; `tsc --noEmit` is the excess-property guard for every fixture that still carried `defaultWorkflowTemplateId`. `pwsh -File scripts/test-client.ps1`. |
| **S7c Docs + scripts** (group D) | `docs/antiphon-api.md` (drop the `/api/workflows*`, gates, cascade, artifacts, `feature-status`, `/api/audit/cost-summary`, `/cost-ledger`, `/conversation`, `/api/settings/templates*`, `template-groups`, `model-routing` lines; the section header names only `OrchestratorEndpoints.cs`); `docs/project-context.md:28-29,37-38,47,111` (naming examples re-pointed at live names such as `Cards`/`AgentTasks`, `IX_AgentTasks_CardId`, `/api/cards`, `AgentTaskService`; `IStageExecutor` dropped from the interface list); `scripts/perf/page-load-probe.py:55`; `scripts/verify-agent-tui-profile.ps1:200`. `docs/agent-credentials.md` unchanged (providers kept); `docs/features/**` unchanged (history). | `PipelineDefinitionDocumentationTests` gains two negative pins: `docs/antiphon-api.md` contains neither `/api/workflows` nor `/api/settings/templates`. |

**Dispatch grouping (D-12 confirmed; order per D-14):** A = S1+S2+S6a (Worktree, Code,
Frontier; land; restart). B = S3+S4+S6b (Worktree, Code, Frontier; land; restart; rollout
probe). D = S7a+S7b+S7c (Worktree, Code, High: a mechanical deletion plus a compile-fix loop
across two build systems, no design; land; restart; `client/dist` rebuild). C = S5 (Worktree,
Code, High; land; client rebuild). Strictly sequential in that order: C cannot precede A, D
cannot precede B, C should not precede D (`SettingsPage.tsx`). A and B could be one dispatch if
the operator prefers one restart.

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
- D-13/D-15 (group D): the migration Up on seeded gen-1 rows and Down to empty tables; the
  route guard (six 404s, two 200s, one archive); `AuditService.RecordEventAsync` still writes a
  row whose `EventType` round-trips; `ParseYamlHooks` on legacy YAML that also has `stages:`;
  readiness has no `workflow-template` key; the seeder writes providers only; client route
  table, nav, settings default tab and invalidation map.
- Regression for D: `Antiphon.Tests` minus the 51 deleted tests, `Antiphon.E2E` minus the 10
  deleted, Vitest minus the deleted suites, all green; the seven legacy-YAML fixtures named in
  §Ground truth stay green UNEDITED, which is the proof that removing the legacy branch changed
  nothing observable.

## Group D verification inventory and cost

Guards go red if D regresses; PCs are the method-scoped mutation controls TestDesign should
plan (`docs/testing-and-build.md`). The A/B/C guards are unchanged by the amendment.

| # | Guard | PC (mutate → which test goes red) |
|---|---|---|
| G-1 | `RemoveGen1WorkflowEngineMigrationTests.Up_drops_nine_tables_and_two_columns_keeps_audit_rows` | re-add `Agents.DefaultWorkflowTemplateId` to the entity only → the `HasPendingModelChanges` guard is red |
| G-2 | `RemoveGen1WorkflowEngineMigrationTests.Down_recreates_empty_tables` | delete one `CreateTable` from `Down` → red |
| G-3 | `Gen1RemovalGuardTests.Gen1_routes_are_gone` (six 404s) | map a stub `GET /api/workflows` returning 200 → red |
| G-4 | `Gen1RemovalGuardTests.Kept_routes_still_serve` (`/api/settings/providers`, `/api/audit`, `DELETE /api/audit/archive`) | drop `MapSettingsEndpoints()` → red |
| G-5 | `AuditServiceTests.RecordEventAsync_writes_row_without_gen1_ids` | skip `SaveChangesAsync` → red |
| G-6 | `AgentSessionServiceIntegrationTests.ParseHooks_reads_hooks_from_legacy_yaml_with_stages` | return `WorkflowHooks.Empty` when the content contains `stages:` → red |
| G-7 | `ProjectReadinessTests.Readiness_has_no_workflow_template_key` | re-add the check → red |
| G-8 | `DatabaseSeederTests.Seed_writes_providers_and_syncs_config_only` | seed a `TemplateGroup` → compile red (type gone), which is the point |
| G-9 | Vitest `App.test` route table, `Layout` nav, `SettingsPage` default tab, `useSignalRInvalidation` map | re-add the `/workflows` nav item → red |
| G-10 | `dotnet build` + `tsc --noEmit` | the compile itself: any fixture still carrying `defaultWorkflowTemplateId`, any `using` of a deleted namespace, any `[Test]` on a deleted type |

Cost band for D (Code, High): 107 files deleted (55 server, 52 client), ~52 trimmed, 1 CLI
migration, ~10 new tests, 5 npm packages dropped. The work is a compile-fix loop over two build
systems, then one full `Antiphon.Tests` run chunked by namespace, one `Antiphon.E2E` run and one
Vitest run. Verification floor about 45 min (`Antiphon.Tests` ~25.5 min + E2E + client);
authoring 2-3 h; `-ExpectAbout` 4 h, band 3-6 h. D adds one land+restart to the card's rollout
(four instead of three) and is the only group whose migration cannot be reversed with data.

## Verification design

TestDesign task `924d72d5`, 2026-09-18, inspected at `62ab4bdf` (plan tip; master `3a62074e` unchanged).
Appended; the fix design above is not rewritten. The test names below are the contract Code
implements against; renaming one is a Review defect unless the Code report says why.

### Corrections found during inspection (Code applies these; the decisions D-1..D-15 stand)

1. **The "seven legacy-YAML fixtures" are four.** The plan's list (§Ground truth, D-13) came from a
   grep for `stages:` + `executorType:`. Three of the seven are gen-1 `WorkflowTemplate` objects,
   not `BoardWorkflowDefinition` rows: `AgentControlServiceIntegrationTests.cs:1908`
   (`NewWorkflowTemplate`), `AgentServiceIntegrationTests.cs:1697` (`CreateGraph`'s template),
   `BoardServiceIntegrationTests.cs:560` (`NewWorkflowTemplate`). That type is deleted in D, so
   those three belong to the "~20 trimmed" set and cannot stay unedited.
   `AgentChannelServiceIntegrationTests.cs:509` is a diagnostics string, not a fixture. The four
   fixtures that ARE `BoardWorkflowDefinition.Content` YAML and stay unedited:
   `AgentChannelServiceIntegrationTests.cs:357-372` (`NewGraph`, `name: E11`),
   `AgentSessionServiceIntegrationTests.cs:1363-1378` (`CreateGraph`, `E05`),
   `OrchestratorServiceIntegrationTests.cs:1393-1408` (`CreateGraph`, `E07`),
   `Agents/RealCliStubBServerHarness.cs:222-232` (`CreateGraph`, `name: stub`). Verified: no test
   asserts the `Workflow: <name>` / `- Run (raw)` lines the legacy branch appends (grep
   `"Workflow: "` and `(raw)` over `tests/`: zero hits outside fixtures); the prompt assertions in
   those files are `SentPrompt.ShouldContain(<identifier>)` (`OrchestratorServiceIntegrationTests:100,137,169`,
   `AgentSessionServiceIntegrationTests:800-801`), which the plain prompt still satisfies. R-6
   names the consumer classes.
2. **A does not compile as sliced.** S1 drops `AgentId`/`WorkflowTemplateId`/`WorkflowDefinitionSnapshot`
   from `CardWorkflowRun` and `ExecutorType`/`ModelName`/`GateRequired`/`SystemPrompt` from
   `CardWorkflowStage`; `CardWorkflowRunFactory.CreateRun` (`:44-80`) writes every one of them and
   `AgentService.AssignCardAsync` (`:846`) calls it. The factory rewrite (`CreateFromRevision`,
   pure) and D-11 (`AssignCardAsync` assigns only; `DeleteAsync` stops loading runs;
   `CardLifecycleTransitions` stops nulling the run) therefore move from B to A, with the fixture
   edits the plan's D-14 row attributes to B (`AgentServiceIntegrationTests:582-665`,
   `BoardServiceIntegrationTests:35-77`, `KanbanPersistenceTests:87-148`,
   `HomeTaskServiceIntegrationTests:577-622`, `ProjectReadinessTests:673`,
   `ProjectSetupServiceTests:223`, E2E `AgentE2ETests:148-185`). B keeps `CardWorkflowRunService`,
   create-time linkage, transitions and launch wiring. A stays runtime-inert: nothing creates a run
   until B. V-A17..V-A19 are the moved tests.
3. **The built-in id collides with a seed id.** `c0000000-0000-0000-0000-000000000001` is
   `DatabaseSeeder.AnthropicProviderId`. Different table, so nothing breaks, but every test below
   refers to `PipelineDefinitions.StandardPipelineId`; Code picks an unused prefix
   (`f0000000-0000-0000-0000-000000000001`) and the literal appears once, in the constant.
4. **Boards have no update endpoint.** `BoardEndpoints` maps no `PUT /api/boards/{id}`, only
   `PUT /{id}/workflow`. The pointer writes are two new routes, `PUT /api/boards/{id}/pipeline`
   and `PUT /api/projects/{id}/pipeline`, body `{ "pipelineDefinitionId": "<guid>" | null }`
   (null = inherit / clear), 200 with the board / project DTO, 404 for an unknown board, project
   or definition, 409 `pipeline_definition_archived`. `UpdateProjectRequest` is not widened: its
   null-means-unchanged convention cannot express "clear".
5. **The seeder also seeds the admin user.** `DatabaseSeeder.SeedAsync` = admin + providers +
   config sync + (after A) the built-in pipeline. G-8's "providers only" reads "admin, providers,
   config sync, built-in pipeline; nothing else".
6. **`WorkflowCount` reaches the client.** `ProjectCascade.cs:142` → `client/src/api/projects.ts:67`
   and `ProjectDeleteDialog.test.tsx:32,124`; D trims both.
7. **A's `Down` needs a data step too.** The CLI `Down` re-adds `CardWorkflowRuns.AgentId` NOT
   NULL with a zero-guid default and an FK to `Agents`; on a database holding post-A runs it
   fails. Hand-add `DELETE FROM "CardWorkflowStages"; DELETE FROM "CardWorkflowRuns";` at the top
   of `Down` (mirror of `Up`), so the three existing downgrade tests (R-10), which migrate below
   A on every run, keep passing once B has created runs in their schemas.
8. **The Up data step is unconditional.** `Up` deletes every `CardWorkflowStages` and
   `CardWorkflowRuns` row, not "the one CARD-0001 row": every existing row is gen-1 shaped and
   test databases hold others (`HomeTaskServiceIntegrationTests.SeedWorkflowRunAsync`).
   `Cards.ActiveWorkflowRunId` is `SetNull` (`AppDbContext.cs:1007-1010`), so a linked card survives.

### Inspection
- `tests/Antiphon.Tests/Migrations/CommitOnSettleMigrationTests.cs` (operation inspection plus isolated-schema round trip), `Application/AgentTaskInternalDecisionMigrationTests.cs`, `Application/StandingSpecialistRoutingMigrationTests.cs` (real `IMigrator` down / raw write / up, `information_schema` probes, `GetPendingMigrationsAsync().ShouldBeEmpty()`, `HasPendingModelChanges().ShouldBeFalse()`, `PostgresException.ConstraintName` unique-violation pin) | boundaries: seeded vs empty DB, linked vs unlinked legacy run, post-A rows on Down → V-A1..V-A5, V-D1, V-D2; deleted entity types → raw-SQL seeding while downgraded (V-A2, V-D1).
- `TestHelpers/TestDbFixture.cs` (`CreateIsolatedSchemaAsync` = cloned migrated DB, never seeded) | seeder tests start from zero rows → V-A13, V-D8.
- `Infrastructure/DatabaseSeederTests.cs` (3 gen-1 tests, all deleted in D), `DatabaseSeeder.cs:47-53,90-119,175-260` | → correction 5, V-A13, V-D8.
- The four YAML `BoardWorkflowDefinition` fixtures and their consumers; `CardService.BuildPrompt:1539-1566`, `OrchestratorService.BuildPrompt:1013-1039`, `AgentSessionService.ParseHooks:2905-2921`, `WorkflowDefinitionParser` (`ParseYamlHooks` reads the root `hooks:` key regardless of siblings; `ParseYamlDefinition` additionally validates `stages:`), `WorkflowDefinitionLoader.Parse:126-138` | boundaries: content with `hooks:` + `stages:`, malformed `stages:`, Markdown front matter, no `hooks:` → V-D6, V-D11; `InternalsVisibleTo("Antiphon.Tests")` exists (`Antiphon.Server.csproj:12`), so `ParseHooks` and both `BuildPrompt`s become `internal static`.
- `ProjectReadinessTests.cs:404-424,611-624,656-661,850-862`, `AgentPresetsTests.cs` (7 tests, 2 template lines), `InstructionBundleTests.cs:118-128,132-142`, `ProjectSetupServiceTests.cs:32,168,183,199,218-236`, `TestHelpers/SeededWorkflowTemplates.cs` (8 call sites in 3 files) | → V-D7, R-2, R-12, R-13.
- `AuditArchiveEndpointTests.cs` (WebAppFactory subclass, `Audit:RetentionDays=14`; seeds `AuditRecord` without gen-1 ids), `DataRetentionServiceTests.cs` (16 tests, none touch gen-1 columns), `AuditService.cs:108-135,224-250`, `AuditEndpoints.cs`, `AuditRecord.cs`, `AuditEventType` (0-17) | → V-D3..V-D5, R-7.
- `AppDbContext.cs:14-93` (DbSets), `:530-555` (audit FKs), `:653-715` (TUI-profile composite-FK precedent), `:1003-1010` (card → run SetNull), `:1060-1088` (run FKs), `:1684` (`PropertySaveBehavior.Throw` precedent) | → V-A1, V-A6.
- `PipelineHandoff.cs` (17 aliases, `Token`, `TryToStageRole`, `HeaderBit`), `PipelineHandoffParseTests.cs` (19 alias rows + 12 shape tests), `InterimVerificationPolicy.CapHandoff:48-51`, `AgentTaskReplyService.cs:665-668`, `AgentTaskReplyIntegrationTests.cs:300-348,4436-4580` (`SeedDispatchedTaskAsync` / `SeedTurnAsync` / `CreateService` / `OnTurnEndAsync` settle path; class is already `partial`) | → V-B7, V-B12, R-5.
- `AgentTaskDispatcher.cs:4121-4122,4645-4646` (both `ForDelegate` sites), `:3354,4663,4842` (Dispatched transitions), `InstructionBundles.cs:203-233`, `DelegateBundleLaunchTests.cs` (`BuildLaunchSpec` harness: `SpecOf` / `AppendedSystemPrompt` / `TaskFor`), `AgentTaskPoolTests.cs` (28 tests; `ReuseOutcome` harness), `MutationDispatchTests.cs:179-270` (tick harness with `CaptureFactory`; its `Settle` writes status directly and does not exercise the reply service) | → V-B7b, V-B9, V-B10, V-B13.
- `AgentTaskPipelineStatusService.cs:209-283` (`BuildReady`), `AgentTaskPipelineDtos.cs:76-95`, `AgentTaskPipelineStatusTests.cs` (29 methods, 12 argument rows) | → V-B12, R-1.
- `CardLifecycleTransitions.cs:67-95` and its 6 callers, `CardService.MoveAsync:424-460`, `CardService.ArchiveAsync:895-935`, `HomeTaskService.ClassifyCard:328-350`, `HomeTaskServiceIntegrationTests.cs:44-56,577-622` | → V-B4, V-B8, R-8.
- `AgentTaskService.cs:483-492` (binder), `:962-982` (row init), `:1050-1060` (gate), `AgentTaskCardBinder.cs:12-16` (`Warning` channel = event + response) | → V-B5.
- Client: `App.test.tsx` (route table; mocks `useStreamingEvents`, `DashboardPage`, `WorkflowDetailPage`), `SettingsPage.test.tsx` (3 cases keyed on `/api/settings/templates` request counts), `useSignalRInvalidation.test.ts` (2 tests; callback map), `useSignalRInvalidation.ts:25-57`, `Layout.tsx:71-79` (`NAV_ITEMS`), `SettingsPage.tsx:11-40` (`SETTINGS_TABS`, default `templates`), `test/mocks/handlers.ts:45-72`, the 24 files carrying `defaultWorkflowTemplateId`, `CardModal.tsx:413-418`, `CardRow.tsx:107-111`, `boards.ts:12,68-76`, `features/board/WorkflowEditor.tsx` (the board picker sits beside it), `test/utils.ts` (`renderWithProviders`, `renderHookWithProviders`) | → V-C1..V-C4, V-D9.
- `server/Bundles/stage-*.md` `next:` lines and `docs/orchestration-loop.md:118-160` | the D-5 table transcribes them token for token (checked) → V-B16.
- `docs/testing-and-build.md` (method-scoped PC procedure, alternate `OutputPath`, restore-timestamp rule, >15-20-row sharding rule, CARD-0110 timings) | → Cost.

### Delivery inventory
This card adds no asynchronous outcome-delivery path. Every new write is synchronous inside an
existing request or settle unit of work: run creation inside `AgentTaskService.CreateAsync` (same
transaction as the task row), stage transitions inside `AgentTaskReplyService.OnTurnEndAsync` and
the dispatcher tick, the stage-bundle Warning as an `AgentTaskEvent` row written by the dispatcher
before launch, the `pipeline=off` bit as text in the existing completion header. The header rides
the unchanged caller-delivery path (CARD-0146 / CARD-0331 contracts, pinned by the existing
`AgentTaskReplyIntegrationTests` header tests); this design asserts the header TEXT (V-B12) and
does not re-prove delivery. Substitute declared: none needed, because nothing here claims a
recipient received anything. No durable identity is introduced for delivery; the run's durable
identity (`CardWorkflowRun.Id`, `IX_CardWorkflowRuns_CardId_Open`) connects create, dispatch and
settle and is what V-B1/V-B2/V-B7 assert on.

### Proves it works now

Layer key: U = `[Category("Unit")]`, no DB; I = `[Category("Integration")]` on an isolated schema or `AntiphonWebAppFactory`; V = Vitest; S = the real script under pwsh against `StubApi` (`[ParallelLimiter<ProcessSpawnLimit>]`). "Code" in a `[Arguments]` list means `AgentTaskRole.Code`.

**Group A (S1 + S2 + S6a, plus the factory / D-11 move from correction 2)**

- V-A1: `AddPipelineDefinitions` operation shape | U | `Migrations/PipelineDefinitionMigrationTests.AddPipelineDefinitions_operations_create_tables_repoint_runs_and_add_the_open_run_index` | `new AddPipelineDefinitions().UpOperations`: two `CreateTableOperation` (`PipelineDefinitions`, `PipelineDefinitionRevisions`); a `SqlOperation` whose `Sql` deletes `CardWorkflowStages` then `CardWorkflowRuns` and whose index is lower than every `AddColumnOperation` on those two tables; `DropColumnOperation` for `AgentId`, `WorkflowTemplateId`, `WorkflowDefinitionSnapshot`, `ExecutorType`, `ModelName`, `GateRequired`, `SystemPrompt`; `AddColumnOperation` with `IsNullable == false` for `PipelineDefinitionId`, `PipelineDefinitionRevisionId`, `Role`, `BundleKey`, `AllowedNextJson` (`ColumnType == "jsonb"`); `CreateIndexOperation` `IX_CardWorkflowRuns_CardId_Open` with `IsUnique` and `Filter == "\"Status\" IN (0, 1)"`; `IX_CardWorkflowStages_RunId_Role` unique; `AddColumnOperation` `AgentTasks.CardWorkflowStageId` nullable; `DownOperations[0]` is the mirror `SqlOperation` (correction 7).
- V-A2: real Up on seeded legacy runs | I | `PipelineDefinitionMigrationTests.Up_deletes_every_legacy_run_and_stage_before_the_not_null_adds` | head schema → `migrator.MigrateAsync(<migration before AddPipelineDefinitions>)`; raw SQL inserts (column lists from `git show 3a62074e:server/Migrations/AppDbContextModelSnapshot.cs`): 1 `Agents`, 1 `WorkflowTemplates`, 1 `Projects` + `Boards` + `BoardColumns` + `Cards`, 2 `CardWorkflowRuns` (one with `WorkflowTemplateId`, one null), 3 `CardWorkflowStages`; `MigrateAsync()` → `CardWorkflowRuns` count 0, `CardWorkflowStages` count 0; `information_schema.columns` for `CardWorkflowRuns` has `PipelineDefinitionRevisionId` with `is_nullable = 'NO'` and no `AgentId`; the agent and card rows survive; `GetPendingMigrationsAsync()` empty; `HasPendingModelChanges()` false.
- V-A3: linked card survives | I | `PipelineDefinitionMigrationTests.Up_nulls_a_card_pointer_at_a_deleted_legacy_run` | same seeding with `Cards.ActiveWorkflowRunId` = run 1 → after Up the card exists with `ActiveWorkflowRunId` null.
- V-A4: filtered unique index | I | `PipelineDefinitionMigrationTests.Open_run_index_refuses_a_second_open_run_and_allows_a_closed_one` with `[Arguments("Queued,Queued")] [Arguments("Queued,Running")] [Arguments("Running,Running")]` → `DbUpdateException` whose inner `PostgresException.SqlState == PostgresErrorCodes.UniqueViolation` and `ConstraintName == "IX_CardWorkflowRuns_CardId_Open"`; `[Arguments("Completed,Queued")] [Arguments("Canceled,Queued")] [Arguments("Failed,Running")]` → both rows saved.
- V-A5: Down with post-A rows | I | `PipelineDefinitionMigrationTests.Down_removes_post_A_runs_then_restores_gen1_columns_and_Up_returns_to_head` | at head seed one run + one stage through the entities; `MigrateAsync(<before A>)` succeeds; `CardWorkflowRuns` count 0; `information_schema` has `AgentId` and `WorkflowDefinitionSnapshot`, no `PipelineDefinitions` table, no `AgentTasks.CardWorkflowStageId`; `MigrateAsync()` → head; pending empty.
- V-A6: revision immutability | I | `PipelineDefinitionMigrationTests.Revision_columns_cannot_change_after_insert` `[Arguments("StagesJson")] [Arguments("RevisionNumber")] [Arguments("DefinitionId")]` | load the revision, change the named column, `SaveChangesAsync` throws `InvalidOperationException` (`PropertySaveBehavior.Throw`).
- V-A7: canonical JSON + hash | U | `Application/PipelineStagesJsonTests.Serialise_is_canonical_and_hash_is_eight_lowercase_hex` | two `PipelineStageSpec[]` equal by value but with different `AllowedNext` insertion order and mixed-case tokens serialise byte-equal; `ContentHash` matches `^[0-9a-f]{8}$` and equals the first 8 hex of SHA-256 over that text; `Parse(Serialise(x))` round-trips; the on-disk role is the member name `"Code"`, never `3`; tokens are stored canonical (`"test-design"`).
- V-A7b: parse rejects | U | `PipelineStagesJsonTests.Parse_rejects_unknown_role_member_and_non_array` `[Arguments("[{\"role\":\"Ship\",\"bundleKey\":\"stage-code\",\"allowedNext\":[]}]")] [Arguments("{}")] [Arguments("")]` → `ValidationException` with `Code == "pipeline_stages_invalid"`.
- V-A8: validation matrix | I (isolated schema; the service reads `InstructionBundles.All`) | `Application/PipelineDefinitionServiceTests.Create_refuses_invalid_stages` `[Arguments]` rows → `ValidationException.Code`: role `Docs` → `pipeline_stage_role_not_stage`; `Custom` → same; `Check` → same; `Code` twice → `pipeline_stage_role_duplicate`; empty array → `pipeline_stages_empty`; bundle key `board-api` → `pipeline_stage_bundle_not_stage`; `stage-nope` → `pipeline_stage_bundle_unknown`; `allowedNext: ["ship"]` → `pipeline_stage_next_unknown`; `allowedNext: ["mutation"]` in a revision without Mutation → `pipeline_stage_next_not_in_revision`; blank name and a 201-char name → `pipeline_definition_name_invalid`; a taken name → `ConflictException` `pipeline_definition_name_taken`. Companion `Create_accepts_valid_shapes` `[Arguments]`: Review with `["review"]` (self-loop); `["land","decide","none"]`; single stage `[Code]` with `["code","decide"]`; the six-stage built-in shape → created, `RevisionNumber == 1`, `ActiveRevisionId` set.
- V-A9: built-in protection + clone | I | `PipelineDefinitionServiceTests.BuiltIn_refuses_revision_and_archive_but_clones` | `AddRevisionAsync(StandardPipelineId, …)` → `ConflictException` `Code == "pipeline_definition_builtin"`; `ArchiveAsync(StandardPipelineId)` → same; `CloneAsync(StandardPipelineId, "Mine")` → new id, `Source == Custom`, one revision with `RevisionNumber == 1`, `StagesJson` byte-equal to the built-in's active revision, `ContentHash` equal, `ActiveRevisionId` set.
- V-A10: revisions append + pointer | I | `PipelineDefinitionServiceTests.AddRevision_appends_N_plus_1_and_moves_the_active_pointer_in_one_transaction` | Custom def at r1 → `AddRevisionAsync` → r2 active; r1 row intact; `ChangeNote` of 401 chars → `pipeline_revision_note_too_long`; an invalid revision leaves `ActiveRevisionId` at r1 and the revision count at 1.
- V-A11: archive semantics | I | `PipelineDefinitionServiceTests.Archived_definition_is_refused_at_pointer_write_but_an_existing_pointer_still_resolves` | archive Custom def X; `BoardService.SetPipelineAsync(board, X)` → `ConflictException` `Code == "pipeline_definition_archived"`; a board already pointing at X → `PipelineResolution.ResolveForCardAsync` returns X's active revision with `Source == "board"`; `UnarchiveAsync` → the pointer write is accepted; `ListAsync(includeArchived: false)` omits X.
- V-A12: resolution precedence | I | `Application/PipelineResolutionTests.Board_pointer_outranks_project_default_which_outranks_the_built_in` `[Arguments("board")] [Arguments("project")] [Arguments("builtin")]` → expected `(DefinitionId, RevisionId, Source)` per row; `RevisionNumber` and `Hash` populated; after `AddRevisionAsync` on the pointed definition the resolution returns the NEW active revision (pin-at-use belongs to the run, not to resolution).
- V-A13: seeder D-5 | I | `Infrastructure/DatabaseSeederTests.Pipeline_seed_inserts_the_built_in_once_and_appends_a_revision_only_when_the_constant_hash_changes` | fresh schema: `SeedAsync` → `PipelineDefinitions` 1 row (`StandardPipelineId`, `Source == BuiltIn`, `Name == "Standard pipeline"`), `PipelineDefinitionRevisions` 1 row, `ContentHash == PipelineDefinitions.StandardPipeline.Hash`, `StagesJson` parses to the D-5 table (six roles in order, exact `AllowedNext` sets); second `SeedAsync` → still 1 revision and `UpdatedAt` unchanged; `DatabaseSeeder.SeedPipelineDefinitionsAsync(db, alternateSpec, ct)` (test-visible overload) → 2 revisions, r2 active, r1 `StagesJson` unchanged; a `Custom` definition created before either boot is untouched (hash and revision count equal).
- V-A14: endpoints | I (`AntiphonWebAppFactory`) | `Application/PipelineDefinitionEndpointTests`: `List_and_get_return_the_seeded_built_in` (`GET /api/pipeline-definitions` 200 array containing `StandardPipelineId` with `source == "BuiltIn"`, `activeRevision.revisionNumber == 1`, `activeRevision.stages[3].role == "Code"`; `GET /{id}` 200; `GET /{Guid.NewGuid()}` 404); `Post_validates_and_creates` (valid → 201 with `Location`; role `Docs` → 422 with `errors.stages` naming `pipeline_stage_role_not_stage`); `Revisions_clone_archive_unarchive_round_trip` (`POST /{id}/revisions` 200 r2; on the built-in → 409 `pipeline_definition_builtin`; `POST /{id}/clone` 201 Custom; `POST /{id}/archive` 204 then `GET` shows `archivedAt`; `POST /{id}/unarchive` 204); `Board_and_project_pointer_routes_write_and_expose_pipeline` (`PUT /api/boards/{id}/pipeline` `{ pipelineDefinitionId: X }` 200 → `GET /api/boards/{id}` `pipeline.source == "board"`, `pipeline.definitionId == X`, `pipeline.revisionNumber == 1`, `pipeline.hash` 8 hex; `PUT` with `null` → `source == "project"` when the project has a default else `"builtin"`; `PUT /api/projects/{id}/pipeline` likewise on `GET /api/projects/{id}`; archived X → 409 `pipeline_definition_archived`; unknown id → 404).
- V-A15: script | S | `Scripts/PipelineDefinitionScriptTests` (`RoutingPinScriptTests` shape): `List_gets_the_collection`, `Get_gets_by_id`, `Clone_posts_clone_with_name`, `Revise_posts_the_stages_file_as_the_revision_body` (`-StagesFile` JSON lands byte-for-byte in `LastBody.stages`, `changeNote` from `-Note`), `SetBoard_puts_the_pointer` (`PUT /api/boards/<id>/pipeline`, body `pipelineDefinitionId`), `SetProject_puts_the_pointer`, `SetBoard_Inherit_puts_null` (`ValueKind == JsonValueKind.Null`) → each asserts `LastMethod`, `LastPath`, `LastBody`, exit 0 with `run.Output` as the failure message.
- V-A16: docs | U | `Application/PipelineDefinitionDocumentationTests.Docs_name_the_routes_the_script_and_the_run_status_table` | `docs/antiphon-api.md` contains `GET    /api/pipeline-definitions`, `POST   /api/pipeline-definitions/{id}/revisions`, `PUT    /api/boards/{id}/pipeline`; `docs/ops-http.md` contains `pipeline-definitions`; `docs/agent-card-lifecycle.md` contains `CardWorkflowRunStatus`; `scripts/pipeline-definition.ps1` exists.
- V-A17 (moved from B): queue assignment creates no run | I | `AgentServiceIntegrationTests.AssignCardAsync_assigns_card_to_next_queue_position_and_creates_no_run` (replaces `…_and_snapshots_default_workflow`) | queue position 1, `WorkflowStatus` null, `CurrentStageName` null, `CardWorkflowRuns.Count() == 0`, `AgentQueueChanged` and `CardChanged` published; `DeleteAsync_removes_agent_and_unassigns_cards` (replaces `…_and_drops_runs`) with a run built by `CardWorkflowRunFactory.CreateFromRevision` for the card → the run still exists after the agent delete, card `AssignedAgentId` null; `BoardServiceIntegrationTests:35-77` asserts `ActiveWorkflowRunId` null and `CurrentWorkflowStageName` null after assignment (R-11).
- V-A18 (moved): pure factory | U | `Application/CardWorkflowRunFactoryTests.CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer` | card + definition + six-stage revision + `now` → run `Status == Queued`, `WorkflowName == definition.Name`, `PipelineDefinitionId`, `PipelineDefinitionRevisionId == revision.Id`, six `Stages` ordered `StageOrder` 0..5 with `Role`, `Name == role.ToString()`, `BundleKey`, canonical `AllowedNextJson`, all `Pending`, `CurrentStageId == Stages[0].Id`, `CreatedAt == now`; the factory takes no `DbContext`.
- V-A19 (moved): persistence fixtures | I | `KanbanPersistenceTests.AppDbContext_round_trip_persists_agent_queue_and_card_workflow_run` rewritten to the new columns (run → `StandardPipelineId` + its revision; stage `Role = Code`, `BundleKey = "stage-code"`, `AllowedNextJson`) → reload equals; `ProjectDeletionTests` cascade arm asserts runs + stages of the project's cards are gone; `HomeTaskServiceIntegrationTests.SeedWorkflowRunAsync` rewritten to the new shape with its `WaitingForHumanReview` gate test (`:44-56`) unchanged (R-8).

**Group B (S3 + S4 + S6b)**

- V-B1: ensure idempotent | I | `Application/CardWorkflowRunServiceTests.EnsureActiveRun_creates_once_then_returns_the_open_run` | first call → run `Queued`, six `Pending` stages, `CurrentStageId` = the Investigate row, `card.ActiveWorkflowRunId` set; second call → same `Id`, `CardWorkflowRuns.Count(r => r.CardId == card.Id) == 1`.
- V-B2: concurrent ensure | I | `CardWorkflowRunServiceTests.Two_concurrent_ensures_yield_one_run` | ctx1 `BeginTransactionAsync`, add a `Queued` run for the card, `SaveChangesAsync` (uncommitted); `var second = service2.EnsureActiveRunAsync(card.Id, ct)` started as a Task (blocks on the index); ctx1 commits; `await second` returns ctx1's run id; count 1; nothing throws.
- V-B3: resnapshot | I | `CardWorkflowRunServiceTests.Resnapshot_cancels_the_open_run_and_creates_one_from_the_current_resolution` | board pointer moved to a two-stage Custom def between ensure and resnapshot → old run `Canceled` with `FailureReason == "resnapshot"`, new run `Queued` with 2 stages and the Custom revision id, `card.ActiveWorkflowRunId` = new, old stage rows untouched.
- V-B4: complete / archive | I | `CardWorkflowRunServiceTests.Complete_maps_card_terminal_status_to_run_status` `[Arguments(CardStatus.Done, CardWorkflowRunStatus.Completed)] [Arguments(CardStatus.Canceled, CardWorkflowRunStatus.Canceled)]` via `CardService.MoveAsync` into the terminal column → run status, `CompletedAt` set, `card.ActiveWorkflowRunId` unchanged; `Archive_cancels_the_open_run_instead_of_refusing` via `CardService.ArchiveAsync` on an InProgress card with a `Running` run → no throw, run `Canceled` with `FailureReason == "archived"`.
- V-B5: create-time linkage matrix | I | `Application/AgentTaskCreateSnapshotsRunTests` (harness = `AgentTaskServiceIntegrationTests.CreateService:1852` pattern): `A_card_bound_stage_role_worker_creates_the_run_and_links_the_stage_row` `[Arguments(Investigate)] [Arguments(Plan)] [Arguments(TestDesign)] [Arguments(Code)] [Arguments(Review)] [Arguments(Mutation)]` → `task.CardWorkflowStageId` = the row with that `Role`, one run; `Helper_roles_orchestrator_kind_and_unbound_tasks_create_nothing` `[Arguments(Worker, Docs, true)] [Arguments(Orchestrator, Plan, true)] [Arguments(Worker, Code, false)] [Arguments(Worker, Check, true)]` → `CardWorkflowStageId` null, `CardWorkflowRuns.Count() == 0`; `A_stage_role_outside_the_run_definition_is_created_with_a_pipeline_warning` (board on Custom `[Plan, Code]`; create Role Mutation) → created, `CardWorkflowStageId` null, response `Warning` contains `pipeline: role Mutation is not in Two r1`, an `AgentTaskEvent` `Type == Warning` with that text; `Run_creation_happens_before_the_open_gate_and_a_gate_refusal_leaves_the_run` (gate cap 0 → `ConflictException`; the run row exists).
- V-B7: D-8 transitions, settle side | I | `Application/AgentTaskReplyIntegrationTests.CardWorkflowStage.cs` (new file of the existing `partial` class, same harness; tasks created through `AgentTaskService.CreateAsync` so `CardWorkflowStageId` is real, then `SeedTurnAsync` + `OnTurnEndAsync`):
  - `Settle_next_review_completes_code_and_moves_the_pointer_to_review` → run `Queued`, Code stage `Completed` with `ResultSummary == handoff`, `CompletedAt` set, `CurrentStageId` = the Review row.
  - `Settle_next_land_decide_none_or_unmarked_completes_the_stage_and_keeps_the_pointer` `[Arguments("land")] [Arguments("decide")] [Arguments("none")] [Arguments(null)]` (a Review task) → Review `Completed`, `CurrentStageId` unchanged, run `Queued`.
  - `Settle_next_role_outside_the_run_completes_the_stage_keeps_the_pointer_and_flags_off_pipeline` (Custom `[Plan, Code]`, Code settles `next: review`) → Code `Completed`, pointer stays Code, `task.NextStage == Review` (settlement itself unchanged), the completion header text contains `next=review pipeline=off`.
  - `Settle_failed_marks_the_stage_failed_with_the_reason` (report closes `[antiphon-report:… failed]`) → stage `Failed`, `FailureReason == task.FailureReason`, run `Queued`.
  - `Settle_blocked_leaves_the_stage_unchanged` (`… blocked]`) → stage still `Running`, pointer unchanged.
  - `A_re_entered_stage_goes_completed_to_running_again` (Review settles `next: code`; a new Code task is created and dispatched) → Code stage `Running` again, `CompletedAt` null, `StartedAt` updated.
  - `Interim_review_asking_land_is_capped_to_review_and_the_pointer_self_loops` (`VerificationRound.Interim`, `next: land`) → `task.NextStage == Review` (existing `CapHandoff`), Review stage `Completed`, pointer stays Review.
- V-B7b: D-8 transitions, dispatch side | I | `Application/CardWorkflowRunDispatchTests` (harness = the `MutationDispatchTests.Harness` shape with `CaptureFactory`; lift it to `TestHelpers/DispatchTickHarness.cs` or copy it, and register the graph through `AddDelegationWorktreeGraph` so `DelegationHarnessCensusTests` stays green): `Dispatch_marks_the_stage_running_and_skips_earlier_pending_stages` (card-bound Code task on a fresh six-stage run; `Tick()`) → run `Running` with `StartedAt`, Code `Running`, Investigate / Plan / TestDesign `Skipped`, Review / Mutation `Pending`, `CurrentStageId` = Code; `Dispatch_of_a_task_without_a_stage_row_touches_no_run` (helper Docs task) → run unchanged; `A_task_canceled_before_dispatch_leaves_the_stage_pending`.
- V-B8: home rail | I | `HomeTaskServiceIntegrationTests.A_card_is_running_only_while_a_stage_task_is_live_and_stage_shows_the_pointer` | run `Running` + bound task `Working` → group `Running`, `Stage == "Code"`; after the settle (`next: review`) run `Queued`, no open task → group per card status, `Stage == "Review"` (the next stage).
- V-B9: `ForDelegate` with a stage key | U | `InstructionBundleTests.an_explicit_stage_bundle_key_replaces_the_role_default` (`ForDelegate(Worker, Code, null, "stage-review")` → `["stage-review", "delegate-basics"]`); `a_missing_stage_bundle_key_falls_back_to_the_role_default` (`"stage-gone"` → `["stage-code", "delegate-basics"]` and the result's `DroppedStageKey == "stage-gone"`); `is_stage_bundle_key_accepts_only_shipping_stage_bundles` `[Arguments("stage-code", true)] [Arguments("board-api", false)] [Arguments("stage-nope", false)] [Arguments("style-brief", false)]`; `a_sub_orchestrator_ignores_a_stage_key` (Orchestrator kind → `["orchestrator", "delegate-basics"]`).
- V-B10: launch composition from the stage row | U | `DelegateBundleLaunchTests.a_stage_row_bundle_key_outranks_the_role_default_at_launch` (task with `CardWorkflowStage = new() { BundleKey = "stage-review" }`, role Code → `AppendedSystemPrompt` starts with `[bundle:stage-review v{version}]`, contains `[bundle:delegate-basics`, does not contain `[bundle:stage-code`); `a_stage_row_naming_a_missing_bundle_launches_on_the_role_default` (`"stage-gone"` → starts with `[bundle:stage-code v`); `a_grok_and_codex_launch_carry_the_stage_row_key_in_their_typed_payloads` (the `:289-320` arms with a stage row).
- V-B10b: warning event on fallback | I | `CardWorkflowRunDispatchTests.A_vanished_stage_bundle_key_launches_on_the_role_default_and_records_a_warning_event` | stage row `BundleKey = "stage-gone"` → task `Dispatched`, `StartedArgs` append starts `[bundle:stage-code v`, exactly one `AgentTaskEvent` with `Type == Warning` whose `Detail` contains `stage-gone` and `stage-code`.
- V-B10c: drift at the warm-reuse site | I | `AgentTaskPoolTests.warm_grok_reuse_recomputes_the_desired_rules_from_the_stage_row` | a warm Grok session whose installed rules were composed with `stage-code`; the task's stage row now says `stage-review` (post-resnapshot) → `ReuseOutcome.SpawnFresh`; the same task with `stage-code` → reuse.
- V-B11: per-stage menu | U | `DelegationUnitTests.stage_handoff_contract_for_renders_only_the_allowed_tokens` (`StageHandoffContractFor(["review", "code", "decide"])` contains `next: <review|code|decide>`, contains the unchanged alias line, length ≤ 700); `stage_handoff_contract_for_falls_back_to_the_literal_for_null_or_empty` (`null` and `[]` → `== StageHandoffContract`); `build_brief_uses_the_stage_row_menu_when_the_task_has_one` (`BuildBrief(task with stage row AllowedNext ["none", "decide"])` contains `next: <none|decide>`).
- V-B12: header bit + ready projection + DTO | U + I | `DelegationUnitTests.completion_header_carries_pipeline_off_after_next_only_when_the_handoff_is_outside_the_run` (`…next=review pipeline=off…` vs no bit); `AgentTaskPipelineStatusTests.a_ready_row_is_projected_with_off_pipeline_true_when_the_source_next_was_outside_its_run` and `a_ready_row_is_off_pipeline_false_by_default` (`OffPipeline` trailing member; the 29 existing methods untouched, R-1); `existing_agent_task_routes_are_unchanged` extended: `GET /api/agent-tasks/{id}` JSON has `summary.cardWorkflowStageId`.
- V-B13: SourceLanding Mutation links to the companion's Mutation stage | I | `MutationDispatchTests.C558_source_landing_mutation_task_links_to_the_companion_run_mutation_stage_and_skips_earlier_stages` | companion card + Mutation task with `SourceLandingOperationId` → `CardWorkflowStageId` = the Mutation row; after `Tick()` Investigate..Review `Skipped`, Mutation `Running`, run `Running`; the four existing arms unchanged (R-4).
- V-B14: task DTO parity | V | `agentTasks.ts` `AgentTaskDto.cardWorkflowStageId: string | null` compiles under `npx tsc --noEmit`; the server pin is in V-B12.
- V-B15: D-12 invariant | U | `PipelineHandoffParseTests` untouched (R-5) plus `Application/PipelineDefinitionsBuiltInTests.every_allowed_next_token_in_the_built_in_is_a_known_handoff_alias` (each token → `PipelineHandoff.TryParse("--- next stage ---\nnext: <token>\n")` yields a non-null `Kind` and `Token(kind) == token`).
- V-B16: built-in transcribes the prose | U | `PipelineDefinitionsBuiltInTests.the_built_in_transcribes_each_stage_bundles_next_line` | per stage: `InstructionBundles.TextOf(bundleKey)`, take the line starting `next:`, assert every `AllowedNext` token appears in it as `next: <token>`, `<token> when`, `; <token>` or `| <token>`, and no other `PipelineHandoff` alias appears there (a prose edit that adds or removes an edge fails here, not in production).
- V-B17: docs | U | `PipelineDefinitionDocumentationTests.Loop_doc_bundle_readme_and_tracker_doc_name_the_pipeline_layer` | `docs/orchestration-loop.md` contains `pipeline=off` and `PipelineDefinition`; `server/Bundles/README.md` contains `stage row`; `docs/workflow-tracker-block.md` contains `pipeline-definitions`.

**Group C (S5, Vitest; `renderWithProviders` + MSW `server.use`)**

- V-C1: panel | V | `client/src/features/settings/PipelineDefinitionsPanel.test.tsx`: `lists definitions with revision and hash and disables revise on the built-in` (`GET /api/pipeline-definitions`; the built-in row reads `Standard pipeline r1 v<hash>`, its Revise button `toBeDisabled()`, Clone enabled); `editor limits the role select to stage roles and the bundle select to stage-* keys` (`GET /api/agents/bundles` returns `board-api`, `stage-code`, `stage-review`, `style-brief` → bundle options exactly `['stage-code', 'stage-review']`; role options exactly the six stage roles in pipeline order); `saving posts a revision with ordered stages canonical tokens and a change note` (captured `POST /api/pipeline-definitions/:id/revisions` body `toEqual({ stages: [{ role: 'Code', bundleKey: 'stage-code', allowedNext: ['review', 'code', 'decide'] }, { role: 'Review', bundleKey: 'stage-review', allowedNext: ['land', 'review', 'code', 'decide'] }], changeNote: 'tighten' })`); `a 422 shows the server error code on the stage row` (`pipeline_stage_next_not_in_revision` rendered next to the offending row).
- V-C2: pickers | V | `client/src/features/board/BoardPipelinePicker.test.tsx`: `shows inherit with the effective source and writes the chosen id` (`PUT /api/boards/b1/pipeline` body `{ pipelineDefinitionId: 'X' }`), `choosing inherit writes null` (body `pipelineDefinitionId === null`); `client/src/features/settings/ProjectPipelinePicker.test.tsx` the same two cases against `/api/projects/p1/pipeline`.
- V-C3: card tooltip | V | `CardModal.test.tsx` new case `stage line tooltip names the definition and revision` (fixture `currentWorkflowStageName: 'Code'`, `pipeline: { name: 'Standard pipeline', revisionNumber: 2 }` → hovering the stage line shows `Standard pipeline r2`); `CardRow.test.tsx` badge cases unchanged (R-11).
- V-C4: settings tab laziness | V | `SettingsPage.test.tsx` new case `does not fetch pipeline definitions until the Pipelines tab is opened` (the existing counter pattern: `GET /api/pipeline-definitions` count 0 before the click, 1 after).
- V-C5: whole client | V | `pwsh -File scripts/test-client.ps1` prints `CLIENT TESTS EXIT CODE: 0`; `npx tsc --noEmit` clean.

**Group D (S7a + S7b + S7c)**

- V-D1: Up | I | `Migrations/RemoveGen1WorkflowEngineMigrationTests.Up_drops_nine_tables_and_two_column_sets_and_keeps_agent_and_audit_rows` | at head seed through kept entities: 1 `Project`, 1 `LlmProvider`, 1 `Agent`; `migrator.MigrateAsync(<migration before RemoveGen1WorkflowEngine>)`; raw SQL (pre-D column lists from `git show 3a62074e:server/Migrations/AppDbContextModelSnapshot.cs`): 1 `TemplateGroups`, 3 `WorkflowTemplates` (one with `TemplateGroupId`), 10 `ModelRoutings` (the provider + a template), 1 `Workflows` (project + template), 1 `Stages`, `UPDATE "Agents" SET "DefaultWorkflowTemplateId" = <t1>`, 2 `AuditRecords` (`EventType` 17 with `WorkflowId` = the workflow and `StageId` = the stage; `EventType` 1 with nulls); `MigrateAsync()` → `information_schema.tables` has none of `CostLedgerEntries`, `ArtifactSectionReviews`, `GateDecisions`, `StageExecutions`, `Stages`, `Workflows`, `ModelRoutings`, `WorkflowTemplates`, `TemplateGroups`; `information_schema.columns` has no `Agents.DefaultWorkflowTemplateId` and none of `AuditRecords.WorkflowId` / `StageId` / `StageExecutionId`; `pg_indexes` has no `IX_AuditRecords_WorkflowId` / `IX_AuditRecords_StageId`; `db.Agents.Single(...)` keeps `Name`, `Kind`, `ModelLevel`, `WorkingDirectory`; `db.AuditRecords` count 2 with `EventType` `SessionDisconnected` and `ToolInvocation`; raw `SELECT "EventType"` returns 17 and 1; `GetPendingMigrationsAsync()` empty; `HasPendingModelChanges()` false.
- V-D2: Down | I | `RemoveGen1WorkflowEngineMigrationTests.Down_recreates_the_nine_tables_empty_and_the_four_columns_nullable_then_Up_returns_to_head` | head with the agent + the 2 audit rows → `MigrateAsync(<before D>)` → each of the nine tables exists with `COUNT(*) = 0`; `Agents.DefaultWorkflowTemplateId` and the three audit columns exist with `is_nullable = 'YES'`; the audit rows are still 2; `MigrateAsync()` → head; pending empty.
- V-D3: routes gone | I (`AntiphonWebAppFactory`) | `Application/Gen1RemovalGuardTests.Gen1_routes_are_gone` `[Arguments("GET", "/api/workflows")] [Arguments("GET", "/api/workflows/00000000-0000-0000-0000-000000000001")] [Arguments("POST", "/api/workflows/00000000-0000-0000-0000-000000000001/gates/approve")] [Arguments("GET", "/api/settings/templates")] [Arguments("GET", "/api/settings/template-groups")] [Arguments("GET", "/api/audit/conversation?workflowId=00000000-0000-0000-0000-000000000001")] [Arguments("GET", "/api/audit/cost-summary?workflowId=00000000-0000-0000-0000-000000000001")] [Arguments("GET", "/api/audit/cost-ledger")] [Arguments("GET", "/api/projects/00000000-0000-0000-0000-000000000001/feature-status/x")]` → `HttpStatusCode.NotFound`.
- V-D4: kept routes | I | `Gen1RemovalGuardTests.Kept_routes_still_serve` | `GET /api/settings/providers` 200 JSON array of 3; `GET /api/settings/providers/{AnthropicProviderId}` 200; `GET /api/audit?take=5` 200 with `items` and `total`; `GET /api/audit?workflowId=<guid>` 200 (unknown query ignored; stated default); `DELETE /api/audit/archive?olderThanDays=1` 200 with `archivedCount` (the default-retention path stays in `AuditArchiveEndpointTests`, R-7).
- V-D5: audit service | I | `Application/AuditServiceTests.RecordEventAsync_writes_a_row_whose_event_type_round_trips_as_its_stored_integer` `[Arguments(AuditEventType.SessionDisconnected, 17)] [Arguments(AuditEventType.ToolInvocation, 1)]` | the new signature `RecordEventAsync(eventType, summary, clientIp, userId, gitTagName, fullContentJson, ct)` → the row exists; raw `SELECT "EventType" … WHERE "Id" = …` equals the expected integer; `QueryAsync(from: null, to: null, minCost: null, maxCost: null, skip: 0, take: 10, ct)` includes it; with `EnableFullContent = false` `FullContent` is null.
- V-D6: hooks parse | U (`ParseHooks` made `internal static`) | `AgentSessionServiceIntegrationTests.ParseHooks_reads_hooks_from_legacy_yaml_with_stages` `[Arguments]`: row 1 `"name: legacy\nhooks:\n  before_run: echo hi\nstages:\n  - name: Run\n    executorType: raw\n"` → `BeforeRun.Command == "echo hi"`, `Timeout == 30 s`; row 2 the same with a malformed stage (`- executorType: raw`, no name) → still hooks (the removed branch threw); row 3 Markdown front matter with `hooks:` → hooks through the loader; row 4 content without `hooks:` → `WorkflowHooks.Empty`.
- V-D7: readiness | I | `ProjectReadinessTests.Readiness_has_no_workflow_template_key` | `dto.Checks.Select(c => c.Key)` equals the list at `:611-624` minus `ReadinessKeys.WorkflowTemplate` (11 keys, order preserved) and `ShouldNotContain("workflow-template")`; `ReadinessKeys` has no `WorkflowTemplate` member (compile); `:404-424` and `SeedTemplateAsync` deleted.
- V-D8: seeder | I | `DatabaseSeederTests.Seed_writes_admin_providers_config_sync_and_the_built_in_pipeline_only` | fresh schema, `new LlmSettings { Providers = { ["Anthropic"] = new() { BaseUrl = "https://alt.example", ApiKey = "k" } } }` → `Users` 1 (`DefaultAdminId`), `LlmProviders` 3 with Anthropic `BaseUrl == "https://alt.example"` and `ApiKey == "k"`, `PipelineDefinitions` 1; a second `SeedAsync` → the same counts; the three template / routing tests deleted; `DatabaseSeeder` exposes no `Bmad*` / `Routing*` ids (compile).
- V-D9: client | V | `App.test.tsx`: `ROUTES` without `/workflows` and `/workflow/wf-1`; the `DashboardPage`, `WorkflowDetailPage` and `useStreamingEvents` mocks removed; new `it('has no route for /workflows')` → `renderAt('/workflows')`, `expect(screen.queryByText(/Workflows page ready/)).toBeNull()`, `expect(screen.queryByRole('link', { name: 'Workflows' })).toBeNull()`. New `client/src/shared/Layout.test.tsx`: `renders the nav without a Workflows item` (link names exactly `['Home', 'Boards', 'Agents', 'Channels', 'Orchestrator', 'Settings']` in order). `SettingsPage.test.tsx`: the `/api/settings/templates` and `/api/settings/template-groups` handlers and `templateRequests` counters go; case 1 becomes `opens on the Projects tab and fetches readiness once` (`getByRole('tab', { name: /projects/i })` has `aria-selected="true"`, `readinessRequests === 1` with no click); case 2 keeps its routing assertions; case 3 renamed `?tab=routing selects the tab and mounts routing queries`. `useSignalRInvalidation.test.ts` new `it('registers no gen-1 workflow events')` → `connection.on` never called with any of `WorkflowStatusChanged`, `StageCompleted`, `GateReady`, `GateActioned`, `ArtifactUpdated`, `CascadeTriggered`, and still called with `BoardChanged`, `CardChanged`, `WorkflowReloaded`. `ProjectDeleteDialog.test.tsx` fixtures lose `workflowCount`.
- V-D10: build guards | build | `dotnet build Antiphon.sln --property:OutputPath=bin-c558d/` clean; `npx tsc --noEmit` clean (excess-property errors surface every one of the 24 `defaultWorkflowTemplateId` sites); `HasPendingModelChanges` inside V-D1.
- V-D11: plain prompt for non-Markdown content | U (both `BuildPrompt`s made `internal static`) | `Application/CardServiceTests.BuildPrompt_returns_the_plain_prompt_for_non_markdown_board_content` and `Application/OrchestratorServiceTests.BuildPrompt_returns_the_plain_prompt_for_non_markdown_workflow_content` | content `name: E11\nstages:\n  - name: Run\n    executorType: raw` → the prompt equals `Work on card <id>: <title>\n\nDescription:\n<desc>` after `ReplaceLineEndings("\n")` and `ShouldNotContain("Workflow:")`; a Markdown front-matter arm still goes through `WorkflowDefinitionLoader.RenderPrompt`.
- V-D12: deletion impact | U + V | `ProjectDeletionTests:283` arm removed; `ProjectDeletionImpactDto` has no `WorkflowCount` (compile); client side in V-D9.
- V-D13: docs | U | `PipelineDefinitionDocumentationTests.Api_doc_no_longer_lists_gen1_routes` | `docs/antiphon-api.md` contains none of `/api/workflows`, `/api/settings/templates`, `/api/audit/cost-summary`; `docs/project-context.md` does not contain `IStageExecutor`.
- V-D14: E2E | I | `Antiphon.E2E` minus `WorkflowDeleteTests` (6) and `WorkflowOutputTests` (4) = 82 green; `AgentE2ETests.CreateWorkflowTemplateAsync` / `SetAgentDefaultWorkflowTemplateAsync` removed; `AntiphonAppFixture` has no `UseMockExecutor` (compile; `OutputDistillationApplyCanaryTests:58` drops the argument).

### Guards the regression
- R-1: `AgentTaskPipelineStatusTests` — 29 methods unchanged; decisive: `a_plan_settled_next_test_design_is_ready_on_test_design_not_code` and `next_land_decide_none_yield_no_ready_even_with_a_plan_doc` pass with `OffPipeline` defaulted false.
- R-2: `InstructionBundleTests` — 38 → 37 unchanged + V-B9; decisive: `the_catalog_holds_exactly_the_bundles_that_ship` (this card adds no bundle); the deleted method is `the_orchestrator_preset_enables_remote_control_and_the_full_feature_pipeline` (D), replaced by `the_orchestrator_preset_enables_remote_control` asserting `RemoteControlEnabled` only.
- R-3: `DelegateBundleLaunchTests` — 15 unchanged; decisive: `a_worker_launches_with_the_delegate_basics_bundle_under_its_versioned_header` (a task with NO stage row still composes `[bundle:stage-code v…]` first).
- R-4: `MutationDispatchTests` — 4 unchanged; decisive: `C470_retained_worktree_launch_is_fresh` (`[bundle:stage-mutation v` still first for a Mutation task without a stage row).
- R-5: `PipelineHandoffParseTests` — 31 methods / rows unchanged (D-12: no alias added or removed); decisive: `every_token_and_alias_normalises_to_the_canonical_kind`, all 19 rows.
- R-6: the four legacy-YAML fixtures unedited (`git diff --stat <landed-A-sha>..HEAD -- <four files>` empty at D's land) and their consumers green: `AgentChannelServiceIntegrationTests`, `AgentSessionServiceIntegrationTests`, `OrchestratorServiceIntegrationTests`, and the unheaded `RealCliStubBServerHarness` consumers `SpecialistToolPolicyTests`, `GrokRulesDispatchAcceptanceTests`, `GrokRulesLiveMappedDispatchTests`, `GrokRulesCompactionAcceptanceTests`, `GrokRulesHerdrAcceptanceTests`, `CodexCommandLengthSessionTests`, `CodexBootWedgeProbeTests`. Decisive: `OrchestratorServiceIntegrationTests:169` `adapter.SentPrompt.ShouldContain(graph.Card.Identifier)` and `AgentSessionServiceIntegrationTests:800` — the prompt no longer carries `Workflow: E05`, and nothing asserted it.
- R-7: `AuditArchiveEndpointTests` (1) and `DataRetentionServiceTests` (16) unedited; decisive: `Omitting_olderThanDays_uses_the_configured_RetentionDays_not_a_hardcoded_90`.
- R-8: `HomeTaskServiceIntegrationTests:44-56` (`WaitingForHumanReview` → `NeedsHuman` / `Gate`) unchanged apart from the seeding helper's shape: reserved values keep their classification.
- R-9: `KanbanPersistenceTests` and `ProjectDeletionTests` cascade assertions unchanged in content; only the run / stage fixture shape changes.
- R-10: `CommitOnSettleMigrationTests`, `AgentTaskInternalDecisionMigrationTests`, `StandingSpecialistRoutingMigrationTests` unedited and green after A and after D — they migrate below both new migrations on every run, so A.Down / A.Up and D.Down / D.Up execute in the ordinary suite, not only in the new tests.
- R-11: `CardRow.test.tsx`, `CardModal.test.tsx` existing cases; `BoardServiceIntegrationTests:35-77` keeps every column / label assertion and swaps the three run assertions for null (D-11).
- R-12: `AgentPresetsTests` — 7 → 7 with the two `DefaultWorkflowTemplateId` lines deleted (`apply_omit_takes_the_orchestrator_preset`, `apply_no_preset_uses_the_hard_defaults`); no other assertion changes.
- R-13: `ProjectSetupServiceTests` (`:183` deleted; `SeededWorkflowTemplates` calls removed) and `AgentReplyStyleEndpointTests` (2 calls removed) — behaviour assertions unchanged.
- R-14: after D, full `Antiphon.Tests` chunked by namespace minus the 51 deleted; `Antiphon.E2E` 82; Vitest minus the deleted suites — all green, counts reported.

### Guard inventory
Every guard maps 1:1 to a PC-n defined in the next section. "split" marks a guard the plan listed once but that is independently bypassable, so it gets one PC per bypass.

Group A
- G-A1: the S1 data step runs before the NOT NULL adds (V-A2) | PC-A1
- G-A2: `IX_CardWorkflowRuns_CardId_Open` is a filtered unique index (V-A4) | PC-A2
- G-A3: revision `StagesJson` is immutable after insert (V-A6) | PC-A3
- G-A4: a non-stage role is refused (V-A8) | PC-A4
- G-A5: a duplicate role is refused (V-A8) | PC-A5
- G-A6: a bundle key must be a shipping `stage-*` key, `InstructionBundles.IsStageBundleKey` (V-A8; B's launch-side fallback is G-B19, a different function) | PC-A6
- G-A7: `allowedNext` tokens must be aliases and, for stage tokens, present in the revision (V-A8) — split: unknown token | PC-A7a; known token, role absent | PC-A7b
- G-A8: empty stages refused (V-A8) | PC-A8
- G-A9: BuiltIn write / archive refusal (V-A9) | PC-A9
- G-A10: an archived definition is refused at pointer write (V-A11) | PC-A10
- G-A11: resolution order board > project > built-in (V-A12) | PC-A11
- G-A12: the seeder inserts once and appends no revision on reboot (V-A13) | PC-A12
- G-A13: the seeder appends on hash change and keeps r1 (V-A13) | PC-A13
- G-A14: the seeder never touches a Custom definition (V-A13) | PC-A14
- G-A15: the hash is over the canonical serialisation (V-A7) | PC-A15
- G-A16: A.Down deletes post-A runs before restoring the gen-1 columns (V-A5, R-10) | PC-A16
- G-A17: queue assignment creates no run (V-A17) | PC-A17
- G-A18: the script sends `null` on inherit (V-A15) | PC-A18
- G-A19: revision insert and pointer move are one transaction (V-A10) | PC-A19

Group B
- G-B1: ensure is idempotent (V-B1) | PC-B1
- G-B2: concurrent ensure catches the unique violation and re-reads (V-B2) | PC-B2
- G-B3: a card-bound stage Worker creates the run and links the row (V-B5) | PC-B3
- G-B4: helper / orchestrator / unbound tasks create nothing (V-B5) — split: kind check | PC-B4a; `IsStage` check | PC-B4b; card check | PC-B4c
- G-B5: an off-definition role is created with the warning, never refused (V-B5) | PC-B5
- G-B6: the run is created before the open gate (V-B5) | PC-B6
- G-B7: dispatch marks stage and run Running (V-B7b) | PC-B7
- G-B8: dispatch skips earlier Pending stages (V-B7b) | PC-B8
- G-B9: settle next→T completes S with the handoff summary and moves the pointer (V-B7) | PC-B9
- G-B10: settle land / decide / none / unmarked keeps the pointer (V-B7) | PC-B10
- G-B11: an off-pipeline next keeps the pointer and sets the header bit (V-B7, V-B12) — split: pointer | PC-B11a; header bit | PC-B11b
- G-B12: failed → stage Failed with the reason (V-B7) | PC-B12
- G-B13: blocked / canceled leave the stage unchanged (V-B7) | PC-B13
- G-B14: re-entry goes Completed → Running (V-B7) | PC-B14
- G-B15: card Done / Canceled → run Completed / Canceled through the lifecycle (V-B4) | PC-B15
- G-B16: archive cancels instead of refusing (V-B4) | PC-B16
- G-B17: resnapshot cancels the old run and creates from the current resolution (V-B3) | PC-B17
- G-B18: an explicit stage key replaces the role default (V-B9, V-B10) | PC-B18
- G-B19: a missing key falls back to the role default (V-B9, V-B10) | PC-B19
- G-B20: a missing key records a Warning event (V-B10b) | PC-B20
- G-B21: launch site `:4122` passes the stage-row key (V-B10) | PC-B21
- G-B22: warm-reuse drift site `:4645` passes the stage-row key (V-B10c) | PC-B22
- G-B23: the per-stage menu renders only the allowed tokens (V-B11) | PC-B23
- G-B24: the menu falls back to the literal when there is no row (V-B11) | PC-B24
- G-B25: `OffPipeline` is projected in `BuildReady` (V-B12) | PC-B25
- G-B26: the built-in transcribes the bundle prose (V-B16) | PC-B26
- G-B27: every built-in token is a known alias — D-12 (V-B15) | PC-B27
- G-B28: a SourceLanding Mutation task links to the companion stage (V-B13) | PC-B28
- G-B29: the home rail is Running only while a stage task is live (V-B8) | PC-B29

Group C
- G-C1: the revision POST body carries ordered stages with canonical `allowedNext` (V-C1) | PC-C1
- G-C2: inherit writes `null` (V-C2) | PC-C2
- G-C3: the role select is limited to stage roles (V-C1) | PC-C3
- G-C4: the bundle select is filtered to `stage-*` (V-C1) | PC-C4
- G-C5: the built-in is not editable, only clonable (V-C1) | PC-C5
- G-C6: the Pipelines tab fetches lazily (V-C4) | PC-C6

Group D (the plan's G-1..G-10, refined)
- G-D1 (plan G-1): Up drops the nine tables (V-D1) | PC-D1
- G-D2 (plan G-1): Up drops `Agents.DefaultWorkflowTemplateId` and the three audit columns (V-D1) — split: agent column | PC-D2a; audit columns | PC-D2b
- G-D3 (plan G-2): Down recreates the tables (V-D2, R-10) | PC-D3
- G-D4 (plan G-3): gen-1 routes 404 (V-D3) | PC-D4
- G-D5 (plan G-4): kept routes serve — split: `MapSettingsEndpoints` providers group | PC-D5a; `MapAuditEndpoints` GET + archive | PC-D5b
- G-D6 (plan G-5): `RecordEventAsync` persists (V-D5) | PC-D6
- G-D7 (plan G-6): `ParseHooks` reads legacy YAML that also has `stages:` (V-D6) | PC-D7
- G-D8 (plan G-7): readiness has no `workflow-template` key (V-D7) | PC-D8
- G-D9 (plan G-8): the seeder writes admin + providers + config sync + built-in and nothing else (V-D8) — split: providers | PC-D9a; config sync | PC-D9b
- G-D10 (plan G-9): client — split: nav item | PC-D10a; route table | PC-D10b; default tab | PC-D10c; invalidation map | PC-D10d
- G-D11 (plan G-10): model / migration parity, `HasPendingModelChanges` (V-D1) | PC-D11
- G-D12: plain prompt for non-Markdown content in `CardService` (V-D11) | PC-D12
- G-D13: plain prompt in `OrchestratorService` (V-D11) | PC-D13
- G-D14: `AuditEventType` stored integers unchanged (V-D5) | PC-D14
- G-D15: docs negative pins (V-D13) | PC-D15

Counts: guards = 19 (A) + 29 (B) + 6 (C) + 15 (D) = 69; mapped = 69; missing = 0; duplicate PC mappings = 0. The plan's G-1..G-10 are all present (G-1 → G-D1 + G-D2, G-4 → G-D5a/b, G-8 → G-D9a/b, G-9 → G-D10a-d, G-10 → G-D11).

### Positive controls
Each: break the guard by a compiling defect; expect the exact method red at the named assertion. Filters are `--treenode-filter "/*/*/<Class>/<Method>"` on the `Antiphon.Tests` build in `bin-pc/` unless marked V (Vitest: `npx vitest run <file> -t "<name>"`). Restore, touch the restored file's `LastWriteTime`, rebuild, rerun green. A thrown exception inside the test body is red; a build error or a zero-test run is not.

- PC-A1: delete the `migrationBuilder.Sql("DELETE FROM \"CardWorkflowStages\"; DELETE FROM \"CardWorkflowRuns\";")` call from `AddPipelineDefinitions.Up`; expect `PipelineDefinitionMigrationTests.Up_deletes_every_legacy_run_and_stage_before_the_not_null_adds` red at `migrator.MigrateAsync()` throwing `PostgresException` 23502, and `AddPipelineDefinitions_operations_create_tables_repoint_runs_and_add_the_open_run_index` red at the `SqlOperation`-precedes-`AddColumn` index assertion.
- PC-A2: remove `.HasFilter("\"Status\" IN (0, 1)")` from the index configuration and `filter:` from the migration's `CreateIndex`; expect `Open_run_index_refuses_a_second_open_run_and_allows_a_closed_one("Completed,Queued")` red at the second `SaveChangesAsync` throwing.
- PC-A3: delete `entity.Property(r => r.StagesJson).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw)`; expect `Revision_columns_cannot_change_after_insert("StagesJson")` red at `Should.ThrowAsync<InvalidOperationException>`.
- PC-A4: replace `if (!AgentTaskRoles.IsStage(spec.Role))` with `if (false)`; expect `Create_refuses_invalid_stages("Docs")` red at `ex.Code.ShouldBe("pipeline_stage_role_not_stage")`.
- PC-A5: skip the duplicate-role check; expect `Create_refuses_invalid_stages("Code twice")` red at the code assertion.
- PC-A6: make `InstructionBundles.IsStageBundleKey` return `All.ContainsKey(key)` (drop the prefix test); expect `Create_refuses_invalid_stages("board-api")` red at `pipeline_stage_bundle_not_stage` and `InstructionBundleTests.is_stage_bundle_key_accepts_only_shipping_stage_bundles("board-api", false)` red.
- PC-A7a: skip the alias lookup on `allowedNext`; expect `Create_refuses_invalid_stages("ship")` red at `pipeline_stage_next_unknown`.
- PC-A7b: skip the present-in-revision check; expect `Create_refuses_invalid_stages("mutation without Mutation")` red at `pipeline_stage_next_not_in_revision`.
- PC-A8: change `stages.Count == 0` to `stages.Count < 0`; expect `Create_refuses_invalid_stages("empty")` red at `pipeline_stages_empty`.
- PC-A9: skip the `Source == BuiltIn` refusal in `AddRevisionAsync`; expect `BuiltIn_refuses_revision_and_archive_but_clones` red at `Should.ThrowAsync<ConflictException>` on the revision call.
- PC-A10: skip the `ArchivedAt is not null` refusal in `BoardService.SetPipelineAsync`; expect `Archived_definition_is_refused_at_pointer_write_but_an_existing_pointer_still_resolves` red at the `ConflictException` assertion.
- PC-A11: swap the board and project branches in `PipelineResolution.ResolveForCardAsync`; expect `Board_pointer_outranks_project_default_which_outranks_the_built_in("board")` red at `Source.ShouldBe("board")`.
- PC-A12: in `SeedPipelineDefinitionsAsync` replace `active.ContentHash != constant.Hash` with `true`; expect `Pipeline_seed_inserts_the_built_in_once_and_appends_a_revision_only_when_the_constant_hash_changes` red at the second-boot `revisions.Count.ShouldBe(1)`.
- PC-A13: replace the same comparison with `false`; expect the same method red at the changed-constant `revisions.Count.ShouldBe(2)`.
- PC-A14: drop the `Source == BuiltIn` filter from the seeder's definition lookup (match by name); expect the same method red at the Custom definition's unchanged-hash assertion.
- PC-A15: hash the raw incoming JSON string instead of the canonical serialisation; expect `Serialise_is_canonical_and_hash_is_eight_lowercase_hex` red at the two-orderings `Hash` equality.
- PC-A16: delete the `DELETE …` `Sql` from `AddPipelineDefinitions.Down`; expect `Down_removes_post_A_runs_then_restores_gen1_columns_and_Up_returns_to_head` red at `MigrateAsync(<before A>)` throwing.
- PC-A17: in `AssignCardAsync` add `_db.CardWorkflowRuns.Add(CardWorkflowRunFactory.CreateFromRevision(card, builtIn, builtIn.ActiveRevision!, now))` after the queue-position write; expect `AssignCardAsync_assigns_card_to_next_queue_position_and_creates_no_run` red at `CardWorkflowRuns.Count().ShouldBe(0)`.
- PC-A18: in `scripts/pipeline-definition.ps1` `set-board -Inherit` send `{}` instead of `{ pipelineDefinitionId: null }`; expect `PipelineDefinitionScriptTests.SetBoard_Inherit_puts_null` red at `ValueKind.ShouldBe(JsonValueKind.Null)`.
- PC-A19: save the revision row, then validate, then move the pointer (two `SaveChangesAsync`); expect `AddRevision_appends_N_plus_1_and_moves_the_active_pointer_in_one_transaction` red at `revisions.Count.ShouldBe(1)` after the failed validation.
- PC-B1: remove the "return the open run if present" early return; expect `EnsureActiveRun_creates_once_then_returns_the_open_run` red at `second.Id.ShouldBe(first.Id)` (or the unique violation surfacing).
- PC-B2: remove the `catch (DbUpdateException) when (IsOpenRunViolation(ex))` re-read; expect `Two_concurrent_ensures_yield_one_run` red at `await second` throwing `DbUpdateException`.
- PC-B3: guard the `EnsureActiveRunAsync` call with `if (false)`; expect `A_card_bound_stage_role_worker_creates_the_run_and_links_the_stage_row(Code)` red at `CardWorkflowStageId.ShouldNotBeNull()`.
- PC-B4a: drop `request.Kind == AgentTaskKind.Worker` from the condition; expect `Helper_roles_orchestrator_kind_and_unbound_tasks_create_nothing(Orchestrator, Plan, true)` red at `Count().ShouldBe(0)`.
- PC-B4b: replace `AgentTaskRoles.IsStage(request.Role)` with `true`; expect the `(Worker, Docs, true)` row red.
- PC-B4c: replace `binding.CardId is not null` with `true` and resolve the card from `request.Card` when the binder returned none; expect the `(Worker, Code, false)` row red at `CardWorkflowRuns.Count().ShouldBe(0)`.
- PC-B5: throw `ValidationException` instead of appending the warning; expect `A_stage_role_outside_the_run_definition_is_created_with_a_pipeline_warning` red at the create call throwing.
- PC-B6: move the ensure call after the gate transaction commits; expect `Run_creation_happens_before_the_open_gate_and_a_gate_refusal_leaves_the_run` red at `CardWorkflowRuns.Count().ShouldBe(1)`.
- PC-B7: in the dispatcher's Dispatched transition skip setting the stage row to `Running`; expect `Dispatch_marks_the_stage_running_and_skips_earlier_pending_stages` red at `code.Status.ShouldBe(Running)`.
- PC-B8: skip the earlier-Pending → `Skipped` loop; expect the same method red at `investigate.Status.ShouldBe(Skipped)`.
- PC-B9: after settle leave `CurrentStageId` untouched; expect `Settle_next_review_completes_code_and_moves_the_pointer_to_review` red at `run.CurrentStageId.ShouldBe(reviewRow.Id)`.
- PC-B10: on land / decide / none set `CurrentStageId = null`; expect `Settle_next_land_decide_none_or_unmarked_completes_the_stage_and_keeps_the_pointer("land")` red at the pointer assertion.
- PC-B11a: on an off-run next move the pointer to the first stage; expect `Settle_next_role_outside_the_run_completes_the_stage_keeps_the_pointer_and_flags_off_pipeline` red at the pointer assertion.
- PC-B11b: never append `pipeline=off`; expect `DelegationUnitTests.completion_header_carries_pipeline_off_after_next_only_when_the_handoff_is_outside_the_run` red at `ShouldContain("next=review pipeline=off")`.
- PC-B12: on task Failed mark the stage `Completed`; expect `Settle_failed_marks_the_stage_failed_with_the_reason` red at `Status.ShouldBe(Failed)`.
- PC-B13: on Blocked mark the stage `Failed`; expect `Settle_blocked_leaves_the_stage_unchanged` red at `Status.ShouldBe(Running)`.
- PC-B14: when dispatching onto a `Completed` stage leave it `Completed`; expect `A_re_entered_stage_goes_completed_to_running_again` red.
- PC-B15: in `CardLifecycleTransitions` restore `card.ActiveWorkflowRunId = null` and skip `CompleteAsync`; expect `Complete_maps_card_terminal_status_to_run_status(Done, Completed)` red at `run.Status.ShouldBe(Completed)`.
- PC-B16: restore the `ConflictException` at `CardService.ArchiveAsync:916-927`; expect `Archive_cancels_the_open_run_instead_of_refusing` red at the archive call throwing.
- PC-B17: in `ResnapshotAsync` reuse the old revision id instead of re-resolving; expect `Resnapshot_cancels_the_open_run_and_creates_one_from_the_current_resolution` red at `newRun.PipelineDefinitionRevisionId.ShouldBe(customRevision.Id)`.
- PC-B18: in `ForDelegate` ignore `stageBundleKey`; expect `InstructionBundleTests.an_explicit_stage_bundle_key_replaces_the_role_default` red at `ShouldBe(["stage-review", "delegate-basics"])`.
- PC-B19: when the key is missing return `[DelegateBasics]` only; expect `a_missing_stage_bundle_key_falls_back_to_the_role_default` red.
- PC-B20: skip writing the `AgentTaskEvent` on fallback; expect `CardWorkflowRunDispatchTests.A_vanished_stage_bundle_key_launches_on_the_role_default_and_records_a_warning_event` red at `events.ShouldHaveSingleItem()`.
- PC-B21: at `AgentTaskDispatcher.cs:4122` pass `stageBundleKey: null`; expect `DelegateBundleLaunchTests.a_stage_row_bundle_key_outranks_the_role_default_at_launch` red at `ShouldStartWith("[bundle:stage-review v")`.
- PC-B22: at `:4645` pass `stageBundleKey: null`; expect `AgentTaskPoolTests.warm_grok_reuse_recomputes_the_desired_rules_from_the_stage_row` red at `ShouldBe(ReuseOutcome.SpawnFresh)`.
- PC-B23: make `StageHandoffContractFor` return `StageHandoffContract` unconditionally; expect `stage_handoff_contract_for_renders_only_the_allowed_tokens` red at `ShouldContain("next: <review|code|decide>")`.
- PC-B24: return `""` for null / empty; expect `stage_handoff_contract_for_falls_back_to_the_literal_for_null_or_empty` red.
- PC-B25: pass `OffPipeline: false` unconditionally in `BuildReady`; expect `a_ready_row_is_projected_with_off_pipeline_true_when_the_source_next_was_outside_its_run` red.
- PC-B26: remove `"investigate"` from the Plan stage's `AllowedNext` in `PipelineDefinitions.StandardPipeline`; expect `the_built_in_transcribes_each_stage_bundles_next_line` red at the Plan row (V-A13's exact-set assertion is red too).
- PC-B27: add `"ship"` to the Review stage's `AllowedNext`; expect `every_allowed_next_token_in_the_built_in_is_a_known_handoff_alias` red at `Kind.ShouldNotBeNull()` for `ship`.
- PC-B28: skip `EnsureActiveRunAsync` when `request.SourceLandingOperationId is not null`; expect `C558_source_landing_mutation_task_links_to_the_companion_run_mutation_stage_and_skips_earlier_stages` red at `CardWorkflowStageId.ShouldNotBeNull()`.
- PC-B29: in `HomeTaskService.ClassifyCard` treat `workflowStatus == Queued` as Running; expect `A_card_is_running_only_while_a_stage_task_is_live_and_stage_shows_the_pointer` red at the post-settle group assertion.
- PC-C1 (V): omit `allowedNext` from the revision POST body; expect `PipelineDefinitionsPanel.test.tsx` "saving posts a revision…" red at `toEqual`.
- PC-C2 (V): send `undefined` instead of `null` on inherit; expect `BoardPipelinePicker.test.tsx` "choosing inherit writes null" red at `body.pipelineDefinitionId === null`.
- PC-C3 (V): add `'Docs'` to the role option list; expect "editor limits the role select…" red at the options equality.
- PC-C4 (V): remove the `startsWith('stage-')` filter; expect the same test red at the bundle options equality.
- PC-C5 (V): enable Revise when `source === 'BuiltIn'`; expect "lists definitions… disables revise on the built-in" red at `toBeDisabled()`.
- PC-C6 (V): mount the panel eagerly (`keepMounted`); expect "does not fetch pipeline definitions until the Pipelines tab is opened" red at `expect(requests).toBe(0)`.
- PC-D1: delete `migrationBuilder.DropTable(name: "TemplateGroups")` from `RemoveGen1WorkflowEngine.Up` (the last drop; nothing references it once `WorkflowTemplates` is gone); expect `Up_drops_nine_tables_and_two_column_sets_and_keeps_agent_and_audit_rows` red at `tables.ShouldNotContain("TemplateGroups")`.
- PC-D2a: delete the `DropColumn(name: "DefaultWorkflowTemplateId", table: "Agents")` (keep its FK / index drops); expect the same method red at `agentColumns.ShouldNotContain("DefaultWorkflowTemplateId")`.
- PC-D2b: delete `DropColumn(name: "StageExecutionId", table: "AuditRecords")`; expect the same method red at `auditColumns.ShouldNotContain("StageExecutionId")`.
- PC-D3: delete `CreateTable(name: "TemplateGroups", …)` from `Down`; expect `Down_recreates_the_nine_tables_empty_and_the_four_columns_nullable_then_Up_returns_to_head` red at `tables.ShouldContain("TemplateGroups")` (R-10's three tests go red as well at their own `MigrateAsync`).
- PC-D4: add `app.MapGet("/api/workflows", () => Results.Ok(Array.Empty<object>()));` to `Program.cs`; expect `Gen1_routes_are_gone("GET", "/api/workflows")` red at `ShouldBe(HttpStatusCode.NotFound)`.
- PC-D5a: remove `app.MapSettingsEndpoints();` (`Program.cs:839`); expect `Kept_routes_still_serve` red at the providers `ShouldBe(OK)`.
- PC-D5b: remove `app.MapAuditEndpoints();`; expect the same method red at the `GET /api/audit` `ShouldBe(OK)`.
- PC-D6: remove `await _db.SaveChangesAsync(cancellationToken)` from `RecordEventAsync`; expect `RecordEventAsync_writes_a_row_whose_event_type_round_trips_as_its_stored_integer(SessionDisconnected, 17)` red at `row.ShouldNotBeNull()`.
- PC-D7: in `AgentSessionService.ParseHooks` add `if (activeDefinition.Content.Contains("stages:", StringComparison.Ordinal)) return WorkflowHooks.Empty;` before the `ParseYamlHooks` fall-through; expect `ParseHooks_reads_hooks_from_legacy_yaml_with_stages(row 1)` red at `BeforeRun.ShouldNotBeNull()`.
- PC-D8: add `Check("workflow-template", ReadinessLevel.Required, ReadinessStatus.Ok, "x", null, null)` to the readiness list; expect `Readiness_has_no_workflow_template_key` red at `ShouldNotContain("workflow-template")`.
- PC-D9a: comment out `await SeedDefaultProvidersAsync(db, cancellationToken)`; expect `Seed_writes_admin_providers_config_sync_and_the_built_in_pipeline_only` red at `LlmProviders.Count().ShouldBe(3)`.
- PC-D9b: comment out `await SyncProviderConfigAsync(…)`; expect the same method red at `BaseUrl.ShouldBe("https://alt.example")`.
- PC-D10a (V): re-add `{ to: '/workflows', label: 'Workflows' }` to `NAV_ITEMS`; expect `Layout.test.tsx` "renders the nav without a Workflows item" red at the link-names equality.
- PC-D10b (V): re-add `<Route path="workflows" element={<div>Workflows page ready</div>} />`; expect `App.test.tsx` "has no route for /workflows" red.
- PC-D10c (V): change `?? 'projects'` back to `?? 'templates'` and re-add `'templates'` to `SETTINGS_TABS`; expect `SettingsPage.test.tsx` "opens on the Projects tab and fetches readiness once" red at `aria-selected`.
- PC-D10d (V): re-add the `WorkflowStatusChanged` mapping; expect `useSignalRInvalidation.test.ts` "registers no gen-1 workflow events" red.
- PC-D11: re-add `public Guid? DefaultWorkflowTemplateId { get; set; }` to `Agent` and `entity.Property(a => a.DefaultWorkflowTemplateId);` in `AppDbContext` without a migration; expect `Up_drops_nine_tables_and_two_column_sets_and_keeps_agent_and_audit_rows` red at `HasPendingModelChanges().ShouldBeFalse()`.
- PC-D12: in `CardService.BuildPrompt` append `"\n\nWorkflow: legacy"` when the content is non-Markdown; expect `BuildPrompt_returns_the_plain_prompt_for_non_markdown_board_content` red at `ShouldNotContain("Workflow:")`.
- PC-D13: the same in `OrchestratorService.BuildPrompt`; expect `BuildPrompt_returns_the_plain_prompt_for_non_markdown_workflow_content` red.
- PC-D14: swap the values of `SessionDisconnected` and `StageStarted` (`= 2` / `= 17`); expect `RecordEventAsync_writes_a_row_whose_event_type_round_trips_as_its_stored_integer(SessionDisconnected, 17)` red at the raw-SQL `ShouldBe(17)`.
- PC-D15: re-insert the line `GET    /api/workflows` into `docs/antiphon-api.md`; expect `Api_doc_no_longer_lists_gen1_routes` red.

Mutation reports break, red, restore, green after land per `docs/testing-and-build.md` §Mutation-stage positive-control execution; Code implements the tests and runs the V/R rows; ordinary Review judges them before land.

### Out of scope
- Delivery of the completion header to the caller session: an existing, unchanged path pinned elsewhere (§Delivery inventory).
- A definition with no active revision: unreachable (create inserts r1 in the same transaction; archive keeps the pointer), so untested; if Code makes it reachable it adds a test and a 409 code.
- Per-definition tier / WIP / pin overrides, display labels, `AllowedNext` enforcement at settlement, backfilling closed cards (plan §Out of scope).
- The six `*RealCliStubProxyCanaryTests` consumers of `RealCliStubBServerHarness`: headed / real-CLI canaries on the nightly lane; the fixture is exercised by the seven unheaded consumers in R-6.
- The rollout probe B-1 after the B restart: operator-owned, reported pending by Code (D-12).
- `App.test.tsx` "entry bundle": needs `client/dist`; D's rebuild satisfies it unchanged.
- Browser / E2E coverage of the pipeline panel: MSW-level only (V-C1); the panel has no live-session dependency.
- The `GitSettings.PollIntervalSeconds` now-unread setting (config-cleanup card, plan §Out of scope).

### Cost
All figures estimated unless marked measured. Build into a producer-owned `--property:OutputPath=bin-c558<group>/` (forward slash) and delete the ~12 `bin-c558*` directories at the end of each group.

| Group | Ordinary V/R floor (Code) | PC floor (Mutation) |
|---|---|---|
| A | build 3 min; Unit lane 2 min (measured 70 s, testing doc); named classes `PipelineDefinitionMigrationTests`, `PipelineStagesJsonTests`, `PipelineDefinitionServiceTests`, `PipelineResolutionTests`, `DatabaseSeederTests`, `PipelineDefinitionEndpointTests`, `PipelineDefinitionScriptTests`, `PipelineDefinitionDocumentationTests`, `CardWorkflowRunFactoryTests`, `AgentServiceIntegrationTests`, `BoardServiceIntegrationTests`, `KanbanPersistenceTests`, `ProjectDeletionTests`, `HomeTaskServiceIntegrationTests`, `CommitOnSettleMigrationTests`, `AgentTaskInternalDecisionMigrationTests`, `StandingSpecialistRoutingMigrationTests` 12 min; Vitest type parity 3 min → **20 min** | 19 PCs × 4 min method-scoped (mutate, incremental build ~1.5 min, red, restore + touch, build, green) in 4 independent batches (migration / service / seeder / script) → **55 min** |
| B | build 3 min; named classes `CardWorkflowRunServiceTests`, `AgentTaskCreateSnapshotsRunTests`, `AgentTaskReplyIntegrationTests` (~10 min), `CardWorkflowRunDispatchTests`, `HomeTaskServiceIntegrationTests`, `InstructionBundleTests`, `DelegateBundleLaunchTests`, `AgentTaskPoolTests`, `DelegationUnitTests`, `AgentTaskPipelineStatusTests`, `MutationDispatchTests`, `PipelineHandoffParseTests`, `PipelineDefinitionsBuiltInTests`, `PipelineDefinitionDocumentationTests`, `DelegationHarnessCensusTests` 25 min → **30 min** | 29 PCs × 4 min in 5 batches (service / create / settle / dispatch + launch / menu + status) → **85 min**; above the 15-20-row line, so two worktree shards off the task branch are permitted → ~50 min wall |
| C | `npx tsc --noEmit` 1 min; `pwsh -File scripts/test-client.ps1` 3 min → **5 min** | 6 Vitest PCs × 1 min (single file, no build) → **6 min** |
| D | build both solutions 5 min; full `Antiphon.Tests` chunked by namespace (25.5 min per the CARD-0110 measure; the doc's 2026-09-10 broad slice ran 94 min under shared-host load, so plan 30-90); `Antiphon.E2E` 82 tests ~35 min; Vitest 3 min → **75 min (band 60-130)** | 21 PCs: 13 server × 4 min in 4 batches (Up / Down / routes + audit / seeder + readiness + prompts) + 4 Vitest × 1 min + PC-D11 + PC-D14 → **70 min** |
| Total | **130 min** (band 115-185), build included | **215 min** (band 150-240); ~150 min wall with B sharded |

Total verification floor = build (14 min, inside the rows) + V/R 130 min + PC 215 min ≈ **345 min** unsharded, ≈ 280 min with B sharded. Savings against "full suite per group" (4 × 25.5 min `Antiphon.Tests` + 4 × 35 min E2E ≈ 240 min of V/R) ≈ 110 min, from class-scoped V/R in A / B / C and one full run only in D, the only group that deletes across the tree. No group has a zero PC floor: each changes a server-enforced or user-visible invariant.
