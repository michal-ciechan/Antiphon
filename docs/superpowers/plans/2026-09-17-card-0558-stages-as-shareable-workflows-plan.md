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
