# CARD-0505 / CARD-0506 state investigation — 2026-10-13

CARD-0505 is functionally implemented, reviewed and published on master; CARD-0506 is ready for Plan. Formal post-land Mutation remains pending. This is a feature/state reconciliation, not a newly reproduced product bug.

Task: `012c1c9e-d489-47fa-9412-4bfc284a2b29`. Source examined: `/work/worktrees/task-012c1c9e`, baseline `00fa6b884a5f127982cff26a677aae74e6666650`, branch `feat/card-task-012c1c9e`. Only this investigation report is changed, as required by the Investigate reporting contract. No production/test edits, builds, test runs, deletions, card moves, settings writes, deployments or restarts. HTTP requests were GETs, with the inherited task-token header kept out of output. Historical results below are stored evidence, not tests run by this task.

## Confirmed state and mechanism

1. **CARD-0505's S1, S2 and S3 are on master.** The original implementation and repair commits were rebased during landing. The stored landing operation confirms publication to `refs/heads/master`, and Git confirms the published SHA is an ancestor of both the examined master baseline and the running server SHA (E2, E3). The settings API is registered in `server/Program.cs:1146`, the service in `:409`, and startup initialization in `:887`.
2. **The Review column was not proof of missing backend work.** CARD-0505 history revision 29 records its final Review succeeding on October 10; revision 30 moves it to InProgress because this investigation was dispatched (E1). Review → Done is expressly not automatic (`docs/agent-card-lifecycle.md:133`). The state shown in the brief was accurate before this task's own transition.
3. **CARD-0506's backend prerequisite is fulfilled.** Its card still says “Depends on CARD-0505 landing the backend knobs first,” and has no revisions (E1). The global/project routes and DTOs exist, including replacement and clear semantics (`server/Api/Endpoints/DispatchConcurrencyEndpoints.cs:15`, `server/Application/Dtos/DispatchConcurrencyDtos.cs:57`, `server/Application/Dtos/DispatchConcurrencyDtos.cs:99`). Both GETs work on the live server (E4).

### Landed slices and exact source footprint

| Slice | Original branch commit → master commit | Implemented files / source evidence |
|---|---|---|
| S1: store, inheritance, audited API | `04f062bde` → `b351695fc` | `server/Domain/Entities/DispatchConcurrencySettings.cs:7`; `server/Application/Services/DispatchConcurrencyPolicy.cs:41`; `server/Application/Services/DispatchConcurrencySettingsService.cs:47`; `server/Application/Dtos/DispatchConcurrencyDtos.cs:57`; `server/Api/Endpoints/DispatchConcurrencyEndpoints.cs:15`; `server/Migrations/20261010081006_AddDispatchConcurrencySettings.cs:14`; `server/Infrastructure/Data/AppDbContext.cs:2449`; `server/Program.cs:409`. Four new settings/policy/wire/migration test classes are listed in the plan at `docs/superpowers/plans/2026-09-30-card-0505-configurable-dispatch-concurrency-plan.md:929`. |
| S2: create gate and dispatch hold | `bfdfafb19` → `af6bc6b92` | `server/Application/Services/DelegationOpenGate.cs:74`; `server/Application/Services/AgentTaskService.cs:1458`; `server/Application/Services/AgentTaskDispatcher.cs:7767`; `server/Application/Services/DispatchConcurrencyPolicy.cs:122`; `server/Application/Exceptions/ConcurrencyLimitException.cs:103`; `server/Application/Settings/DelegationSettings.cs:43`. Admission/dispatch tests and regression scope are in the plan at `:930`. |
| S3: scoped pipeline, script, documentation | `5ff5a5350` → `be539dd6e` | `server/Application/Services/AgentTaskPipelineStatusService.cs:419`; `server/Application/Dtos/AgentTaskPipelineDtos.cs:57`; `scripts/dispatch-concurrency.ps1:126`; `docs/ops-http.md:177`; `docs/orchestration-loop.md:811`; `scripts/delegate.ps1` and `server/Bundles/orchestrator.md` are also in the commit's file list (E3). Pipeline/script tests and regression scope are in the plan at `:931`. |
| Claim-transaction probe repair | `76af2dc11` → `e2f6d3097` | Test-only repair and its two green CP-2 receipts are recorded in the plan at `:951`; production dispatch was unchanged (E5). |
| Corrected evidence prose | `9f0f63d4f`, `4554fed6a` → `7eb24357e`, `0327f0c89` | Results and corrected CP-3 census explanation in the plan at `:947` and `:953`. |
| Reviewed carrier/publication | `6268bda2b` → `a646a004d` | Empty carrier preserves reviewed tree; final Review `f159e9ed` and landing owner `1522ff9c` confirm the published SHA (E2). |

There is no remaining S1–S3 backend implementation slice established by this investigation. CARD-0749's residual runtime scope was folded into this one store, not a separate prerequisite (`docs/superpowers/plans/2026-09-30-card-0505-configurable-dispatch-concurrency-plan.md:100`; its current Done closure is in E1). CARD-0881 subsequently added runners, runner defaults and host remaining to the pipeline; its current Done closure names `bd775ef5d` (E1), and the live pipeline includes those settings (E4).

## Knobs, defaults and precedence

| Exact knob | Shipped seed/default and accepted values | Evidence |
|---|---|---|
| `overrides.mode` | `LegacyOpen`; explicit opt-in `SeparateQueues` | `DispatchConcurrencyPolicy.cs:51`, `:481` |
| `overrides.maxParallel` | Seeded from `Delegation:MaxOpenTasks`, code default **6**; required finite overall scope cap when supplied, **1–512** | `DelegationSettings.cs:46`; `DispatchConcurrencyPolicy.cs:54`, `:14`, `:200` |
| `overrides.maxQueued` | Seed **null**, no independent queued limit; **0–4096** or null. Zero refuses new queued admission. | `DispatchConcurrencyPolicy.cs:55`, `:16`, `:390`, `:510` |
| `overrides.roles.<Role>.maxParallel` | Seeded from `Delegation:RolePolicy:<Role>:RecommendedInFlight`; shipped Code/Review **2**, other configured ordinary roles **1**, Custom **null**. **1–512** or null. | `DelegationSettings.cs:348`, `:1134`, `:1148`; `DispatchConcurrencyPolicy.cs:46`, `:564` |
| `overrides.roles.<Role>.maxQueued` | Seed **null** for every ordinary role; **0–4096** or null | `DispatchConcurrencyPolicy.cs:48`, `:566` |
| `overrides.schemaVersion` | Optional structural field; only **1** accepted, emitted by canonical storage | `DispatchConcurrencyPolicy.cs:189`; `DispatchConcurrencySettings.cs:10` |

The short filenames in this table mean `server/Application/Services/DispatchConcurrencyPolicy.cs`, `server/Application/Settings/DelegationSettings.cs`, and `server/Domain/Entities/DispatchConcurrencySettings.cs`.

There are 14 supported ordinary roles: Custom, Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, Investigate, TestDesign, Mutation (E4; `server/Domain/Enums/AgentTaskEnums.cs:26`). Check/Distill/Diagnose are specialists and excluded (`server/Domain/Enums/AgentTaskEnums.cs:431`, `DispatchConcurrencyPolicy.cs:30`, `:543`).

Resolution is **explicit project field > explicit global field > immutable imported startup seed**, with sources `project`, `global`, `default` (`DispatchConcurrencyPolicy.cs:459`, `:471`). Startup imports `Delegation:MaxOpenTasks` and bound `Delegation:RolePolicy` once into global revision 1; subsequent initialization returns without importing again (`DispatchConcurrencySettingsService.cs:47`, `:69`). Config file/environment changes are therefore seed inputs for a missing store, not live overrides after import (`DelegationSettings.cs:43`, `:1130`). The live imported values differ from shipped code defaults: overall 6, Code/Review 5, Plan/Investigate 3, other configured roles 1, Custom null; every queued bound null (E4).

“Operator settings” is not a fourth automatic precedence tier. An operator can persist a Human global or project override through the API, following the field precedence above; Auto cannot replace a Human scope, or introduce a new project override over a Human global (`DispatchConcurrencySettingsService.cs:295`). Separately, host budgets and runner seat capacity constrain actual launch: local defaults to `Delegation:MaxConcurrentTasks` **2**, remote effective budget is the minimum of configured host budget and declared runner capacity (`server/Application/Services/HostBudgetService.cs:72`; `DelegationSettings.cs:36`). The operator's four-per-stage / six-on-server2 policy is orchestration guidance and does not silently mutate the store (`docs/orchestration-loop.md:800`, `:817`, `:821`). E4 shows server2 declared/effective 10 with no configured host budget.

Mode matters to the meaning of `maxParallel`: `LegacyOpen` applies it at creation to **Queued + Dispatched + Working**; `SeparateQueues` gates creation by queued bounds and dispatch by **Dispatched + Working**, holding excess accepted work Queued (`DispatchConcurrencyPolicy.cs:122`, `:153`, `:364`; `AgentTaskDispatcher.cs:7767`, `:7844`). These populations are per ProjectId across runners; null ProjectId is its own bucket, and specialists do not count (`DispatchConcurrencyPolicy.cs:101`). A legacy `ignoreConcurrencyLimit` bypass does not bypass queued limits or SeparateQueues dispatch bounds (`DispatchConcurrencyPolicy.cs:136`, `:144`, `:166`).

## Backend contract available to the UI now

| Route | Contract |
|---|---|
| `GET /api/dispatch-concurrency` | `DispatchConcurrencyGlobalDto`: immutable seed, raw overrides, effective/null-project policy, supported roles/ranges, revision/audit, occupancy (`server/Application/Dtos/DispatchConcurrencyDtos.cs:57`). |
| `PUT /api/dispatch-concurrency` | `PutDispatchConcurrencyRequest`: `expectedRevision`, `overrides`, `reason`, `provenance`; replaces the global override object (`DispatchConcurrencyDtos.cs:99`; `DispatchConcurrencySettingsService.cs:303`, `:330`). |
| `GET /api/projects/{projectId}/dispatch-concurrency` | `DispatchConcurrencyProjectDto`: raw overrides, inherited policy, effective policy, project/global revisions, ranges/roles, audit, occupancy (`DispatchConcurrencyDtos.cs:71`; `DispatchConcurrencySettingsService.cs:425`). |
| `PUT /api/projects/{projectId}/dispatch-concurrency` | Same request plus mandatory `expectedGlobalRevision`; unknown project is checked before writing (`DispatchConcurrencyEndpoints.cs:117`; `DispatchConcurrencySettingsService.cs:222`). |
| Either scope's `GET .../revisions` | `DispatchConcurrencyRevisionPageDto`; limit defaults 50, allowed 1–100, `beforeRevision` cursor (`DispatchConcurrencyEndpoints.cs:19`, `:27`, `:60`; `DispatchConcurrencyDtos.cs:86`). |

The short filenames here are the same server paths as the landed-slices table. All routes are mapped in `server/Api/Endpoints/DispatchConcurrencyEndpoints.cs:15`.

**Revert to default is implemented.** Send a replacement `overrides: {}` to the project PUT with the current project/global revisions. Omitted fields inherit; explicit null means unbounded for nullable bounds, not “remove override.” The persisted scope row/revision/history survive clear, avoiding stale-revision reuse (`server/Domain/Entities/DispatchConcurrencySettings.cs:4`; `DispatchConcurrencySettingsService.cs:329`; `DispatchConcurrencyPolicy.cs:471`). The concrete regression clears P at revision 1, retains revision 2, changes global Code 4 → 3, observes P now inherit Code 3/global, and rejects the old revision (`tests/Antiphon.Tests/Application/DispatchConcurrencySettingsTests.cs:124`). For a single field, omission from the complete replacement removes that field's override; all other desired explicit overrides must remain in that replacement (`DispatchConcurrencyPolicy.cs:471`; `DispatchConcurrencySettingsService.cs:330`).

Reason must trim to 1–400 characters; provenance is Human or Auto (`DispatchConcurrencySettingsService.cs:535`). Stale scope/global revisions return `409 dispatch_concurrency_revision_conflict`; Auto/Human conflicts return `409 dispatch_concurrency_human` (`DispatchConcurrencySettingsService.cs:289`, `:548`, `:558`). Caller task attribution is resolved server-side, not a writable body field (`DispatchConcurrencyEndpoints.cs:120`, `:147`). Writes publish `DispatchConcurrencyChanged` after commit (`DispatchConcurrencySettingsService.cs:369`, `:712`). The shell helper already has get/history/set/clear and sends a single replacement without retrying 409 (`scripts/dispatch-concurrency.ps1:6`, `:15`, `:109`, `:199`).

## CARD-0506 readiness and existing surfaces

**Yes, ready for Plan now; no missing backend prerequisite identified.** Existing UI surfaces, identified as candidate homes only:

- `/settings` routes to SettingsPage (`client/src/App.tsx:210`); its existing Projects and Routing panels are in `client/src/features/settings/SettingsPage.tsx:35`, `:50`, `:62`.
- Routing already displays model availability, usage, runner defaults and a role/complexity matrix (`client/src/features/settings/RoutingSettingsTab.tsx:68`).
- Projects already supplies per-project editing: `openEditModal(project)` at `client/src/features/settings/ProjectConfig.tsx:143`, its edit action at `:299`, and Edit Project modal at `:346`.

The requested concurrency UI is absent in the inspected client: `rg -n 'dispatch-concurrency|DispatchConcurrency|maxParallel|maxQueued' client/src` returned zero matches (exit 1). The existing global tabs contain no concurrency editor (`SettingsPage.tsx:11`); the project edit form has other settings but no concurrency API binding (`ProjectConfig.tsx:96`, `:200`). Backend change events exist, but `rg -n DispatchConcurrency client/src/hooks/useSignalRInvalidation.ts` also returned zero matches. These are CARD-0506's UI work, not prerequisites requiring another CARD-0505 backend slice. No UI design or implementation was performed.

## Stored verification, limits and recommended decisions

Stored final Review `f159e9ed` reports CP-1 **38/38**, CP-2 **94/94**, CP-3 **91/91**, zero failed/skipped; receipts bind `9f0f63d4f`, whose server/tests inputs were unchanged at carrier `6268bda2b`. Its structured Review evidence is Clean and source-clean (E2, E5). The plan preserves Code receipts and repaired CP-2 receipts (`docs/superpowers/plans/2026-09-30-card-0505-configurable-dispatch-concurrency-plan.md:933`, `:951`). CP-3's 91 exceeds the floor 77 because of later test growth; the corrected explanation is at `:947`. The earlier key-free probe red was repaired, not an outstanding production failure (E5; plan `:951`).

All **44 formal PCs remain pending** for method-scoped SourceLanding Mutation; the plan explicitly records that no formal campaign ran (`plan:966`). The one diagnostic PC-8f red claimed by the repair is not formal campaign evidence: Review notes its TRX was not retained (E5). This task independently ran **0 tests / 0 builds** and did not reopen or inspect those old TRX/build receipts; its conclusions use stored Review evidence and current read/source checks.

**CARD-0505:** recommend closing the implementation card as functionally shipped after the caller records/verifies the durable same-board companion obligation and explicit acceptance/deployment conditions, naming Code `1522ff9c`, carrier `55619eaf`, Review `f159e9ed`, landing operation `98089b3c-d8e7-48e9-96c1-3dd9a1daeccb`, published `a646a004d`, and pending 44 PCs. Do not claim PC-clean. Those are the closure requirements in `docs/agent-card-lifecycle.md:171`. No additional backend Code slice is identified. Companion linkage was not established by this investigation; the complete board search for CARD-0505 returned six cards but cannot exclude a companion linked without that text (E1).

**CARD-0506:** recommend Plan now against the existing GET/PUT/clear contract above; the backend landing dependency is satisfied (E2, E4). The card's requested global editing, project overrides and revert are still UI deliverables (E1; SettingsPage/ProjectConfig citations above).

**Not done, noted:** No fixes designed or implemented; possible separate follow-ups are status predicates for the population scans (`DelegationOpenGate.cs:200`, `AgentTaskDispatcher.cs:7785`), removal of unused PUT `options` (`DispatchConcurrencyEndpoints.cs:146`), and correction of stale “only in a 409” documentation (`docs/orchestration-loop.md:807`).

Remaining uncertainties: live PUT/clear/enforcement were not exercised by this read-only task; their mechanism is reconstructed from source, a concrete regression, and stored review/test results. Old generated receipts were not independently SHA-validated here. Current canonical desktop cleanliness is unknown: the historical landing event mentioned canonical-checkout dirt and an unlanded superseded plan sibling, while its current structured publication and cleanup both say Complete (E2). Formal Mutation and companion linkage remain distinct from backend availability. None of these prevents a UI Plan.

## Evidence record

### E1 — card records and history (2026-10-13, roughly 16:09–16:14 UTC)

Commands: `pwsh -NoProfile -File scripts/card.ps1 get CARD-0505 -Board Antiphon`, `history CARD-0505 -Board Antiphon`, and the same for CARD-0506; also get CARD-0749/CARD-0881 and `search 'CARD-0505' -Board Antiphon -All`. All exited 0.

Stored card rows: CARD-0505 ID `2a8d08c1-8b55-4e8e-9083-851ad1758c74`, status InProgress, revisions 30; CARD-0506 ID `ef759588-df6e-4b1d-ad3b-059a1183c860`, status Backlog, revisions 0, no history. Board ID `8988ca03-7414-47ad-b0b6-51556c701703`. CARD-0506 description retains “Depends on CARD-0505 landing the backend knobs first; sequence Plan/Code for this after that lands.”

Relevant unedited history lines:

```text
 30  Move         10/13/2026 16:09:53  Task 012c1c9e (Investigate, High) dispatched against this card.
     by card-transitions
 29  Move         10/10/2026 14:00:04  Task f159e9ed (Review) settled Succeeded; no other task is open against this card.
     by card-transitions
 28  Move         10/10/2026 13:54:02  Task f159e9ed (Review, Frontier) dispatched against this card.
 27  Move         10/10/2026 13:53:02  Task 55619eaf (Code) settled Succeeded; no other task is open against this card.
```

Other history-bound task IDs: Investigate `b9ca189f`; Plan `4529ab28`, `cc63c869`; TestDesign `1e0b8578`, `c34420cd`; Code `1522ff9c`, `4b4a88ba`, `f1e22bb1`, `7b235349`, `55619eaf`; Review `c7a82b84`, `be21cdfa`, `84193f60`, `5751b94b`, `f159e9ed`. The task-detail read shows `5751b94b-6236-4853-8e89-9d6f6cc8a9ea` Canceled, with no result or Review evidence; it is not the final Review.

The complete search returned six cards: CARD-0505 InProgress, CARD-0506 Backlog, CARD-0749 Done, CARD-0880 Done, CARD-0881 Done, CARD-1180 Done. CARD-0749 close reason explicitly retains runtime scope on CARD-0505 and says Mutation not run. CARD-0881 close reason names `bd775ef5d`, CP-1 78/78 and CP-2 99/99, Final Review clean, paused Mutation PC-1..PC-4.

### E2 — stored task/Review/landing rows

GET `/api/agent-tasks/1522ff9c` and `/api/agent-tasks/f159e9ed` succeeded. The Code summary is Succeeded, linked to CARD-0505. Selected structured values (not terminal-screen inference):

```text
landing.operationId = 98089b3c-d8e7-48e9-96c1-3dd9a1daeccb
landing.phase = Complete
landing.publication = Landed
landing.cleanup = Complete
landing.sourceSha = 6268bda2b380eefd89273ac9ccba00ee8e74f623
landing.verifiedSha = a646a004dd76ed8ffc2d444a4c41152c6f4e005d
landing.remoteSha = a646a004dd76ed8ffc2d444a4c41152c6f4e005d
landing.remoteConfirmedAt = 2026-10-10T14:05:31.084002Z
landing.destinationRef = refs/heads/master
landing.reviewEvidenceId = 86d7aa90-b6ed-4f14-9235-b6540320ab5f
landing.recoveryMode = AdoptReviewedSource
landing.recoverySourceTaskId = 55619eaf-94f0-4fe7-b4a0-ba2fb5d4ea65
landRequest.state = Completed
reviewEvidence.id = 86d7aa90-b6ed-4f14-9235-b6540320ab5f
reviewEvidence.subjectTaskId = 55619eaf-94f0-4fe7-b4a0-ba2fb5d4ea65
reviewEvidence.reviewedSourceSha = 6268bda2b380eefd89273ac9ccba00ee8e74f623
reviewEvidence.reviewedSourceRef = refs/heads/feat/card-task-55619eaf
reviewEvidence.outcome = Clean
reviewEvidence.reviewedSourceClean = true
```

Historical `LandedWithResidue` event detail confirms push exit 0 and cleanup Complete, but also records `canonical=canonical_checkout_dirty, unlanded-sibling=1e0b8578:feat/card-task-1e0b8578`. That warning is preserved; it does not contradict confirmed remote publication. No canonical cleanup action was attempted here.

### E3 — Git publication and retained branches

`git ls-remote origin refs/heads/master refs/heads/feat/card-task-012c1c9e` returned both at `00fa6b884a5f127982cff26a677aae74e6666650` before the report commit. Cached `origin/master` was `c6bbb1bb115c28b16529750c06424c89562056f6`, seven commits behind remote master/assigned baseline; it was an ancestor of HEAD. No checkout/rebase/reset/fetch was needed. The requested `git log origin/master --grep='CARD-0505' --grep='0505'` contained the master commits listed above. `git merge-base --is-ancestor a646a004d HEAD` and the same check against running SHA `7f8448798955a24427bc2279138bf9fb64bc4195` exited 0.

`git branch -a --list '*0505*'` returned only `origin/feat/card-task-0505ce7d`. Its tip `08d2d1117` and five branch-only subjects concern **CARD-0478 host/runner verification custody**, not CARD-0505. Matching a task-ID substring is not a feature branch classification.

Retained remote task branches from CARD-0505 history, obtained by `git for-each-ref`:

| Branch suffix (`origin/feat/card-task-…`) | Tip | Interpretation |
|---|---|---|
| b9ca189f | `e0959ec68f1a1fc12ede0842cdd2c5601db6282d` | Earlier September 27 plan only; superseded by September 29 then September 30 plans. |
| 4529ab28 | `a6a1daf2221289498a2e099297f6516bd1d76423` | Earlier September 29 plan, obsolete `/api/dispatch-policy` proposal; superseded. |
| 1e0b8578 | `78e1f98a4f38f056dbe98d87eba3742e08b261fa` | Verification design for that older plan; historical landing sibling warning refers to this branch. |
| cc63c869 | `c45c4ba27cf5a9ceab976179c2836a76238132b4` | September 30 current plan; rebased master commit `08b7b100c`. |
| c34420cd | `2863e62a945eec65bf7694b1cf59d496533778a5` | Current TestDesign; its fixture amendment is on master as `b39832470`; contents retained at plan `:539`. Other rebased docs are `fa3cf7da1`, `c2777d2d0`, `ca9cf3130`. |
| 1522ff9c, 55619eaf, f159e9ed | `6268bda2b380eefd89273ac9ccba00ee8e74f623` | Final reviewed carrier, published through landing owner 1522ff9c. |
| 4b4a88ba, be21cdfa | `9f0f63d4f3b48e0071adc242259b0c66d5870446` | Claim-xid test repair and receipts, included in carrier/publication. |
| f1e22bb1, 84193f60, 7b235349, 5751b94b | `4554fed6a692f8ccd582da52759e44883d5e776e` | Corrected docs/reviewed tree, included in carrier/publication. |
| c7a82b84 | `fd6b44b667f0ebe7d68893425b6b0ddc40577a89` | Initial Review baseline; later test repair supersedes its red. |

`git log HEAD..origin/feat/card-task-55619eaf` lists 16 divergent commit IDs because of landing rebase. **Every one** was marked `-` by `git cherry -v HEAD origin/feat/card-task-55619eaf`, including S1–S3, repair/docs and empty carrier: no unmatched implementation patch in that final chain. Retained divergent ancestry is not unlanded functionality. `git show --stat b351695fc af6bc6b92 be539dd6e` established the slice footprints. Earlier plan commit messages and `git show 2863e62a9` established the docs-only/superseded classification.

### E4 — live GETs / durable settings snapshot

At `2026-10-13T16:10:57.8682070Z`, GET `/api/version` returned version `7f8448798955a24427bc2279138bf9fb64bc4195`, capabilities `land-v2`, `operator-shutdown-v1`. This is not the current remote master SHA; Git confirms it includes CARD-0505 publication.

At `16:10:58.5676806Z`, GET `/api/dispatch-concurrency` returned:

```text
revision=1; provenance=Migration; callerTaskId=null
overrides={"schemaVersion":1}
seed.origin=startupConfiguration
seed.importedAt=2026-10-10T17:06:02.0806041Z
seed.mode=LegacyOpen; seed.maxParallel=6; seed.maxQueued=null
role maxParallel: Code=5, Review=5, Plan=3, Investigate=3, Custom=null;
  Commit/Coverage/Debug/Deploy/Docs/Merge/Mutation/Test/TestDesign=1
all role maxQueued=null; all effective field sources=default
```

GET `/api/dispatch-concurrency/revisions` returned one durable revision, revision 1, previousRevision null, scopeKey global, provenance Migration, the same seed snapshot and empty canonical overrides; `nextBeforeRevision=null`. No live runtime override history was present in that read.

GET `/api/projects/d4ea7ae9-e769-474b-95b9-aa25fbc1303f/dispatch-concurrency` at `16:10:58.7068087Z` and a compact re-read at `16:14:51.8984780Z` returned project revision 0, globalRevision 1, `overrides={"schemaVersion":1}`, inherited/effective LegacyOpen, maxParallel 6, maxQueued null, and exactly the above role values/default sources. Occupancy: open 6, parallel 6, queued 0, parallelRemaining 0.

GET `/api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` at `16:10:59.2289557Z` returned `taskScope=project`, a matching concurrency scope/policy/occupancy, and fleet host rows:

```text
local: inFlight=1 effectiveLimit=2 configured=null declared=null source=config remaining=1
server2: inFlight=8 effectiveLimit=10 configured=null declared=10 source=runner remaining=2
server2-temp: inFlight=0 effectiveLimit=null configured=null declared=null source=runner remaining=null
runnerDefaults: revision=2 globalRunnerId=server2 kindDefaults=[] lastProvenance=Human
```

These numbers are timestamped observations, not current dispatch authorization. No PUT or clear was issued.

### E5 — stored review/test provenance

GET `/api/agent-tasks/{id}` retrieved result text for Code 1522ff9c, Review c7a82b84, repair 4b4a88ba, Review be21cdfa, docs repair f1e22bb1, Review 84193f60, carriers 7b235349/55619eaf and final Review f159e9ed. Stored final Review cites:

| CP | Run | Source receipt SHA | Executed / passed / failed / skipped |
|---|---|---|---|
| CP-1 | `20261010-131526-3256` | `9f0f63d4f3b48e0071adc242259b0c66d5870446` | 38 / 38 / 0 / 0 |
| CP-2 | `20261010-130935-7924` | same | 94 / 94 / 0 / 0 |
| CP-3 | `20261010-130935-7924` | same | 91 / 91 / 0 / 0 |

Review records sourceState clean, buildSource verified, no unlisted tests; report/receipts originally lived under `/work/worktrees/task-be21cdfa/.antiphon/checkpoints`. Final Review used these receipts without re-running, explicitly because server/tests build inputs matched the carrier. Its result ends with `ordinaryScopeCompleted: Full`, `reviewedSourceClean: true`, next land, and 44 PCs pending.

Earlier c7a82b84 CP-2 was 93/94 on `fd6b44b66`, failing a PID-bound key-free probe after EF returned its connection. Repair binds `pg_current_xact_id()` to `pg_xact_status='committed'`; stored be21cdfa Review reports all three rows green on repair SHA. These are test-evidence repairs, not a remaining backend fault.

Essential unedited CHECKPOINT provenance already preserved in the tracked plan (`:958`, `:960`):

```text
CHECKPOINT CP-2 commit=76af2dc11dfb7110d61dd45c0dd1b7084719923a build=ok filter=/*/*/(DispatchConcurrencyAdmissionTests*)|(DispatchConcurrencyDispatchTests*)|(AgentTaskConcurrencyLimitTests*)|(AgentTaskDispatcherPredicateTests*)|(DispatchHoldVisibilityTests*)|(HostBudgetServiceTests*)/* executed=94 passed=94 failed=0 skipped=0 trx=/work/worktrees/task-4b4a88ba/.antiphon/checkpoints/20261010-125338-276f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=76af2dc11dfb7110d61dd45c0dd1b7084719923a sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=76af2dc11dfb7110d61dd45c0dd1b7084719923a build=ok filter=/*/*/(DispatchConcurrencyAdmissionTests*)|(DispatchConcurrencyDispatchTests*)|(AgentTaskConcurrencyLimitTests*)|(AgentTaskDispatcherPredicateTests*)|(DispatchHoldVisibilityTests*)|(HostBudgetServiceTests*)/* executed=94 passed=94 failed=0 skipped=0 trx=/work/worktrees/task-4b4a88ba/.antiphon/checkpoints/20261010-125708-865c/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=76af2dc11dfb7110d61dd45c0dd1b7084719923a sourceState=clean buildSource=verified
```

--- next stage ---
next: decide
handoff: CARD-0505 backend S1–S3 is reviewed, landed and live; close with durable companion/acceptance conditions and 44 formal PCs pending. CARD-0506 is ready for Plan against existing revisioned global/project GET/PUT and empty-override clear semantics; no backend slice remains identified.
artifact: docs/investigations/2026-10-13-card-0505-configurable-dispatch-concurrency-parallel-queued-limits-per-stage-project.md
