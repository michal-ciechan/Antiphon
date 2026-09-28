# CARD-0558 investigation: stages as shareable per-project workflows, and reusable agent definitions

Date: 2026-09-17. Task 7102ff54 (Investigate, Worktree). Evidence only; no fix designed here.

## Outcome in one line

The dormant `activeWorkflowRunId` / `workflowRunStatus` / `currentWorkflowStageName` fields are
the per-card "run" half of an agent-queue workflow engine designed in May 2026 whose engine was
never built: one writer (queue assignment, an endpoint the pipeline never uses), zero state
transitions anywhere in the code, one row ever in the database, and a definition model
(`WorkflowTemplate`, gen-1 BMAD YAML with `executorType`/`modelName`/`gateRequired`) that
has no relation to `AgentTaskRole`, bundles or tiers. They are reusable in shape (per-card run +
ordered stage rows + definition snapshot + client/home-rail/archive plumbing) but not in
substance, so this card is neither "build from scratch" nor "wire up what is there": it is a
re-pointing of an existing run skeleton onto a definition object that does not exist yet, plus a
decision about what a stage's identity is once it is data rather than an enum member.

## 1. Three workflow generations exist; none is the delegated-task pipeline

| Gen | When / commit | Definition | Run state | Engine | Live rows (2026-09-17) |
|---|---|---|---|---|---|
| 1 "feature workflow engine" | 2026-03-16, `b808fe5e` (Story 1.8), `1e84fd93` (2.1), `bbc85c86` (2.2) | `WorkflowTemplate` (global YAML, `TemplateGroup` BMAD, `ModelRouting`) | `Workflow`, `Stage`, `GateDecision`, `StageExecution` | `WorkflowEngine` (915 lines, sequential `IStageExecutor`, gates, cascades, git branch per workflow), `AgentExecutor` registered (`Program.cs:479-486`) | 3 seeded templates, 1 group, 10 routings, **0 Workflows, 0 Stages** |
| 2 "Symphony WORKFLOW.md" | 2026-05-15/16, `a734b299` (e04), `796457cf` (e05), `d6720b89` (e09) | `BoardWorkflowDefinition` per board: YAML front matter + Markdown prompt, versioned, file-mirrored | `RunAttempt.BoardWorkflowDefinitionId` pins the version | none: it is the card-spawn prompt template + tracker/hooks config | Antiphon board: 3 versions, v3 active since 09-04; 39 of 71 RunAttempts pin one |
| 3 "agent queues" | 2026-05-18, `edae6eaa` (persistence), `eb4addec` (queue service) | reuses gen-1 `WorkflowTemplate` via `Agent.DefaultWorkflowTemplateId` | `CardWorkflowRun`, `CardWorkflowStage`, `Card.ActiveWorkflowRunId` | **never built** (see §2) | 1 run, 2 stages, 0 cards linked |
| (live) "stage roles" | 2026-09-03/05, CARD-0146 `dca5b06e`, `aa3c7c93`; CARD-0470 added Mutation | `AgentTaskRole` enum + `server/Bundles/stage-<role>.md` + prose | bound `AgentTask` rows: `Role`, `Status`, `NextStage`, `NextHandoff` | the orchestrator (human/agent) dispatches from the header `next=`; server projects ready rows | 1,519 stage-role tasks across 5 boards |

Gen 1 is reachable (nav item `Workflows`, `client/src/shared/Layout.tsx:73`; `DashboardPage`,
`WorkflowDetailPage`, settings `TemplateManager`; routes `/api/workflows/*`, `/api/settings/templates/*`,
gates, cascade — `docs/antiphon-api.md:602-609`) and has never been used on this instance.

The CARD-0146 plan looked at exactly this and deferred it (`docs/superpowers/plans/2026-09-03-card-0146-stage-pipeline-handoff-plan.md:38`):
"`Workflow` / `Stage` / `StageExecution` / `CardWorkflowRun` / `CardWorkflowStage` /
`WorkflowDefinition` are the card-spawn YAML workflow-template path ... Delegated tasks never
touch it. Not this card. Unifying the two is a separate, larger decision; nothing here makes it
harder." CARD-0558 is that decision.

## 2. Priority 1: what the dormant Card fields were built for

### Origin and stated intent

- Added by `edae6eaa` (2026-05-18, "feat(agents): add queue persistence model"), the only commit
  ever to touch `CardWorkflowRun.cs` / `CardWorkflowStage.cs`; `Card.ActiveWorkflowRunId`
  entered `Card.cs` in the same commit (`git log -S ActiveWorkflowRunId`).
- Design: `docs/superpowers/specs/2026-05-18-agent-queues-design.md`. Lines 14-17: "Each card
  gets its own workflow run and stage state. Agent default workflow is used unless a card
  overrides it. Reuse existing workflow template/YAML concepts first. Antiphon owns workflow
  state and gates; agents report/request progress." Lines 100-106: on queue assignment the
  agent's default workflow is snapshotted into a run; "Antiphon remains authoritative for
  workflow state. It sends stage prompts to the persistent agent session, records stage
  transitions, and pauses at gates." Line 85: snapshot per run "so later edits to agent defaults
  or templates do not rewrite active card behavior."
- Plan: `docs/superpowers/plans/2026-05-18-agent-queues-foundation.md:20,28,38-54`. Line 28
  lists "Stage execution against persistent agents" as a later slice. The slice-4 plan
  (`2026-05-20-agent-queues-slice-4-persistent-sessions.md`) never mentions the run again
  (grep: 0 hits for `CardWorkflowRun|workflow run|WorkflowRunStatus`).
- Planned endpoints that do not exist (grep of `server/Api`): `POST /api/cards/{id}/workflow/approve`,
  `/workflow/reject`, `/handoff`, `/compact` (spec lines 191-194).

### Entities (all still present)

- `server/Domain/Entities/CardWorkflowRun.cs`: `CardId`, `AgentId` (Restrict FK), `WorkflowTemplateId?`
  (gen-1), `WorkflowName`, `WorkflowDefinitionSnapshot` (YAML string), `Status`
  (`CardWorkflowRunStatus`: Queued/Running/WaitingForHumanReview/Completed/Failed/Canceled),
  `CurrentStageId?`, `FailureReason`, timestamps, `Stages`.
- `server/Domain/Entities/CardWorkflowStage.cs`: `StageOrder`, `Name`, `ExecutorType`, `ModelName`,
  `GateRequired`, `SystemPrompt`, `Status` (`CardWorkflowStageStatus`: Pending/Running/
  WaitingForHumanReview/Completed/Failed/Skipped), `ResultSummary`, `FailureReason`.
- `server/Domain/Entities/Card.cs:17,112,118`: `ActiveWorkflowRunId`, `ActiveWorkflowRun`,
  `WorkflowRuns`. `AppDbContext.cs:975,1007-1009,1049`: index + one-to-one FK + entity config.
- `server/Domain/Entities/Agent.cs:18,195`: `DefaultWorkflowTemplateId`, `WorkflowRuns`.
- Migration `server/Migrations/20260518102948_AgentQueuesFoundation.cs`.

### Every writer, in code

| Path | What it does | File:line |
|---|---|---|
| `POST /api/agents/{id}/queue` | the ONLY creator: `CardWorkflowRunFactory.CreateFromAgentDefaultAsync` snapshots `Agent.DefaultWorkflowTemplateId` (or the first template by name) into a Queued run with Pending stages, links `card.ActiveWorkflowRun` | `AgentEndpoints.cs:187-194`, `AgentService.cs:826-870`, `CardWorkflowRunFactory.cs:21-80` |
| `DELETE /api/agents/{id}/queue/{cardId}` | nulls `ActiveWorkflowRunId` (run row left behind) | `AgentService.cs:947-951` |
| finished-card dequeue | nulls it | `CardLifecycleTransitions.cs:77-78` |
| agent delete | nulls it, deletes the agent's runs (cycle-safe) | `AgentService.cs:787-815` |
| project cascade delete | bulk-nulls, deletes stages/runs/definitions | `ProjectCascade.cs:94,114,129` |

No code writes any `CardWorkflowRunStatus` or `CardWorkflowStageStatus` other than the initial
Queued/Pending: grep for `CardWorkflowRunStatus.(Running|Completed|Failed|Canceled|WaitingForHumanReview)`
and `CardWorkflowStageStatus.(Running|Completed|Failed|Skipped)` over `server/` finds only reads
(`CardService.cs:920-922`, `HomeTaskService.cs:336,345`).

### Every reader

- DTO projection: `BoardService.cs:338-340`, `AgentService.cs:1231-1233`, `CardService.cs:229`,
  `CardThreadService.cs:79`, `HomeTaskService.cs:78-80`; DTOs `BoardDtos.cs:62-64`,
  `AgentDtos.cs:286-287`, `HomeTaskDtos.cs:77`.
- Behaviour: `CardService.ArchiveAsync` refuses to archive while a run is non-terminal
  (`CardService.cs:916-927`); `HomeTaskService.ClassifyCard` groups `WaitingForHumanReview` under
  the human bucket and `Running` under working (`HomeTaskService.cs:330-346`).
- Client: `CardModal.tsx:413-416` and `CardRow.tsx:107-109` render `currentWorkflowStageName`
  when set (added 2026-08-13, CARD-0042 `ce48f504`); type unions in `boards.ts:12,71-73`,
  `agents.ts:124,320-321`, `homeTasks.ts:55`; 25 test fixtures set the three fields to null.
- The home rail already merges the two concepts on one axis (`HomeTaskDtos.cs:63-64`):
  "`Stage`: `ActiveWorkflowRun.CurrentStage.Name`, else the newest bound task's Role name".

### Database evidence (live `antiphon` DB, 2026-09-17)

```
CardWorkflowRuns                     1   (Status=0 Queued)
CardWorkflowStages                   2   (analyze-codebase, finalize-documentation; ai-agent / gpt-4o; both Pending)
Cards with ActiveWorkflowRunId       0   of 729
Cards with AssignedAgentId           0
WorkflowTemplates                    3   (Document Project, Full Feature Pipeline, Quick Change; BMAD group; seeded 2026-01-01)
Workflows / Stages (gen-1)           0 / 0
Agents with DefaultWorkflowTemplateId 3  (school-revision, markdown-package Orchestrator, slides Orchestrator)
```

The one run: CARD-0001, agent "Antiphon", template "Document Project", created 2026-08-01
16:32Z, never started; the card is Done and its `ActiveWorkflowRunId` has been nulled by dequeue.
The three agents carry a template only because the orchestrator preset sets
`DefaultWorkflowTemplateId: FullFeaturePipelineTemplateId` (`AgentPresets.cs:23,41`).

### Verdict on the fields

Reserved infrastructure for a designed-but-unbuilt engine, not dead code in the "unreferenced"
sense (17 server files, DTOs, client rendering, an archive guard and a home-rail classifier all
touch them) but functionally inert. Reusable: the per-card run + ordered stage rows + definition
snapshot + `CurrentStageId` pointer + the DTO/client/home-rail plumbing. Not reusable: the FK to
gen-1 `WorkflowTemplates`; `ExecutorType`/`ModelName`/`SystemPrompt` columns (gen-1 vocabulary);
the Restrict FK to a standing `Agent` (pipeline stages run on pool delegates); the factory's
agent-scoped default (`CardWorkflowRunFactory.cs:26-34`); `WorkflowDefinitionParser.ParseYamlDefinition`,
which requires `stages[].executorType` (`WorkflowDefinitionParser.cs:43-47`).

## 3. Priority 2: the live stage mechanism, re-verified

Unchanged since the CARD-0552 investigation: `git log 6bda48d3..HEAD` over `PipelineHandoff.cs`,
`AgentTaskEnums.cs`, `server/Bundles`, `InstructionBundles.cs`, `AgentTaskPipelineStatusService.cs`,
`AgentTaskDispatcher.cs`, `DelegationReportFormatter.cs` is empty, and `origin/master` has no
commits beyond this worktree's base. Everything in `docs/investigations/2026-09-17-card-0552-mutation-tracked-stage.md` §1
still holds. Additional facts this card needs:

Where the six-stage set is hardcoded (each is a place a data-driven stage set must replace or feed):

| Surface | File:line |
|---|---|
| `AgentTaskRoles.IsStage` + EF `Stage` expression | `AgentTaskEnums.cs:387-393`, `:398-404` (7 `IsStage(` call sites, 2 expression sites) |
| role -> bundle key switch `StageKeyFor`, called by `ForDelegate` | `InstructionBundles.cs:221-233`, `:184-219` |
| handoff vocabulary: alias map, `Token`, `TryToStageRole` | `PipelineHandoff.cs:24-45`, `:56-68`, `:75-100` |
| the literal `<investigate|plan|test-design|code|mutation|review|land|decide|none>` in every stage brief | `DelegationReportFormatter.cs:333-343` (`StageHandoffContract`) |
| stage glance columns = every non-specialist role in enum order | `AgentTaskPipelineStatusService.cs:35-38` (`VisibleRoles`) |
| per-role tier / WIP / timeouts | `DelegationSettings.cs:320-327` (`RolePolicy`, string-keyed config) |
| complexity routing order | `ComplexityRoutingService.cs:63-68` |
| internal-decision policy | `InternalDecisionPolicy.cs:43-48` |
| CLI ValidateSets | `scripts/delegate.ps1:23` (also routing-pin.ps1, complexity-chain.ps1 per CARD-0146 plan `:195`) |
| client role union and labels | `client/src/api/agentTasks.ts:14-47`, `pipelineStageModel.ts:61-79` (`STAGE_LABEL`) |
| tests pinning the catalog and role->key | `InstructionBundleTests.cs:137-142`, `:527`; plus `DelegateBundleLaunchTests`, `MutationDispatchTests`, `AgentTaskPipelineStatusTests` stage count |
| stage-wide routing pins, one per role, fleet-wide | `RoutingPin.cs:36-42` (no board/project column); 13 active, all stage-wide |

Blast radius of the role enum: `AgentTaskRole.` appears in 35 server files, 202 test files, 11
client files. CARD-0470's plan (`2026-09-09-card-0470-code-mutation-split-plan.md:16,26,32,56,99,109-116`)
is the recorded cost of adding one stage, including the D-8 self-hosted rollout dance.

**The stage order is not in code.** The server never computes "what follows X": it parses the
`next:` the delegate wrote (`PipelineHandoff.TryParse`), stores it on the task, and projects a
ready row. Sequencing lives only in prose: each bundle's `next:` line
(`grep "^next:" server/Bundles/stage-*.md` — six lines), `orchestrator.md:33`,
`docs/orchestration-loop.md:118-160` (the diagram), and the `antiphon-orchestrator` skill. A
"workflow" object that adds edges would be adding something that exists nowhere today, not
replacing a hardcoded one.

**A stage's "agent definition" is already split across three places**, and two of them are code:

1. `server/Bundles/stage-<role>.md` — standing rules, 552-2,525 bytes each, embedded resource,
   content-hash versioned (§5).
2. C# literals in `DelegationReportFormatter.cs`: the reporting contract and `StageHandoffContract`
   for every stage role (`:239-245`, `:333-343`), `VerificationProfileBlock` for Code/Review
   (`:257-282`), Shared-write commit lines for Plan/Docs/Code (`:224-228`).
3. Server-enforced role semantics that are not prompt text: Mutation-only `SourceLandingAdmission`
   and the Mutation land refusal (`AgentTaskLandService.cs:78-79`), `CommitOnSettleEligibility.cs:12`,
   `SharedWriterLeaseProjection.Participates(workspace, role)`, Code/Review verification rounds
   (CARD-0544), `RolePolicy` tiers, stage-wide pins.

So "decouple prompt content from role name" is cheap for (1), a refactor for (2), and not
possible for (3) without keeping a role-kind axis.

## 4. Priority 3: what "sharing across projects" would mean today

Hierarchy: `Project` (81 rows; owns `LocalRepositoryPath`, `DefaultLaunchEnvJson`, `CommitOnSettle`,
`ApiKeys`) -> `Board` (98, 48 unarchived; owns `WorkflowDefinitions`, `TrackerKind`, columns) ->
`Card` -> `AgentTask` (`ProjectId?`, `CardId?`; `AgentTask.cs:68,171`). Standing agents carry
`BoardId` (-> project), pool delegates `PoolProjectId` (`Agent.cs:136,181`). The Antiphon project
has 4 boards. Stage-role tasks exist on 5 boards (Antiphon 1,206; markdown-package 150; Gym Stat
148; school-revision 14; Torquay Leander 1), all on the same six roles: today "sharing" is total
and involuntary, and the pipeline glance is fleet-wide (no board filter in
`AgentTaskPipelineStatusService.BuildAsync`).

Prior art for scoped or shared configuration:

| Config | Scope | Storage | Versioned / pinned at use | Override chain |
|---|---|---|---|---|
| Instruction bundles | server-wide | embedded resources | SHA-256 hash stamp; `AgentSession.ComposedBundleStamp` drift badge | role map in code, then `AgentBundleAttachment` rows per agent (12 rows: 6 orchestrator, 6 board-api) |
| `Delegation:RolePolicy` | server-wide | appsettings | no | none |
| `RoutingPin` stage-wide | fleet-wide | DB | no | per-card pin overrides |
| `AgentPresets` | server-wide | code | no | request fields override |
| gen-1 `WorkflowTemplate` | global, grouped | DB, seeded | `UpdatedAt` only; run snapshots YAML | `Agent.DefaultWorkflowTemplateId` |
| gen-2 `BoardWorkflowDefinition` | per board, one active | DB versions + `<repo>/.antiphon/boards/<id>/WORKFLOW.md` mirror, hot reload (`WorkflowFileStore.cs:10-21`) | integer `Version`, `IsActive`; `RunAttempt.BoardWorkflowDefinitionId` pins | none |
| `ApiKey` | project, then global | DB (`ProjectId?`, `ApiKey.cs:27`) | no | project wins (`Agent.cs:124`) |
| launch env | project -> agent -> launch | DB JSON | no | documented merge order (`Agent.cs:120-130`) |
| `AgentTuiProfile` + `AgentTuiProfileRevision` | server-wide by `AgentKind`, `IsDefault` | DB | revision rows, `ActiveRevisionId`; `AgentSession.TuiProfileRevisionId` pins | agent picks profile |
| `antiphon.areas.json` | per repo | file in repo | git | none (`AreaMapLoader.cs:29`) |
| `Delegation:AllowedRoots` | server-wide security boundary | appsettings | | |

Two established shapes for "named, versioned definition referenced by id and pinned at use":
`AgentTuiProfile`/`Revision` (global, revisions, active pointer, session pins a revision) and
`BoardWorkflowDefinition` (per board, integer versions, active flag, attempt pins the id). Neither
is shareable across boards: the first is global-by-kind, the second is owned by one board. The only
"global definition referenced by many" object is gen-1 `WorkflowTemplate`, and it is referenced by
`Agent`, not by `Board`/`Project`.

Isolation rules a shared definition must respect (evidence): bundles are repo-agnostic by design
and per-project bundles were rejected in favour of the repo's own `AGENTS.md`
(`docs/superpowers/specs/2026-08-16-card-0058-0059-0060-instruction-bundles.md:55-59`, D4);
`AllowedRoots` bounds where any task may run; pins, policy, WIP slots and the glance are keyed by
`AgentTaskRole`, so a stage that is not a role has none of them; the pin-the-version-at-launch rule
(`RunAttempt.BoardWorkflowDefinitionId`, `AgentSession.TuiProfileRevisionId`, `CardWorkflowRun.WorkflowDefinitionSnapshot`,
001 plan gotcha 7 "existing attempts use snapshot of definition at launch, not live").

## 5. Priority 4: how stage bundles are loaded

Filesystem convention at build time, not at run time, and not DB-backed:

- `server/Antiphon.Server.csproj:25` embeds `Bundles/*.md` (README excluded; `Bundles/Presets/*.md`
  separately at `:28`, not attachable).
- `InstructionBundles.Load()` (`InstructionBundles.cs:238-262`) enumerates the assembly manifest;
  key = filename without `.md`; version = first 8 hex digits of SHA-256 of the LF-normalised text;
  rendered as `[bundle:<key> v<hash8>]` (`server/Bundles/README.md`).
- Selection: `ForDelegate(kind, role, attachedKeys)` (`:184-219`) returns
  `[StageKeyFor(role), delegate-basics]` for a stage role; `StageKeyFor` is a six-arm switch
  (`:221-233`) that throws for a non-stage role. Attachments come after role defaults and are
  deduped by the composer.
- Distribution: composed at every FRESH launch into `--append-system-prompt`
  (`AgentTaskDispatcher.cs:4108-4128`), budget-checked against `CommandLineBudgetChars` (30,000,
  `DelegationSettings.cs:304`); warm reuse keeps launch-time bundles, drift recomputed at `:4645`.
  A prompt edit reaches agents only after a server rebuild/restart (CARD-0470 D-8), and the weekly
  prompt-review loop edits bundles as Review cards, i.e. through git (`orchestration-loop.md` §10,
  README "output-distiller" section).
- The only bundle state in the DB is `AgentBundleAttachments` (key string, no FK; a renamed bundle
  leaves rows dropped with a warning, `AgentBundleAttachments.cs:60-70`).
- Tests: `InstructionBundleTests.cs:137-142` pins the exact key set; `:527` the role->key table;
  `stage_bundle_invariants_are_pinned_by_substring` pins sentences; `:460` the composed size.

Consequence: prompt content is already a named key at the catalog level; the coupling to the role
is one switch at selection and one embedded-resource convention at distribution. A workflow stage
carrying `bundleKey` (or a definition id) instead of deriving it from `Role` is a one-line change
at selection; the real decision is whether definitions stay in git (hash-versioned, PR-reviewed,
drift badge intact) or move to DB rows (editable live, but outside the §10 review lane and the
D4 rejection of DB-backed per-project bundles).

## 6. What exists, what is missing

Exists and works:

- Per-card run skeleton with definition snapshot, ordered stages, current-stage pointer, DTO
  fields, client rendering, home-rail classification, archive guard (§2).
- A per-board versioned definition with file mirror, hot reload, Monaco editor and version pinning
  on attempts (gen-2), currently carrying tracker config + spawn prompt + hooks, no stages.
- A global versioned-definition pattern with revision rows (`AgentTuiProfile`).
- Named, hash-versioned prompt bundles with a key catalog and per-agent attachment rows (§5).
- Handoff parsing onto `AgentTask.NextStage` and the ready-row projection, stage-wide pins,
  per-role policy, WIP slots (CARD-0146/0304/0305).

Missing, with evidence:

1. No definition object a board or project REFERENCES: `BoardWorkflowDefinition` is owned (BoardId
   FK, one active per board); gen-1 `WorkflowTemplate` is referenced only by `Agent`.
2. No server-side stage order or edges: `next:` is prose in six bundle files; the server only
   records what the delegate said (§3).
3. No link from a card's bound stage tasks to any definition or version: `AgentTask` has `Role`,
   `NextStage`, `NextHandoff` and no workflow/definition id; `CardWorkflowRun` is unused and points
   at gen-1.
4. No way to choose a bundle except by role (`StageKeyFor`), and stage-specific contract text also
   lives in C# (`DelegationReportFormatter`) (§3).
5. No per-board or per-project scoping of `RolePolicy`, stage-wide pins, WIP or the glance (§4).
6. Dead weight to repurpose or remove: gen-1 engine + UI (33 server files, 31 client files, 4
   migrations' tables, `Workflows` nav, settings `TemplateManager`); gen-3 run rows (17 server
   files). Both are load-bearing only through tests and the seeder.

## 7. Scope observations for Plan (not a design)

- The two asks are separable in mechanism and coupled in data: a workflow stage must name SOME
  instruction source, but that source can stay `stage-<role>.md` (today's bundle key) while the
  stage set becomes data, and bundles can become selectable by key while the stage set stays the
  enum. Neither forces the other.
- The cheapest honest reading of ask 1 keeps `AgentTaskRole` as the universe of stage KINDS
  (everything routing/policy/pin/glance/land-refusal is keyed by it, and CARD-0146 line 31 recorded
  "add roles; do not add a stage enum") and makes a workflow a named ordered subset with edges
  over those kinds, referenced by board/project and snapshotted onto the card's run. Free-form
  per-workflow stage names would re-key every surface in the §3 table by (workflow, stage).
- The dormant run rows fit the "snapshot on the card" half of that if re-pointed (drop the
  gen-1 FK and vocabulary columns, drop the standing-agent FK, link stages to bound tasks). The
  gen-1 engine and templates do not fit anything and are the only rows in the DB that no
  live path reads.
- Distribution of prompt content is the one decision with a recorded precedent against DB storage
  (D4, §10 review loop, hash-stamped drift badge); a definition that stores only REFERENCES to
  git-owned bundle keys keeps all three.
- Rollout is self-hosted (CARD-0470 D-8): the running server must recognise a new stage set before
  any delegate can emit it, so the vocabulary in `StageHandoffContract` and `PipelineHandoff` needs
  a compatibility story for in-flight tasks.

## Remaining uncertainties

- Whether any operator ever used the gen-1 `Workflows` UI or `/api/settings/templates` on another
  instance; on this DB the answer is no (0 Workflows) but the seeder re-asserts the built-ins on
  every boot, so "3 templates" is not evidence of use.
- Whether the 2026-08-01 CARD-0001 run was created by a UI click or an API probe; the agent
  ("Antiphon") and card are both long closed.
- Whether boards other than Antiphon have a `WORKFLOW.md` on disk that was never saved through the
  API (the loader would import it on first `GET /api/boards/{id}/workflow`); only Antiphon has
  rows.
- How much of the 202-test-file `AgentTaskRole.` footprint iterates the enum (must stay green
  unedited) versus names specific roles; CARD-0146's plan named three iterating files, not
  re-counted here.

## Not done, noted

Fix idea: a global `WorkflowDefinition` (name, version, ordered stage kinds over `AgentTaskRole`,
per-stage `bundleKey`, allowed `next:` edges) referenced from `Board` with a project default,
snapshotted into the existing `CardWorkflowRun`/`CardWorkflowStage` rows re-pointed away from gen-1,
and `StageKeyFor` replaced by the stage's key; gen-1 engine/templates/UI removed in the same card
or a follow-up.
