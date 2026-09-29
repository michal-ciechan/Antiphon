# CARD-0505: configurable dispatch concurrency (parallel + queued limits per stage/project)

Date: 2026-09-29. Stage: Plan; verification design is a separate TestDesign dispatch, which
refines the draft roster and checkpoint table at the end of this plan before Code.
Source inspected: `d2fb7536eb6385822b5d306fc49157effa6de35c` (= `origin/master` at 18:26Z).
Plan task: `4529ab28-69c8-4049-8f79-fcdc60072acf` (Frontier, Worktree, server2 lane).

Supersedes the unlanded Investigate-stage plan `docs/superpowers/plans/2026-09-27-card-0505-plan.md`
(task `b9ca189f`, commit `e0959ec68` on `origin/feat/card-task-b9ca189f`, never landed). Its
verified findings are carried into the ground-truth table below; its open question (whether a
separate queued-only limit is required) is decided here as D-3. Do not land that branch.

## Outcome and scope

Make every dispatch-concurrency limit a live, persisted, revisioned setting that an operator or
an orchestrator changes over the API and that the create gate, the dispatcher, the 409 detail
and the pipeline projection all read on their next operation, with no config edit and no
restart. The limits are:

1. **Per project, absolute**: `maxOpenTasks`, the CARD-0147 open-task cap (Queued + Dispatched +
   Working, non-specialist, per `AgentTask.ProjectId`; null project is its own bucket).
2. **Per role (stage or helper), per project**: `maxInFlight`, how many tasks of that role may be
   Dispatched/Working at once, and `maxQueued`, how many more may wait in Queued behind a full
   stage before a new create is refused. Today's single per-role `RecommendedInFlight` becomes
   `maxInFlight`; `maxQueued` is the new knob the card asks for.
3. **Per project override** of any of the above with an explicit revert to the global value, so
   CARD-0506's UI has a backend to sit on.

Per-host budgets (`local` and each phone-home runner) are already live and revisioned
(CARD-0654, `PUT /api/hosts/{hostId}/budget`). This plan leaves them as they are and states how
they compose with the new limits (D-6). The runner's own declared capacity (CARD-0654 D-3) is
also untouched.

Out of scope: the settings UI (CARD-0506); a project setting for the desktop host budget
(CARD-0654 Round 2); the projection defect where pipeline status counts runner-bound rows
against the desktop cap (CARD-0755, separate Backlog card; D-7 must not widen into it); any
change to model routing, routing pins, runner defaults or placement.

## Ground truth

Read from the inspected SHA, the live server (`GET /api/version` =
`d2fb7536e`, `/api/agent-tasks/pipeline`, `/api/hosts`, `/api/runner-defaults`,
`/api/session-runners` at 18:26Z) and the board (CARD-0505 revisions 1-3, CARD-0749 revisions
1-16 with its Review reports, CARD-0506, CARD-0755).

| Card assumption or brief question | What the code and the live fleet do | Consequence |
|---|---|---|
| The gate is fixed at absolute 3 per project and 1 per role. | Stale (CARD-0749 changed the defaults). `DelegationSettings.MaxOpenTasks` defaults to 6 and `RolePolicy[...].RecommendedInFlight` to 2 for Code and Review and 1 for every other named role (`server/Application/Settings/DelegationSettings.cs:35,336-355`). `Custom` and `Check` have no entry, so no per-role cap. | Seed the runtime rows from these effective values; nothing else about the numbers is preserved as code. |
| The pipeline reports `maxConcurrentTasks` 2 while the bundle says Code at two and the endpoint recommends five: which is authoritative? | Three different numbers describe three different things. (a) `maxConcurrentTasks` = the **local desktop host budget**: process-spawning tasks with an empty `RunnerId`, `HostBudgetService.Limit("local")` = budget row or `Delegation:MaxConcurrentTasks` (`HostBudgetService.cs:70-80`); live `/api/hosts` shows `local` effective 2, source `config`, and `server2` effective 10 with 6 in flight. Runner defaults route all work to server2 (`/api/runner-defaults` revision 2), so 2 is nearly irrelevant to throughput. (b) `recommendedInFlight` Plan 3 / Code 5 / Review 5 = the **per-role create-time cap** from the desktop's user-secrets (`Delegation:RolePolicy:<Role>:RecommendedInFlight`, per CARD-0749 review b1b80c61: "desktop user-secrets ... still override the code defaults"); enforced as 409 `concurrency_limit` axis `role` (`DelegationOpenGate.cs:47-54,132`). (c) "Code and Review at two" in `server/Bundles/orchestrator.md:73-80`, `docs/orchestration-loop.md:296-341,478-491`, `AGENTS.md:68`, `.claude/skills/antiphon-orchestrator/SKILL.md:18-29` = **prose** written when (b) defaulted to 2/2/1. | **The server's live per-role limit is authoritative** (D-1). The bundle and docs stop stating numbers and tell the orchestrator to read each stage's limits off the pipeline snapshot. The desktop host budget is a separate, already-live control and is renamed nowhere. |
| Limits need a new mechanism to become editable without a redeploy. | Two precedents exist. `RunnerDefaultSettingsService` (`server/Application/Services/RunnerDefaultSettingsService.cs`): singleton row + `RunnerRoutingRevisions` jsonb history, `PUT` with `expectedRevision`/`reason`/`provenance`, one-time import of the legacy key into revision 1, `RunnerDefaultsChanged` event. `HostBudgetService`: one row per host, `Revision++`, `AgentIncidentKind.HostBudgetChanged` audit, read once per tick. `DelegationSettings` is captured from `IOptions<>.Value` in fields (`DelegationOpenGate.cs:33`, `AgentTaskDispatcher.cs:122`, `AgentTaskPipelineStatusService.cs:55`), so options reload would not reach any consumer. | Follow the runner-defaults shape for the global snapshot and the host-budget shape for audit (D-2, D-4). |
| The create gate is where refusal happens. | `DelegationOpenGate.EnsureCanCreateAsync` takes `pg_advisory_xact_lock` inside the create transaction, counts open non-specialists in the project, and throws with `axis` absolute-first (`DelegationOpenGate.cs:63-94`). Live follow-ups and specialists bypass it (`AgentTaskService.cs:1425-1427`). `ignoreConcurrencyLimit` bypasses and writes a Warning event (`AgentTaskService.cs:1555-1569`). | Keep the lock, the axis precedence, the bypass and the warning; change what is counted and where the limits come from (D-3, D-5). |
| The dispatcher enforces per-role limits. | It does not. The tick enforces only host budgets (`AgentTaskDispatcher.cs:409-417,553-570`, `RemoteHoldForAsync`), scope leases, pins and model holds. A queued task of any role dispatches as soon as its host has a seat. | A "parallel" limit that survives a burst of creates needs a dispatch-time role hold (D-3). |
| Pipeline status reads the limits. | `AgentTaskPipelineStatusService` reads `RecommendedInFlightFor(role)` from startup settings (`:190`) and local limit from `HostBudgetService` (`:161`); queued rows carry `queueReason` in the precedence lease, sibling land, pin, host budget, concurrency cap (`:374-450`). | Read the live policy instead; add a `roleCap` reason (D-7). |
| Per-project overrides need a home. | `Project` has nullable per-project switches that inherit a `Delegation:*` default (`CommitOnSettle`, `DefaultWorkerWorkspace`, `server/Domain/Entities/Project.cs:53-59`). `ProjectSetupDtos.DelegationSummaryDto.MaxConcurrentTasks` is a catalogue read of the desktop cap (`ProjectSetupService.cs:217`), not an override; CARD-0749's suggestion that it is one is stale. | Per-project overrides are rows in the new policy table keyed by project, not `Project` columns (D-2). |
| A queued-only limit may be needed. | `MaxOpenTasks` and `RecommendedInFlight` both count Queued rows today, so queued work already consumes the caps; there is no separate waiting-line limit. The orchestrator's "depth of two" (in flight + queued + ready) is applied by hand. | D-3 splits the per-role cap into in-flight and queued. |
| CARD-0749 already delivered this. | CARD-0749 is in Review with its last Code task landed (`b2059dec`, Landed 2026-09-26 20:15Z). Its landed work is the default change to 6/2/2/1 plus documentation and bundle repairs (`2197f3d8e`, `7d6e3fe92`, and the guidance commits). No runtime endpoint, migration or live reader exists on master for these limits (grep for `delegation-concurrency|dispatch-policy|concurrency-limits` under `server/Api/Endpoints` finds nothing). | CARD-0505 is the backend for CARD-0749's stated requirement ("purely application-runtime driven and overridable over the API"). CARD-0749 can close on its landed defaults; cross-link both. |
| The orchestrator's phrasing is pinned. | `StandingPipelinePolicyDocumentationTests` pins "never two tasks in the same stage", "depth of two", "-IgnoreConcurrencyLimit" and "axis" in all four copies; `AgentTaskServiceIntegrationTests.code_and_review_recommend_two_in_flight` and `AgentTaskPipelineStatusTests:993-994` pin the seed defaults; `TaskPlatformGuidanceTests` caps stage bundles at 2,480 trimmed characters (orchestrator.md is a preset prompt, not a capped stage bundle). | D-8 rewrites the numbers into "read the live limit" while keeping every pinned phrase; the default pins stay true because the seed defaults do not change. |
| The pipeline contract fixture is in git. | `ContractSnapshotTests.SnapshotPipelineAsync` captures `pipeline.json` on first run and compares thereafter; no `pipeline.json` is tracked (the desktop's local copy is already stale per CARD-0749 review b1b80c61). | Additive DTO fields are safe for the client (`usePipeline` reads a typed DTO; unknown fields are ignored) and need the desktop fixture re-captured, which is the reviewer's ordinary step. |

Fleet numbers observed at 18:26Z, for the reader who tunes the seed:

| Reading | Value |
|---|---|
| `local` host budget | effective 2 (source `config`), 1 in flight |
| `server2` host budget | effective 10 (declared 10), 6 in flight |
| Plan / Code / Review `recommendedInFlight` (user-secrets) | 3 / 5 / 5 |
| Code defaults in `DelegationSettings` | Plan 1, Code 2, Review 2, others 1, `MaxOpenTasks` 6 |

## Decisions

Stated defaults for what the card left open are D-n; the plan is written under them and none
needs an answer before Code. Where a decision changes an orchestrator-visible contract it says so.

### D-1: the server's live limit is the authority; prose stops carrying numbers

Enforcement lives in one place: the persisted dispatch policy (D-2). The pipeline snapshot
projects that policy per stage, so `stages[].recommendedInFlight` (kept, now the live
`maxInFlight`) plus new `maxQueued`, `inFlightCount`, `queuedCount` and `policySource` are what
an orchestrator reads before dispatching. The bundle, `docs/orchestration-loop.md` §1, `AGENTS.md`
and the orchestrator skill keep their rules (parallel across stages, never two in a stage whose
cap is one, depth of two for Code, `-IgnoreConcurrencyLimit` only for `axis: absolute`) but say
"the stage's limit on the pipeline snapshot" where they say "two" today, and record 2/2/1 as the
seed defaults, not the rule.

Rejected: making the bundle's numbers authoritative and pushing them into config (that is the
redeploy the card wants to remove); keeping both and documenting the mismatch (an orchestrator
already reads the pipeline endpoint on every dispatch, so one source is cheaper than a rule about
two).

### D-2: one persisted, revisioned `DispatchPolicy` snapshot with per-project overrides

Entities and tables (EF migration `AddDispatchPolicy` generated with the repo's dotnet-ef
manifest, CARD-0677):

- `DispatchPolicySettings` (table `DispatchPolicySettings`): `Id` (`fleet`, singleton, ≤32),
  `Revision` (long, concurrency token), `UpdatedAt`, `LastReason` (≤400), `LastProvenance`
  (≤32), `LastCallerTaskId`.
- `DispatchPolicyLimit` (table `DispatchPolicyLimits`): `Id`, `SettingsId`, `ProjectId` (Guid?,
  null = global), `Role` (`AgentTaskRole?`, null = the project-level absolute row),
  `MaxOpenTasks` (int?, only on the `Role == null` row), `MaxInFlight` (int?), `MaxQueued`
  (int?). Unique index `(SettingsId, ProjectId, Role)`. A row that exists for a project is an
  override; a missing project row inherits the global row; a missing global role row means the
  role is unbounded (today's `Custom`/`Check`).
- `DispatchPolicyRevision` (table `DispatchPolicyRevisions`): `Revision`, `PreviousRevision`,
  `SnapshotJson` (jsonb, the complete canonical snapshot), `CreatedAt`, `Reason`, `Provenance`,
  `CallerTaskId`; unique `(SettingsId, Revision)`.

Import once (revision 1, provenance `Migration`, reason "Imported Delegation:MaxOpenTasks and
RolePolicy RecommendedInFlight.") from the effective `IOptions<DelegationSettings>` the server
booted with, so a desktop whose user-secrets say Code 5 keeps 5. `MaxQueued` imports as null
(unbounded) for every role, which reproduces today's behaviour exactly under D-3's rule. After
the row exists the `Delegation:MaxOpenTasks` and `RecommendedInFlight` keys are import-only,
as `Delegation:DefaultRunnerId` is for runner defaults (CARD-0710 D-11); the startup validator
keeps validating them because they remain the seed. Concurrent initializers publish one
revision 1 (the unique index plus the `DbUpdateException` re-read, as `EnsureInitializedAsync`
does).

Rejected: columns on `Project` (no revision history, no global row, no reason); a JSON blob in
one column (no per-row uniqueness, no SQL-visible override); `IOptionsMonitor` reload (no API,
and every consumer captures `.Value`); reusing `HostBudgets` (a host is not a stage).

### D-3: two per-role numbers with distinct enforcement points

Semantics per (project, role), counted over non-specialist tasks in that project:

| Knob | Counts | Enforced where | When it bites |
|---|---|---|---|
| `maxOpenTasks` (project row) | Queued + Dispatched + Working, all roles | create gate, axis `absolute` | unchanged from CARD-0147 |
| `maxInFlight` (role row) | Dispatched + Working in that role, on any host | dispatcher tick: hold with reason `roleCap` (D-7); create gate as the first half of the role axis | a stage cannot exceed its parallel limit even after a burst of creates or an `ignoreConcurrencyLimit` create |
| `maxQueued` (role row) | Queued in that role | create gate, axis `role`, only when in-flight is also at `maxInFlight` | the waiting line behind a full stage is bounded; null = unbounded |

Create refuses on the role axis when `inFlight(role) >= maxInFlight && queued(role) >= maxQueued`
(a null `maxQueued` never refuses on the role axis; a null `maxInFlight` never refuses and never
holds). Absolute still wins the report when both are exceeded. With the imported values
(`maxQueued` null) a third Code create at two Code rows is therefore **accepted and queued**, and
the tick holds it on `roleCap` until one of the two finishes; today it is refused. That is the
card's "parallel + queued" model and the only behaviour change an orchestrator will notice
before it edits the policy. The 409 detail gains `inFlight`, `queued`, `maxInFlight`,
`maxQueued` on the role axis so the coda can say "2/2 running, 1/1 waiting".

`ignoreConcurrencyLimit` keeps its meaning: bypass the create-time refusal for this one task
and write the Warning event. It does **not** exempt the task from the dispatch-time `roleCap`
hold, because the hold is what makes the parallel limit a guarantee; the Warning event text says
so ("queued past the cap; it dispatches when the role has a free slot"). The orchestrator
bundle's rule 7 is unchanged in shape (absolute axis only, never a same-stage occupant).

Rejected: a single "open" cap made editable (does not give the card's parallel/queued split, and
leaves the orchestrator counting depth by hand); a queued-only refusal that ignores in-flight
(refuses a create whose task would dispatch on the next tick); a dispatch-time `maxQueued` (a
queue limit that holds is a contradiction; it must refuse or it is not a limit).

### D-4: one service, read live, no cache; PUT with expectedRevision, reason, provenance

`DispatchPolicyService` (scoped, registered next to `HostBudgetService` in `server/Program.cs`):

- `EnsureInitializedAsync`, `ReadSnapshotAsync` (the canonical `DispatchPolicySnapshot`:
  revision, global row, role rows, project rows), `EffectiveAsync(projectId, role)` returning
  `EffectiveRoleLimit(MaxOpenTasks, MaxInFlight, MaxQueued, Source)` with `Source` one of
  `projectOverride`, `global`, `default` (no row at all, e.g. `Custom`).
- `GetAsync` → `DispatchPolicyDto`; `PutAsync(PutDispatchPolicyRequest, callerTaskId)` requires
  `expectedRevision` (409 `dispatch_policy_revision_conflict`), a 1-400 character `reason`,
  `provenance` `Human` (public PUT accepts only `Human`; `Auto` is reserved as runner-defaults
  reserves it, 409 `dispatch_policy_human` if a later Auto writer meets a Human row), and a
  **complete** snapshot: `maxOpenTasks`, `roles[]` (`role`, `maxInFlight`, `maxQueued`) and
  `projects[]` (`projectId`, optional `maxOpenTasks`, `roles[]`). Missing required top-level
  fields are 422 (a `JsonDocument` presence check like `RunnerDefaultsPut.ReadAsync`), unknown
  or specialist roles are 422, unknown project ids are 422, duplicate keys are 422, ranges are
  `maxOpenTasks` 1-512, `maxInFlight` 1-512 or null, `maxQueued` 0-512 or null. Zero is legal
  only for `maxQueued`. An identical snapshot is a no-op that adds no revision.
- `RevisionsAsync(beforeRevision, limit)` with the runner-defaults cursor shape.
- Every accepted write also adds an `AgentIncident` of new kind `DispatchPolicyChanged = 80`
  (Info) whose message lists each changed row `old -> new` and the reason, so the attention
  feed and the incident list show it without a new sink (AGENTS.md: a decision belongs on the
  feed).
- After commit, `IEventBus.PublishToAllAsync("DispatchPolicyChanged", { revision })`; a lost
  event is recovered by refetch. The client invalidation map gains that event →
  `['dispatchPolicy'], ['agentTasks','pipeline']` (one entry in
  `client/src/hooks/useSignalRInvalidation.ts`; the panel itself is CARD-0506).

Readers take one coherent snapshot per operation: the create gate reads inside its advisory
lock, the tick reads once at the start of the tick, the pipeline projection reads once per GET.
No process-local cache, so a PUT is visible to the next create, tick and read.

Rejected: PUT per row (`PUT /api/dispatch-policy/roles/Code`) as the primary write (partial
snapshots make `expectedRevision` meaningless and reintroduce absent-vs-null ambiguity); a
`RequireOperator` token (the write is reversible and audited, as host budgets and runner
defaults are).

### D-5: the create gate consumes the effective limits and reports them

`DelegationOpenGate.EnsureCanCreateAsync` takes the lock, then calls
`DispatchPolicyService.EffectiveAsync(projectId, role)` before counting, and its `Snapshot`
carries `AbsoluteLimit`, `MaxInFlight`, `MaxQueued`, `InFlightCount`, `QueuedCount` with
`RoleExceeded` = D-3's rule. `ToProblem` and `ConcurrencyLimitException.FormatDetail` emit the
role-axis sentence "N Code running (limit A) and M waiting (limit B) in project X" and the
extension gains `inFlight`, `queued`, `maxInFlight`, `maxQueued` (additive; `count`/`limit`
keep their absolute-axis meaning and on the role axis `count` = in-flight, `limit` =
`maxInFlight`, so `delegate.ps1`'s 409 printing keeps working unchanged).
`FormatOverrideWarning` adds the queued half. `DelegationOpenGate` keeps `IOptions<DelegationSettings>`
only for the `DispatchPolicyService` seed; no limit is read from it after initialization.

### D-6: composition with host budgets and runner capacity

Order of gates for one task: create-time absolute → create-time role (D-3) → dispatcher tick:
scope lease → sibling land → routing pin → **role in-flight hold (new)** → host budget / runner
capacity. The role hold is checked before the host gates so a task held on its stage never
consumes a host seat and never shows `hostBudget` when the stage is the real reason. Host
budgets, runner declared capacity and `local`'s `Delegation:MaxConcurrentTasks` fallback are
unchanged; `maxConcurrentTasks` on the pipeline stays the local host budget and is documented
as such next to the new fields.

### D-7: dispatcher role hold and pipeline projection

Dispatcher (`AgentTaskDispatcher.TickAsync`): after the local-cap skip and before the scope
lease decision, for a non-specialist queued task read `EffectiveAsync(task.ProjectId,
task.Role)`; count Dispatched/Working non-specialist rows with the same project and role
(one grouped query per tick, plus this tick's own dispatches by (project, role)); when
`maxInFlight` is set and the count is at or above it, `skippedRoleCap++`, add `HoldKind.RoleCap`,
trace `DispatchHoldDetails.RoleCap(role, inFlight, maxInFlight, projectShort)` =
`Held: stage 'Code' at its in-flight limit 2/2 in project d4ea7ae9; waiting for a Code slot.`
and `continue`. `DispatchHoldDetails.ClassOf` maps the `Held: stage '` prefix to
`DispatchHoldClass.Cap`; `Classify` treats it as an ordinary wait (CARD-0650 must not alarm on
it). `TickResult` gains `SkippedRoleCap`. Lowering a limit below current occupancy holds new
work only; nothing Dispatched/Working is touched (same rule as CARD-0654 D-5). A retained
capacity-wait return is not re-gated on the role limit (it already owns its process).

Pipeline (`AgentTaskPipelineStatusService`): per stage, `recommendedInFlight` = live
`maxInFlight`, plus `maxQueued`, `queuedCount`, `policySource` (`global`/`default`; the
fleet-wide snapshot has no project, so per-project overrides are shown on
`GET /api/dispatch-policy` and in a new per-stage `projectOverrides: [{projectId, maxInFlight,
maxQueued}]` list only when any exist). `atOrAboveRecommendation` keeps its meaning against the
live number. Queued rows gain reason `roleCap` in the precedence lease → sibling land → pin →
**roleCap** → host budget → concurrency cap, computed from the same counts the tick uses. The
top-level DTO gains `dispatchPolicyRevision`. `docs/antiphon-api.md:432` lists the new reason and
fields.

### D-8: API surface, scripts and documentation

| Route | Body / answer | Rules |
|---|---|---|
| `GET /api/dispatch-policy` | `DispatchPolicyDto`: `revision`, `maxOpenTasks`, `roles[] {role, maxInFlight, maxQueued, source}`, `projects[] {projectId, projectName, maxOpenTasks, roles[]}`, `updatedAt`, `lastReason`, `lastProvenance`, `lastCallerTaskId`, `seed {maxOpenTasks, roles[]}` (the values config would have given, for the UI's "revert to default") | Read-only; initializes revision 1 on first read. |
| `PUT /api/dispatch-policy` | `{expectedRevision, maxOpenTasks, roles[], projects[], reason, provenance}` → `DispatchPolicyDto` | D-4 validation; 409 `dispatch_policy_revision_conflict`; a project entry with no fields is 422 (revert by omitting the project). |
| `GET /api/dispatch-policy/revisions?beforeRevision=&limit=` | page of `{revision, previousRevision, snapshot, createdAt, reason, provenance, callerTaskId}` | Cursor as runner defaults. |
| `GET /api/projects/{id}/dispatch-policy` | `{projectId, effective {maxOpenTasks, roles[] {role, maxInFlight, maxQueued, source}}, override {...} \| null, revision}` | Convenience read for CARD-0506's per-project page; 404 unknown project. No PUT here in this card (one complete-snapshot writer, D-4). |

Caller identity on PUT resolves through `AgentTaskEndpoints.ResolvePollingCallerAsync` as
runner defaults do, so an orchestrator's edit records its task id.

`scripts/dispatch-policy.ps1` (`get`, `set-role -Role Code -MaxInFlight 3 -MaxQueued 1 -Reason
...`, `set-project -Project <id|name> ...`, `revert-project`, `history`) wraps GET → edit →
PUT with the fresh revision, mirroring `scripts/routing-pin.ps1`'s shape and ASCII-only rule,
so an orchestrator has a one-line lever. `delegate.ps1`'s `-IgnoreConcurrencyLimit` comment
drops the hard-coded "default 6 absolute; Code and Review 2" and points at the endpoint.

Docs: `docs/ops-http.md` gains a "Dispatch policy (CARD-0505)" row next to host budgets and
runner defaults with a GET/PUT example; `docs/antiphon-api.md` gains the four routes and the
pipeline fields; `docs/orchestration-loop.md` §1 rule 1, rule 4, rule 7 and the "WIP defaults"
paragraph (`:478-491`) say "the stage's `recommendedInFlight`/`maxQueued` on the pipeline
snapshot (live, `GET /api/dispatch-policy`)" and record 2/2/1 and 6 as the seed; the same edit
lands in `server/Bundles/orchestrator.md:73-84`, `AGENTS.md:68` and the orchestrator skill,
keeping every phrase `StandingPipelinePolicyDocumentationTests` pins. The bundle stays under
its current length (it is a preset prompt, not a capped stage bundle; keep it no longer than
today all the same).

### D-9: what does not change

No change to `Delegation:MaxConcurrentTasks`, host budgets, runner capacity push, model
availability, routing pins, complexity chains, runner defaults, specialist roles (Check, Distill,
Diagnose stay outside every gate), live follow-ups (still bypass the create gate), the
`pg_advisory_xact_lock` key, the shared-checkout lease, or CARD-0755's projection count. The
seed defaults in `DelegationSettings` keep their values and their pins.

## Slices

Each slice is one commit group on this card's branch; each names its files and the tests it
adds or changes. TestDesign turns the V/R list below into red-first tests and may split or
merge rows, but keeps the closed checkpoint list parseable.

**S1 — policy store, service, migration, DI.**
`server/Domain/Entities/DispatchPolicy.cs` (three entities), `AppDbContext` DbSets and model
config, `server/Migrations/<stamp>_AddDispatchPolicy.cs` (+ Designer, snapshot),
`server/Application/Services/DispatchPolicyService.cs`,
`server/Application/Dtos/DispatchPolicyDtos.cs`, `AgentIncidentKind.DispatchPolicyChanged = 80`,
`DispatchPolicyProblems` codes, `server/Program.cs` registration. Tests: new
`tests/Antiphon.Tests/Application/DispatchPolicyServiceTests.cs` (import-once from configured
values incl. user-secrets-shaped overrides; concurrent initializers; PUT round trip after a fresh
context; stale revision 409; validation leaves state unchanged; no-op adds no revision; revert
by omission; incident row and event published).

**S2 — create gate on live limits (D-3, D-5).**
`DelegationOpenGate.cs`, `ConcurrencyLimitException.cs` (+ DTO fields), `AgentTaskService.cs`
override warning text. Tests: extend `AgentTaskConcurrencyLimitTests` (live raise then create
succeeds without restart; lower then create refuses; third Code at two in flight with
`maxQueued` null is accepted; with `maxQueued` 0 is refused with the new counts; absolute wins
when both exceeded; `ignoreConcurrencyLimit` still queues and warns; null project bucket and
per-project override each honoured).

**S3 — dispatcher role hold (D-7).**
`AgentTaskDispatcher.cs` (`HoldKind.RoleCap`, `SkippedRoleCap`, grouped count),
`DispatchHoldDetails.cs` (`RoleCap`, `ClassOf`, `Classify`). Tests: extend
`DispatchHoldVisibilityTests` (third Code held with the `Held: stage 'Code'` detail while two
run; released on the next tick when one settles; lowering below occupancy leaves Working rows
and sessions untouched; role hold precedes host budget in the trace; `Custom` never held; a
per-project override holds only that project). `DispatchHoldDetails` unit cases for the new
sentence's class.

**S4 — pipeline projection and endpoints (D-7, D-8).**
`AgentTaskPipelineStatusService.cs`, `AgentTaskPipelineDtos.cs`,
`server/Api/Endpoints/DispatchPolicyEndpoints.cs`, project convenience route, client
invalidation entry. Tests: extend `AgentTaskPipelineStatusTests` (recommended reflects a PUT
without restart; `roleCap` reason and precedence; `dispatchPolicyRevision`; contract test keeps
the seed defaults 1/2/2), new `DispatchPolicyEndpointTests` (GET initializes; PUT validation
statuses; revisions page; project route 404/200; pipeline and policy agree on a role's limit,
as `HostEndpointTests.C654_Hosts_and_pipeline_agree_on_the_local_limit` does for hosts).

**S5 — script, docs, bundle and skill wording (D-1, D-8).**
`scripts/dispatch-policy.ps1`, `scripts/delegate.ps1` comment, `docs/ops-http.md`,
`docs/antiphon-api.md`, `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md`,
`AGENTS.md`, `.claude/skills/antiphon-orchestrator/SKILL.md`. Tests:
`StandingPipelinePolicyDocumentationTests` (existing pins stay green; add a pin that no copy
states "Code and Review at two" as the rule and that each names `recommendedInFlight`), a
`DelegateScriptKindTests`-style parse test for the new script's parameter sets.

Suggested order: S1 → S2 → S3 → S4 → S5, committing after each. S2 and S3 change behaviour an
orchestrator sees; land them together with S4 so the pipeline explains the new hold on the same
deploy. After landing, the rollout is the ordinary canonical restart; the first `GET
/api/dispatch-policy` on the desktop imports its user-secrets values (Plan 3, Code 5, Review 5,
`MaxOpenTasks` 9 per b1b80c61) into revision 1, and the operator can then delete those
user-secrets keys at leisure.

## Risks

- **Behaviour change at import.** With `maxQueued` null a stage accepts creates past its
  in-flight limit and holds them. An orchestrator following the old bundle will see a Queued
  task where it expected a 409. Mitigation: S5 ships in the same land; the hold detail and
  `roleCap` reason say why; the operator can set `maxQueued` 0 to restore refusal per role.
- **Coherence.** The gate must read the policy inside its lock, and the tick must count this
  tick's own dispatches; both are stated in D-5/D-7 and both get red tests.
- **Fixture drift.** The pipeline DTO gains fields; the desktop's local `pipeline.json` contract
  fixture must be re-captured by the Windows reviewer (it is already stale).
- **Bundle pins.** Wording edits in four copies must keep the pinned phrases; run
  `StandingPipelinePolicyDocumentationTests` and `TaskPlatformGuidanceTests` in S5.
- **CARD-0755 overlap.** S4 touches the same projection; keep the local-count rule as it is and
  leave CARD-0755's fix to its own card.

## Verification design (draft for TestDesign)

TestDesign owns the final red-test inventory, the positive-control plan and the cost; this
section gives it the invariants and a runnable closed roster to tighten.

| ID | Invariant | Evidence class |
|---|---|---|
| V-1 | Revision 1 imports the booted `DelegationSettings` values once; later config edits do not change a persisted policy; concurrent initializers publish one revision. | `DispatchPolicyServiceTests` |
| V-2 | PUT is complete-snapshot, `expectedRevision`-guarded, validated, audited (incident + event), revisioned and no-op safe; project rows revert by omission. | `DispatchPolicyServiceTests`, `DispatchPolicyEndpointTests` |
| V-3 | Create reads the live limits inside the lock: raise → accept, lower → refuse, without restart; role axis follows D-3 (in-flight and queued); 409 detail carries the four counts; override still warns. | `AgentTaskConcurrencyLimitTests` |
| V-4 | The tick holds a queued task at its role's in-flight limit with the `Held: stage '...'` trace, releases it when a slot frees, never touches running work, and orders the hold before host budgets. | `DispatchHoldVisibilityTests` |
| V-5 | Pipeline stages show the live `maxInFlight`/`maxQueued`, the `roleCap` queue reason in precedence, and the policy revision; the HTTP contract keeps the seed defaults. | `AgentTaskPipelineStatusTests`, `AgentTaskPipelineEndpointTests` |
| V-6 | GET/PUT/revisions/project routes return the documented statuses and agree with the pipeline. | `DispatchPolicyEndpointTests`, `HostEndpointTests` (unchanged, regression) |
| R-1 | Host budgets and runner capacity behave exactly as before (CARD-0654 classes green). | `HostBudgetServiceTests`, `HostEndpointTests`, `HostBudgetAdmissionTests` (part of `DispatchHoldVisibilityTests`) |
| R-2 | Every documentation copy keeps the pinned policy phrases and no copy states a number as the rule; stage bundle caps hold. | `StandingPipelinePolicyDocumentationTests`, `TaskPlatformGuidanceTests` |
| R-3 | The seed defaults are unchanged (Code/Review 2, others 1, `MaxOpenTasks` 6). | `AgentTaskServiceIntegrationTests` default pins, `AgentTaskConcurrencyLimitTests` first three methods |

Positive controls for the later Mutation stage (method-scoped, TestDesign names the exact
methods): PC-1 make `EffectiveAsync` return the seed instead of the row (V-3 raise test must
fail); PC-2 drop the role hold `continue` (V-4 third-Code test must fail); PC-3 read the policy
after the count instead of inside the lock (the concurrent create/PUT test must fail); PC-4
compute `queueReason` before the role check (V-5 precedence test must fail).

### Cost

Ordinary Code verification floor from the table: 12 + 6 + 8 + 8 + 4 + 10 + 4 + 6 = **58
minutes** (one isolated build of `tests/Antiphon.Tests`, reused by every later row). Authoring
estimate: 120-180 minutes for S1-S4 (migration, service, gate, tick, projection, endpoints) plus
30-45 minutes for S5. Code `ExpectAbout`: 210-270 minutes. Positive-control floor: four
method-scoped red/restore cycles, about 25-35 minutes. Estimates allow for build-slot waits on
server2 and are not measured timings.

### Checkpoints

Run through the CARD-0723 tool once per committed slice group (`--after S1-S3`, then
`--after S1-S5`), `wait` until the exit is not 75. Filters use the CARD-0403 combined-class
syntax; `Min` counts executed TUnit results. Floors on existing classes are their counts at the
inspected SHA (`AgentTaskConcurrencyLimitTests` 25, `DispatchHoldVisibilityTests` 28 including
its `HostBudgetAdmissionTests` partial, `AgentTaskPipelineStatusTests` 39 plus
`AgentTaskPipelineEndpointTests` 4, `HostBudgetServiceTests` 8 plus `HostEndpointTests` 4,
`StandingPipelinePolicyDocumentationTests` 4 plus `TaskPlatformGuidanceTests` 5,
`AgentTaskServiceIntegrationTests` 120); floors on the two new classes are design minimums.
TestDesign raises each floor by the methods it adds.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c505/` | policy-service | `/*/*/DispatchPolicyServiceTests/*` | V-1, V-2 | all listed, 0 failed/skipped | 8 | 12 | false |
| CP-2 | S1-S3 | CP-1 | create-gate | `/*/*/AgentTaskConcurrencyLimitTests/*` | V-3, R-3 | all listed, 0 failed/skipped | 25 | 6 | false |
| CP-3 | S1-S3 | CP-1 | dispatch-hold | `/*/*/DispatchHoldVisibilityTests/*` | V-4, R-1 | all listed, 0 failed/skipped | 28 | 8 | false |
| CP-4 | S1-S5 | `tests/Antiphon.Tests -> bin-c505b/` | pipeline-status | `/*/*/(AgentTaskPipelineStatusTests)\|(AgentTaskPipelineEndpointTests)/*` | V-5 | all listed, 0 failed/skipped | 43 | 8 | false |
| CP-5 | S1-S5 | CP-4 | policy-endpoints | `/*/*/DispatchPolicyEndpointTests/*` | V-2, V-6 | all listed, 0 failed/skipped | 8 | 4 | false |
| CP-6 | S1-S5 | CP-4 | host-regression | `/*/*/(HostBudgetServiceTests)\|(HostEndpointTests)/*` | R-1, V-6 | all listed, 0 failed/skipped | 12 | 10 | false |
| CP-7 | S1-S5 | CP-4 | policy-docs | `/*/*/(StandingPipelinePolicyDocumentationTests)\|(TaskPlatformGuidanceTests)/*` | R-2 | all listed, 0 failed/skipped | 9 | 4 | false |
| CP-8 | S1-S5 | CP-4 | seed-defaults | `/*/*/AgentTaskServiceIntegrationTests/*` | R-3 | all listed, 0 failed/skipped | 120 | 6 | false |
