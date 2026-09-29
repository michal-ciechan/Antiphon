# CARD-0505: configurable dispatch concurrency (parallel + queued limits per stage/project)

Date: 2026-09-29. Stage: TestDesign complete; the finalized verification design and eight-row
closed checkpoint table below are the Code handoff. D-1..D-9 remain the implementation plan.
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

## Verification design

Finalized by TestDesign task 1e0b8578 on 2026-09-29 against plan tip
a6a1daf2221289498a2e099297f6516bd1d76423. This is a test specification, not
executed test evidence. Code authors the cases below and runs the ordinary checkpoints;
Review judges that evidence before land; Mutation executes the PCs against the confirmed
SourceLanding snapshot. The D-1..D-9 implementation remains the plan, with the executable
boundary clarifications below.

### Inspection

| Bodies inspected | Boundaries covered |
|---|---|
| DelegationOpenGate (entire), AgentTaskService.CreateAsync transaction/override-warning region, AgentTaskConcurrencyLimitTests (all bodies and helpers) | V-3: real Postgres advisory lock through insert; project/null scope; absolute precedence; specialist/live-follow-up bypass. |
| RunnerDefaultSettingsService initialization/read/write, RunnerDefaultSettingsTests and RunnerDefaultMigrationTests, DefaultsHost, HostBudgetServiceTests and HostEndpointTests | V-1/V-2/V-6: migration, unique keys, first-reader race, revision conflict, atomic history/audit, notification failure, endpoint host. New policy classes reuse these fixtures' patterns, not their private/file-local types. |
| AgentTaskDispatcher.TickAsync occupancy/retained/queued/dispatch branches, DispatchHoldDetails classification, DispatchHoldVisibilityTests hold/release bodies and CreateWorld/seeding/World helpers, HostBudgetAdmissionTests (all) | V-4/R-1: local and remote budgets, role cap, same-tick accounting, retained process, fake stopper and clock. |
| AgentTaskPipelineStatusService.GetAsync/ToQueued, AgentTaskPipelineStatusTests defaults/counts/queue-reasons/helpers, AgentTaskPipelineEndpointTests contract, HostBudgetPipelineTests; C557 partial inventory and seed | V-5: coherent live fields, project occupancy, queue precedence, HTTP defaults; all C557 partial cases remain in the class filter. |
| StandingPipelinePolicyDocumentationTests and TaskPlatformGuidanceTests; AgentTaskServiceIntegrationTests default-policy bodies; DelegateScriptKindTests HTTP stub/process harness and DelegateScriptRunner | R-2/R-3: four documentation copies, seed pins, real pwsh parameter/HTTP behavior. |
| useSignalRInvalidation.ts and its entire test file, EventBus/AntiphonHub, AntiphonWebAppFactory isolation/reset, TestDbFixture schema creation, HerdrLabelFollowConcurrencyTests lock observer, checkpoint importer/validator and test-client.ps1 | Delivery receipt, isolated hosts, deterministic barriers, exact filters and count semantics. |

Missing setup is implementation work in S1-S5, not a reason to weaken assertions:

- Add the real DispatchPolicyService to the gate, pipeline and dispatcher test builders and
  hand-built service collections. Keep it scoped and use a fresh database context to verify
  commits. Persist an explicit policy for a fixture instead of mutating captured options after
  initialization. AntiphonWebAppFactory.ResetAsync clears caches, not database rows: use a
  fresh factory/schema for policy-mutating HTTP tests; do not poison the shared seed-contract host.
- Extend dispatcher seed helpers with project, role, status, runner and retained flags.
  Use distinct warm agents and nonoverlapping directories (or ReadOnly work) so shared-writer,
  pinned-agent, model or host holds cannot accidentally make a missing role hold look green.
  Existing host-only fixtures currently use Docs, whose imported cap is 1: explicitly seed
  that fixture's Docs maxInFlight as null when the assertion concerns only a host budget.
  Keep the default-policy tests on the real defaults.
- Adapt the existing second_debug_returns_409_on_the_role_axis and
  same_project_role_axis_still_refuses_and_lists_only_this_projects_role_occupant to an
  explicit Debug maxQueued=0 policy. Their former implicit refusal is intentionally changed
  by D-3. Preserve their assertions and counts. Existing pipeline tests that expect an
  unrelated hold/awaitingDispatch must similarly isolate that cause with explicit policy.
- Add bounded EF command/transaction interceptors in test helpers for lock, read and commit
  cuts. Use TaskCompletionSource with RunContinuationsAsynchronously, cancellation and
  finally-release/await cleanup. Observe actual database blockers with pg_blocking_pids
  (the Herdr fixture precedent); never infer a race from a delay or Task.WhenAll alone.
- New database classes are Integration. New script tests carry the existing assembly-local
  ParallelLimiter<ProcessSpawnLimit>. Test hosts use isolated Postgres and the established
  production-runner guard; no real agent launch is needed for this card.

### Executable boundary clarifications

**D-3 is testable under the existing gate lock.** The lock remains the current global
transaction-scoped advisory key; project isolation is a count predicate, not a new lock key.
Initialize the test policy first. Hold that key in connection A; start CreateAsync in B;
observe B waiting on A; PUT a lower policy through independent context C and commit it;
release A. B must use the committed lower policy and refuse, leaving no task. Also record
that B's policy SELECT occurs after its lock completes. This proves the required order.
The draft PC-3 ("read after the count") could still be inside the lock and is an equivalent
mutation; replace it with an actual read before acquiring the lock.

The gate serializes count-plus-insert among creates. It does not reserve a process slot or
serialize all PUTs/ticks. A policy edit committed after a create's locked snapshot applies
to the next operation; this test does not promise retroactive refusal of that create.
The dispatcher consumes one snapshot per tick and counts successful dispatches in that tick.
Use its existing AfterQueuedSnapshotAsync cut to commit a policy edit midway through a tick:
all candidates use the old revision, the next tick uses the new one. The contract assumes
the existing serialized dispatcher tick; multi-server concurrent dispatch ownership is outside
this card.

For two creates racing for one remaining queue place, hold the first creator immediately
before its task insert while it owns the gate. Start the second creator; observe its actual
lock wait, then release the first and await both. Exactly one task is inserted; the other
returns role 409. The interceptor also records whether a second task-count SELECT reached
the database before the first insert committed, so removing the lock fails a decisive
serialization assertion even if the scheduler happens to produce one success.
Await either the observed database lock wait or that second-count signal; assert the lock
wait won, then release in finally and await both creates. The no-lock mutant must fail this
assertion promptly, never wait for a blocker that can no longer exist.

**Precedence qualification:** D-6's blanket order and D-7's explicit insertion point disagree
for a full local host. Follow D-7's concrete insertion point and D-9's unchanged local-cap
behavior: the existing local-cap skip stays first; roleCap precedes the remote host-budget
decision. The pipeline deliberately reports lease → sibling land → pin → roleCap → hostBudget
→ concurrencyCap, as D-7 specifies. Test both contracts separately; do not claim dispatcher
and projection have identical diagnostic precedence. This qualifies the broad V-4/S3
"before host budgets" wording without moving the existing local gate.

**Inheritance and storage:** missing project fields/role rows inherit; an explicit role
maxInFlight=null is unbounded, not a request to inherit; maxQueued=null is unbounded waiting.
An omitted project reverts all its overrides. An override containing only maxOpenTasks must
not erase inherited role limits. Omission of a global role leaves it unbounded. A null task
ProjectId uses global policy and its own occupancy bucket; it is not a project override key.
D-2's "reproduces today's behaviour exactly" applies to imported numeric values, not create
admission: D-3 explicitly changes that behavior. Test the accepted third Code create.

Postgres uniqueness must include the nullable logical keys (global/project and absolute/role
rows). A plain nullable composite UNIQUE index does not establish this. Test the actual
migrated constraints on all four key shapes; Code supplies null-aware uniqueness (or equivalent
partial indexes), not just request validation. No production database is migrated by these tests.

### Delivery inventory

| Path and identity | Persistence/recovery | Receipt and substitute limits |
|---|---|---|
| Policy PUT → DispatchPolicyChanged(revision) through IEventBus/EventBus → SignalR subscriber → useSignalRInvalidation → policy/pipeline query consumers | Settings, limit rows, immutable revision and incident commit together before broadcast. The revision is the durable identity. Broadcast has no durable queue/outbox and is advisory; lost notification is recovered by a subsequent GET/refetch. | Service observing bus reads the same committed revision from a separate context at publication. E-9 below receives the actual event frame over the real hub, then GETs that revision. Client cases invoke the registered callback and observe QueryClient invalidation and mounted usePipeline data changing, including a pending fetch. |
| Gate → durable Queued task → dispatcher role hold/release | Existing AgentTask row is the real durable queue. No new session-input/completion-note route is introduced. A held task remains Queued; an eligible later tick dispatches it once. | H-1/H-7 assert the same task ID, durable state and Held/Dispatched events plus no stopper calls. Warm-agent/runtime fakes prove admission and scheduling only, not prompt delivery. |

For notification handoffs test commit failure before publication (no event or partial rows),
commit success followed by a throwing publisher (GET still returns the new revision; retry
does not duplicate history), a connected eligible subscriber and a disconnected subscriber
that recovers by GET. For a busy client, defer the initial pipeline HTTP response, deliver the
event during the fetch, complete it and refetch to the saved revision; do not assert eventual
delivery from an event attempt alone. No durable retry/delivery guarantee is added to SignalR.
Client callback/MSW tests substitute for the browser/socket; the separate real-hub test proves
the producer-to-subscriber segment. These joined contract tests do not claim browser E2E or
UserPrompt receipt. Busy/eligible agent callers, enqueue recovery and transcript receipts are
unchanged session paths, so they require no new synthetic message or transcript test here.

### Proves it works now

All names below are exact proposed method names. Each ordinary case runs fixed production
code; its "red" statement identifies the defect it detects. New APIs may initially fail to
compile while scaffolding is absent, but compile errors are not red evidence. Code first
authors assertions against a compiling legacy/disabled behavior, records the intended
assertion failure in the owning checkpoint rerun, then implements the behavior. Once the
fixed code is landed, only Mutation introduces the deliberate PCs. No placeholder/self-compare
test counts toward a floor.

**V-1/V-2 — DispatchPolicyServiceTests (17 methods, 17 results).**

| ID / method | Setup and decisive assertion; red cause |
|---|---|
| S-01 C505_Import_once_preserves_configured_values_after_restart | Seed MaxOpenTasks=9, Plan=3, Code/Review=5 and a configured unbounded role. Revision 1 contains those values, null maxQueued, Migration provenance/reason and one history row. Fresh service with different options returns the persisted policy; reimport is red. |
| S-02 C505_Concurrent_initializers_publish_one_revision | Two contexts both pass the initial absent read using an interceptor barrier before either INSERT. Both callers succeed and return identical revision 1; exactly one singleton and history row survive. No Task.WhenAll-only race proof. |
| S-03 C505_Existing_database_upgrades_without_changing_tasks | In an isolated schema migrate to the predecessor of AddDispatchPolicy, seed representative task/project, migrate forward with the real migrations, import policy and read it. Task/project survive unchanged; the three new tables/history work. |
| S-04 C505_Nullable_logical_keys_are_unique | Attempt direct duplicate inserts for (global,absolute), (global,Code), (project,absolute), (project,Code), each in its own rolled-back transaction. All four receive unique-constraint rejection; different projects/roles remain legal. No EF InMemory substitute. |
| S-05 C505_Put_round_trips_with_immutable_history | Complete global/project PUT survives a fresh context; revision increases once, previousRevision matches, historical JSON stays unchanged, caller/reason/provenance and old→new incident are exact. |
| S-06 C505_Stale_and_concurrent_writes_preserve_one_winner | Stale revision is the named conflict and changes nothing. Two contexts loaded at the same revision race through a controlled pre-save cut: one succeeds, one named conflict; winner's entire snapshot and one new history/incident survive. |
| S-07 C505_Invalid_ranges_leave_policy_unchanged | Loop named invalid cases: open -1/0/513, inFlight -1/0/513, queued -1/513, globally and in a project. Every rejection leaves revision, rows, history, incidents and event count unchanged. This loop contributes one result. |
| S-08 C505_Valid_boundaries_and_null_limits_round_trip | Cover open 1/512, inFlight 1/512/null, queued 0/512/null; explicit null remains null after a fresh read. |
| S-09 C505_Invalid_identity_and_metadata_leave_policy_unchanged | Unknown/numeric role; Check/Distill/Diagnose; unknown project; duplicate role/project/nested role; empty project; whitespace/401-character reason; unsupported provenance. Compare full saved snapshot and audit counts after each refusal. Valid reason lengths 1/400 are accepted. |
| S-10 C505_Reordered_identical_snapshot_is_a_noop | Reverse role/project array ordering and change only reason text on an otherwise identical PUT. No revision/history/incident/event or last-writer metadata change. Stale expectedRevision still conflicts even for an identical body. |
| S-11 C505_Project_inheritance_distinguishes_null_and_omission | Project absolute-only override inherits global roles; project Code overrides only Code; explicit null is unbounded; later global edit flows to inherited values; omit project to revert. Assert effective values and Source separately for project/global/default. |
| S-12 C505_Failed_commit_rolls_back_policy_history_and_incident | Scoped save/transaction interceptor fails at the write boundary after preparing all rows. Fresh reader sees original revision, limits/history/incident counts and zero notifications. |
| S-13 C505_Published_revision_is_already_committed | Observing IEventBus opens an independent context before returning; payload revision, complete policy, immutable history and incident already exist together. |
| S-14 C505_Lost_event_recovers_by_read_without_duplicate_history | Throw on publish after commit. PUT remains successful, new GET after service restart returns saved values; stale retry conflicts and current identical retry is a no-op. Exactly one new revision/incident, no second mutation. |
| S-15 C505_History_cursor_is_descending_and_exclusive | Three revisions; limit=1 and beforeRevision pages have stable descending order, exclude cursor and join to the exact prior snapshot. |
| S-16 C505_Snapshot_never_mixes_two_revisions | Pause the reader at its first policy SQL result; independent writer commits distinct global and project values. Release; snapshot must equal all-old or all-new, never old header/new limits. Verify again with a fresh reader. |
| S-17 C505_Auto_cannot_replace_a_human_policy | Public writer remains Human-only; call the reserved service-side Auto path against a Human row and require dispatch_policy_human conflict with no changes. Public Auto is tested as 422 at E-5. |

**V-3 — AgentTaskConcurrencyLimitTests (8 new methods, 16 new results; 25 existing = 41).**

| ID / method | Setup and decisive assertion; red cause |
|---|---|
| Q-1 C505_Live_absolute_raise_and_lower | Seed config MaxOpenTasks=1 and reuse the same gate/service with Custom tasks (no role cap). At one open task, policy 1 refuses; PUT 3 accepts a second task; PUT 2 refuses the next. Exactly two durable tasks and current 409 limit; captured options are red. |
| Q-2 C505_Role_queue_truth_table | Nine Arguments rows in the table below, one fresh schema each, absolute cap 100. Accept means one new Queued row; refuse means role 409 and no row. No dispatcher is run here. |
| Q-3 C505_Role_refusal_reports_counts_and_absolute_precedence | With one Dispatched + one Working Code, one Queued Code, limits 2/1, assert axis=role, count=2, limit=2, inFlight=2, queued=1, maxInFlight=2, maxQueued=1, message and project/occupant IDs. Lower absolute to 3: both full, axis=absolute and count/limit=3; no insert. |
| Q-4 C505_Project_overrides_and_null_bucket_are_isolated | Occupy project A, project B and null separately; A override differs from global. A refuses at its own bounds; B and null admit while below theirs. Exception lists no foreign occupants. Repeat after omitting A override. |
| Q-5 C505_Only_open_role_rows_consume_the_gate_counts | Seed Code in all statuses plus other roles/specialists. Dispatched/Working count as in-flight, only Queued as waiting; Blocked/terminal do not count, specialists do not consume absolute capacity. Assert the accepted/refused boundary and exact counts. |
| Q-6 C505_Bypass_queues_and_warns_about_dispatch_limit | Full 2/0 Code stage: ignoreConcurrencyLimit creates Queued, writes one Warning with project, counts and "dispatches when the role has a free slot"; no Dispatched event. H-7 proves its subsequent hold. |
| Q-7 C505_Waiting_create_reads_policy_after_gate_lock | Execute the three-connection lock/PUT cut above, 2 → 1 absolute at one occupant. Record read order; after release the create refuses with limit=1 and persists no task. |
| Q-8 C505_Concurrent_creates_take_only_last_queue_place | Two in-flight Code, queue limit 1, no queued Code, absolute 100. Use the pre-insert/lock-wait cut above. Exactly one create succeeds, one role 409 (2 running, 1 waiting); second count occurs only after first commit. |

Q-2 Arguments tuple is (inFlightCount, queuedCount, maxInFlight, maxQueued, accepted):

| inFlightCount | queuedCount | maxInFlight | maxQueued | accepted |
|---:|---:|---:|---:|---|
| 1 | 1 | 2 | 1 | true |
| 2 | 0 | 2 | 1 | true |
| 2 | 1 | 2 | 1 | false |
| 3 | 2 | 2 | 1 | false |
| 2 | 0 | 2 | 0 | false |
| 1 | 3 | 2 | 0 | true |
| 3 | 4 | 2 | null | true |
| 3 | 4 | null | 0 | true |
| 0 | 4 | null | null | true |

This includes full/under/over, zero waiting, each null and both null. maxQueued is deliberately
not a strict queue-length cap while a stage has a free in-flight slot; D-3 allows those creates.
Lowering below existing occupancy never cancels or removes queued/running rows.

**V-4 — DispatchHoldVisibilityTests (12 new methods, 17 new results; 28 existing = 45).**

| ID / method | Setup and decisive assertion; red cause |
|---|---|
| H-1 C505_Third_code_is_held_then_released | Two Code in-flight (one Working, one Dispatched), third queued, host seats available. Tick: SkippedRoleCap=1, Dispatched=0, one exact Held detail and Queued state. Repeated tick dedupes. Settle one; next tick dispatches the same ID exactly once. |
| H-2 C505_One_tick_accounts_for_its_own_dispatches | One in-flight Code, cap 2, three eligible queued on distinct warm agents/directories. Exactly one dispatch, two role holds, resulting Code occupancy 2. Counting only the pre-tick rows is red. |
| H-3 C505_Role_occupancy_spans_all_hosts | Local + remote Code fill the same project/role cap; eligible queued local and remote Code both stay Queued with roleCap. Other-role running rows do not consume Code slots. |
| H-4 C505_Project_override_and_null_scope_do_not_cross_hold | A full at override 1, B below global 2, null below global 2. A held; B and null dispatch. Give each separate agents/directories; verify per-ID outcomes. |
| H-5 C505_Lowering_role_limit_preserves_running_tasks_and_sessions | Lower 3→1 with two Code occupants and queued work. Running task/session IDs, statuses and retained flags remain byte-for-byte equivalent in selected fields; stopper is empty; only queued work is held. |
| H-6 C505_Raising_role_limit_releases_on_next_tick | Same dispatcher instance holds at 1, PUT 2, next tick dispatches exactly once without recreating options. |
| H-7 C505_Create_override_does_not_bypass_dispatch_role_hold | Use real CreateAsync with ignoreConcurrencyLimit under 2/0 Code policy; same durable task ID then enters the real dispatcher queue. It is held while full and dispatches once a slot frees. |
| H-8 C505_Unbounded_and_specialist_roles_have_no_role_hold | Four Arguments: Custom, Check, Distill, Diagnose. Custom with no global row dispatches beside occupied Code; specialists never receive roleCap or increment SkippedRoleCap. Use their existing no-process paths; do not infer prompt delivery. |
| H-9 C505_Retained_return_is_not_readmitted_by_role | Two Arguments: local, server2. Retained Working Code owns its process; role cap is below occupancy but host budget has room. CapacityWaitRetained clears; session ID/status remains, no stop/relaunch and no role hold for this return. |
| H-10 C505_Local_and_remote_budget_precedence_is_explicit | Two Arguments: local, server2, both role and host full. Local retains its earlier host/concurrency skip; remote reports roleCap before remote budget. Once role cap rises, remote hostBudget still holds, so role changes cannot bypass host admission. |
| H-11 C505_Role_detail_is_cap_and_ordinary_wait | Assert RoleCap text with role/project/count/limit; both raw and escalation-wrapped detail yield DispatchHoldClass.Cap and ExpectationHoldClass.OrdinaryWait. |
| H-12 C505_Tick_uses_one_policy_snapshot | Commit cap 2→1 at AfterQueuedSnapshotAsync; two initially eligible queued candidates use old cap and both dispatch in that tick. Next tick with another queued task holds at new cap. Count policy snapshot loads to exclude per-candidate EffectiveAsync database reads. |

**V-5 — AgentTaskPipelineStatusTests (5 new methods, 12 new results).**

| ID / method | Setup and decisive assertion; red cause |
|---|---|
| P-1 C505_Pipeline_reflects_live_policy_revision_and_counts | Same projection instance before/after PUT. Check recommendedInFlight/maxQueued/queuedCount/inFlightCount/atOrAboveRecommendation/policySource/dispatchPolicyRevision and real task IDs; stale options are red. |
| P-2 C505_Queue_reason_precedence | Eight Arguments: lease+all, sibling+pin+role+host, pin+role+host, role+remoteHost, role+localCap, remoteHost-only, localCap-only, none. Expected lease/sibling/pin/roleCap/roleCap/hostBudget/concurrencyCap/awaitingDispatch; assert heldBy only for the holder-bearing reasons. |
| P-3 C505_Role_reason_uses_project_counts_across_hosts | A full with local+remote occupants, B below cap and null below cap; same-role queued rows report only A held. B/null must not use fleet-wide occupancy as their project count. |
| P-4 C505_Project_overrides_do_not_replace_fleet_defaults | Fleet stage reports global limits, explicit projectOverrides contains effective overrides, missing global role yields null/default. Project row with explicit null stays unbounded; project omission removes its list entry. |
| P-5 C505_One_get_uses_one_policy_snapshot | Gate the first policy read, commit a different global and override revision and finish GET. All stage fields, overrides, reasons and dispatchPolicyRevision use one revision. Next GET shows the new one; no per-stage reloads. |

Extend the existing AgentTaskPipelineEndpointTests.pipeline_route_is_literal_and_returns_the_advisory_contract
in place with JSON fields/revision/null/default checks; retain 4 executed endpoint tests.
Source inventory at the inspected SHA is 35 results in AgentTaskPipelineStatusTests.cs,
19 in AgentTaskPipelineStatusC557Tests.cs and 4 in HostBudgetPipelineTests.cs = 58, not the
draft's 39. Hence CP-4 floor is 58 + 12 + 4 = 74.

**V-6 — DispatchPolicyEndpointTests (10 new methods, 15 results).**

| ID / method | Setup and decisive assertion; red cause |
|---|---|
| E-1 C505_Get_initializes_the_documented_contract | GET 200, revision=1, seed/global/project fields, camelCase, explicit null limits and initial metadata. Read-only GET creates only the one initial policy revision. |
| E-2 C505_Put_and_pipeline_agree_without_restart | PUT complete body with valid caller identity; GET policy/project/pipeline from the same host. Exact new revision/limits and LastCallerTaskId agree. |
| E-3 C505_Stale_revision_is_409_without_mutation | Current PUT followed by stale PUT yields ProblemDetails code dispatch_policy_revision_conflict and unchanged GET/history/incident counts. |
| E-4 C505_Missing_required_top_level_field_is_422 | Six Arguments omit expectedRevision/maxOpenTasks/roles/projects/reason/provenance respectively from a valid JSON object; assert 422 and unchanged policy. Use raw JSON so serializer defaults cannot hide omission. |
| E-5 C505_Invalid_wire_shapes_are_422_without_mutation | Named loop covers null arrays, wrong field types/unknown or numeric/specialist roles, duplicate project/role keys, unknown project, empty override, invalid bounds/reason/provenance including Auto; valid zero maxQueued and null role limits succeed. Malformed JSON syntax follows existing middleware's 400 contract separately in this method. |
| E-6 C505_Revisions_route_has_exclusive_cursor | GET revisions returns descending complete snapshots with exclusive beforeRevision and bounded limit; no duplicate page entries. |
| E-7 C505_Project_route_inherits_overrides_reverts_and_404s | Known project with no override 200; full PUT installs override; omission restores inherited values and override=null; unknown project 404. |
| E-8 C505_Task_create_returns_201_or_counted_role_409 | Real POST /api/agent-tasks under full stage with maxQueued=null returns 201 Queued; PUT maxQueued=0 and repeat yields 409 with all four additive counts plus legacy count/limit, correct project/occupants and no extra row. |
| E-9 C505_Committed_revision_reaches_a_live_hub_subscriber | Fresh guarded Program factory; negotiate and connect through TestServer's WebSocket client to the real /hubs/antiphon with JSON protocol. Await handshake, PUT, receive DispatchPolicyChanged frame with saved revision, GET saved policy. Disconnect, PUT again, reconnect/refetch and see the later revision without requiring replay. No external socket/runner or new SignalR client package is necessary. |
| E-10 C505_Read_after_notification_failure_returns_committed_policy | Isolated endpoint host with throwing IEventBus: successful PUT still returns committed revision, fresh GET/history agree and a stale retry is a named conflict. |

Map the real policy/project endpoints and real create handler in the lean HTTP host, with
ExceptionMiddleware and identical JSON enum options (DefaultsHost precedent). Map/register
pipeline dependencies rather than assuming the full task endpoint map works with an incomplete
ServiceCollection. E-9 alone uses the full guarded Program factory with the production EventBus.

### Guards the regression

| ID | Tests and decisive assertions |
|---|---|
| R-1 | All 8 HostBudgetServiceTests, all 4 HostEndpointTests, the 11 HostBudgetAdmissionTests results inside DispatchHoldVisibilityTests, and the 4 HostBudgetPipelineTests results inside AgentTaskPipelineStatusTests. Keep configured/declared minimum, null fallback, zero drain, retained-return admission and local/remote count assertions. H-9/H-10 add composition with the new role cap. |
| R-2 | Keep 4 StandingPipelinePolicyDocumentationTests and 5 TaskPlatformGuidanceTests. Add C505_Every_copy_reads_live_limits_and_preserves_seed_guidance (one result): every active policy section points at recommendedInFlight/maxQueued and /api/dispatch-policy, retains the four pinned phrases and states 2/2/1/6 only as seeds. Scope the old-numeric-rule prohibition to active instructions, not historical quotations. Assert orchestrator preset length does not exceed the pre-edit normalized length recorded by Code. |
| R-3 | Keep all 120 AgentTaskServiceIntegrationTests results, especially shipped_roles_recommend_one_in_flight (10), code_and_review_recommend_two_in_flight (2), custom_and_check_have_no_recommendation_unless_configured and configured recommendation validation. Keep AgentTaskConcurrencyLimitTests' first three methods (4 results) and pipeline endpoint default pins. |

R-2 also adds DispatchPolicyScriptTests (4 Integration methods, each one result):

- C505_Script_parses_and_is_ascii: PowerShell parser returns zero errors; source is ASCII;
  all get/set-role/set-project/revert-project/history invocation shapes bind. This is a
  separate result from the HTTP behavior tests.
- C505_Set_role_preserves_snapshot_and_fresh_revision: execute the real script with
  ProcessStartInfo.ArgumentList against a loopback stub, GET a nontrivial snapshot, then
  PUT changed Code only with that revision/reason/Human and untouched projects/other roles.
- C505_Project_set_revert_and_history_use_the_documented_routes: exercise project ID/name
  resolution, override insertion, revert by omission and history cursor; assert exact
  HTTP paths/bodies, including null versus zero.
- C505_Conflict_is_reported_without_blind_retry: scripted GET then PUT 409; nonzero process
  exit, useful revision-conflict output, one PUT only, no silent overwrite/refetch-retry.

V-2/V-5 delivery adds three Vitest cases in useSignalRInvalidation.test.ts (5 existing + 3 = 8):

- C505 invalidates dispatch policy and pipeline only: seed both query keys and an unrelated
  key, invoke registered DispatchPolicyChanged callback, assert both invalidated and unrelated
  unchanged; unmount removes the handler.
- C505 mounted pipeline refetches committed revision: real hook/QueryClient with MSW responses
  at revisions 1 then 2; callback causes rendered query data to become revision 2/new limits.
- C505 event during pending fetch recovers latest revision: delay initial HTTP response,
  deliver revision-2 event while fetching, finish response, explicitly refetch as needed;
  final hook data is revision 2. This proves recovery, not an unpromised reliable-event queue.

The legacy implementation is red for these additions: no dispatch-policy route/script/event
mapping/live stage limits. Existing regression cases may already be green; preserve them
rather than pretending they are newly red tests.

### Guard inventory

Resource admission, persistent operator intent and notification recovery are the critical
guards below. Each G-n maps to exactly one distinct PC-n. PCs 1-4 retain the draft numbering
but use real compiling defects. Boundary variants within one guard are named explicitly.

| Guard | Plan invariant / protected decision | PC |
|---|---|---|
| G-1 | D-4/D-5: persisted policy, not options, controls the next create | PC-1 |
| G-2 | D-3/D-7: full role cannot dispatch new work | PC-2 |
| G-3 | D-4/D-5: policy read follows gate acquisition | PC-3 |
| G-4 | D-7: role reason outranks host cap but not lease/sibling/pin | PC-4 |
| G-5 | D-3: role refusal requires both full counts and both finite limits | PC-5 |
| G-6 | D-5: gate exclusion lasts through count/insert commit | PC-6 |
| G-7 | D-3: absolute open-task ceiling still refuses | PC-7 |
| G-8 | D-3/D-5: gate counts only its project including null bucket | PC-8 |
| G-9 | D-2: legacy import cannot overwrite persisted intent | PC-9 |
| G-10 | D-2: concurrent initialization has one successful shared result | PC-10 |
| G-11 | D-4: stale expectedRevision cannot overwrite a winner | PC-11 |
| G-12 | D-4: invalid numeric limits never persist | PC-12 |
| G-13 | D-2: database enforces nullable logical row uniqueness | PC-13 |
| G-14 | D-4: limits/revision/history/audit commit atomically | PC-14 |
| G-15 | D-2: project override presence/null/inheritance preserve intent | PC-15 |
| G-16 | D-7: this tick's successful dispatches consume role slots | PC-16 |
| G-17 | D-3: every host consumes the same project/role ceiling | PC-17 |
| G-18 | D-7: lowering never stops or rewrites running tasks/sessions | PC-18 |
| G-19 | D-7/D-9: retained return is not fresh role admission | PC-19 |
| G-20 | D-4: a tick uses one policy revision for all candidates | PC-20 |
| G-21 | D-4/D-7: a GET cannot project mixed revisions | PC-21 |
| G-22 | D-4: publication follows durable commit | PC-22 |
| G-23 | D-4: lost notification preserves committed read/retry recovery | PC-23 |
| G-24 | D-3: ignoreConcurrencyLimit grants create bypass only | PC-24 |
| G-25 | D-7: role waits are ordinary capacity, not watchdog faults | PC-25 |
| G-26 | D-8: script's complete PUT preserves unedited operator settings | PC-26 |

Guard count=26, mapped=26, missing=0, duplicate PC mappings=0. Input shape/status wording,
seed prose, script parsing and client query-key names also have ordinary assertions. They
introduce no additional admission or durable-delivery authority; no further critical guards
are claimed for them. Existing host/routing/session guards are unchanged and covered by
named regressions, not re-mutated wholesale.

### Positive controls

Mutation runs each listed variant alone at confirmed L: exact-method green → compiling
defect → intended assertion-red → restore with fresh source timestamp → isolated rebuild
→ exact-method green. A compile failure, fixture/connection timeout, zero results or unrelated
assertion is invalid evidence. TUnit selector is exactly
/*/Antiphon.Tests.Application/<Class>/<Method> using the full names below, no class wildcard
or broad lane; Q-2 has nine executed Arguments results, P-2 has eight, H-9 has two,
and every other selected method has one.
Keep native command receipts/TRX and restoration outside the SourceLanding worktree under
its assigned verification root. Do not commit/push the mutant or its evidence.

| PC / variants | Compiling production defect | Exact class.method and intended red |
|---|---|---|
| PC-1 | Resolve the gate's effective limits from seed options after initialization. | AgentTaskConcurrencyLimitTests.C505_Live_absolute_raise_and_lower: the create after raising 1→3 still refuses instead of inserting the second task. |
| PC-2 | In the role-full branch remove its continue while leaving tracing/counters intact. | DispatchHoldVisibilityTests.C505_Third_code_is_held_then_released: third task becomes Dispatched; expected Queued/Dispatched=0 fails. |
| PC-3 | Move effective-policy read before TakeLockAsync and reuse that snapshot afterwards. | AgentTaskConcurrencyLimitTests.C505_Waiting_create_reads_policy_after_gate_lock: recorded order is wrong and lowered-policy refusal is absent. |
| PC-4 | Move role-reason assignment after host/concurrency assignment without overriding it. | AgentTaskPipelineStatusTests.C505_Queue_reason_precedence: role+host rows say hostBudget/concurrencyCap instead of roleCap (8 results). |
| PC-5a | Replace the finite-limit count conjunction with OR. | AgentTaskConcurrencyLimitTests.C505_Role_queue_truth_table: under-running/full-queue and full-running/free-queue accepted rows refuse. |
| PC-5b | Change the two count comparisons from >= to >. | Same exact Q-2 method: equality refusal rows admit. |
| PC-5c | Coalesce null maxQueued to 0 for create refusal. | Same exact Q-2 method: unbounded-waiting row refuses. |
| PC-5d | Coalesce null maxInFlight to 1 for create refusal. | Same exact Q-2 method: unbounded-in-flight/zero-waiting row refuses. |
| PC-6 | Remove the await TakeLockAsync acquisition from EnsureCanCreateAsync. | AgentTaskConcurrencyLimitTests.C505_Concurrent_creates_take_only_last_queue_place: second count occurs before first commit (or two rows insert); lock-state/count-order assertion is red, not a timeout. |
| PC-7 | Remove the absolute-exceeded term from refusal while retaining role logic. | AgentTaskConcurrencyLimitTests.C505_Live_absolute_raise_and_lower: initial absolute-full Custom create succeeds instead of named refusal. |
| PC-8 | Remove the task ProjectId predicate from gate occupancy. | AgentTaskConcurrencyLimitTests.C505_Project_overrides_and_null_bucket_are_isolated: B/null admission fails due to foreign occupants. |
| PC-9 | On the existing-row branch overwrite effective numeric limits from current seed options and save. | DispatchPolicyServiceTests.C505_Import_once_preserves_configured_values_after_restart: configured persisted 9/3/5 values change on fresh service. |
| PC-10 | Replace initialization's duplicate-insert recovery/re-read with rethrow. | DispatchPolicyServiceTests.C505_Concurrent_initializers_publish_one_revision: the controlled loser returns DbUpdateException rather than the same successful revision. Catch outcomes and assert two successes so fixture failure is not mistaken for a PC. |
| PC-11 | Remove the explicit expectedRevision comparison in PutAsync. | DispatchPolicyServiceTests.C505_Stale_and_concurrent_writes_preserve_one_winner: sequential stale request overwrites/returns success instead of conflict. |
| PC-12a/b/c | Independently bypass numeric validation for maxOpenTasks / maxInFlight / maxQueued. | DispatchPolicyServiceTests.C505_Invalid_ranges_leave_policy_unchanged: the corresponding named invalid request succeeds or changes saved state. Run three independent variants, not one combined removal. |
| PC-13 | Remove the effective unique index definitions from generated migration/model for logical policy-limit keys, leaving types/columns valid. | DispatchPolicyServiceTests.C505_Nullable_logical_keys_are_unique: at least one of the four direct duplicate inserts succeeds. Use a fresh migrated schema/output for the variant, not a pre-mutant template. |
| PC-14 | Insert a SaveChanges commit of settings/limits before adding history/incident, outside their transaction; retain the later save. | DispatchPolicyServiceTests.C505_Failed_commit_rolls_back_policy_history_and_incident: inject failure in the later history/incident write; changed limits/revision remain visible. The fault targets that write, not the first arbitrary save. |
| PC-15 | Treat a present project role's null maxInFlight as absence and fall back to global. | DispatchPolicyServiceTests.C505_Project_inheritance_distinguishes_null_and_omission: explicit unbounded override becomes finite. |
| PC-16 | Do not increment (project,role) occupancy after a successful dispatch in this tick. | DispatchHoldVisibilityTests.C505_One_tick_accounts_for_its_own_dispatches: more than one waiting Code dispatches and occupancy exceeds 2. |
| PC-17 | Add a local RunnerId-only predicate to role occupancy. | DispatchHoldVisibilityTests.C505_Role_occupancy_spans_all_hosts: remote running Code vanishes from the cap and waiting work dispatches. |
| PC-18 | Add a wrong lower-policy reaction that marks a matching Working occupant Canceled before saving. | DispatchHoldVisibilityTests.C505_Lowering_role_limit_preserves_running_tasks_and_sessions: the saved running status changes. This mutation runs only against isolated fixture rows, never fleet tasks. |
| PC-19 | Apply the fresh role-cap hold to retained-return admission before clearing CapacityWaitRetained. | DispatchHoldVisibilityTests.C505_Retained_return_is_not_readmitted_by_role: retained flag stays true (2 Arguments results). |
| PC-20 | Reload policy inside each queued-candidate iteration instead of using the tick snapshot. | DispatchHoldVisibilityTests.C505_Tick_uses_one_policy_snapshot: post-cut cap 1 affects this tick, so expected two dispatches are absent. |
| PC-21 | Reload policy for each projected stage after its first read, keeping the first header revision. | AgentTaskPipelineStatusTests.C505_One_get_uses_one_policy_snapshot: old dispatchPolicyRevision is joined to new stage/override values. |
| PC-22 | Move PublishToAllAsync before the policy transaction commits. | DispatchPolicyServiceTests.C505_Published_revision_is_already_committed: observing reader cannot find the advertised committed revision/incident. |
| PC-23 | Re-throw the post-commit publish exception instead of allowing committed-read recovery to return success. | DispatchPolicyEndpointTests.C505_Read_after_notification_failure_returns_committed_policy: PUT returns failure instead of success despite the committed row; subsequent GET checks expose the gap. |
| PC-24 | Exempt queued tasks carrying the concurrency-override Warning from the role-full continue. | DispatchHoldVisibilityTests.C505_Create_override_does_not_bypass_dispatch_role_hold: overridden create dispatches while the role is full. |
| PC-25 | Remove the stage-prefix ordinary-wait recognition from DispatchHoldDetails.Classify/IsOrdinaryWait. | DispatchHoldVisibilityTests.C505_Role_detail_is_cap_and_ordinary_wait: classification is not OrdinaryWait. |
| PC-26 | In set-role, emit projects=[] instead of copying the GET snapshot's project overrides into PUT. | DispatchPolicyScriptTests.C505_Set_role_preserves_snapshot_and_fresh_revision: captured PUT loses the seeded project override. |

There are 26 PC IDs and **31 variants** (PC-5 has four, PC-12 has three, all others one).
Keep PC-13's migration mutation compiling and verify actual constraint rejection, not only
EF metadata. S-16 separately tests store-level read coherence; P-5/PC-21 catches a projection
that defeats it by taking multiple snapshots.

### Out of scope and evidence limits

- No UI panel (CARD-0506), desktop host-budget override, routing/model-policy changes, live
  fleet PUTs/restarts or CARD-0755 work. Current source already filters local occupancy in
  AgentTaskPipelineStatusService; preserve it and its existing regression cases rather than
  acting on the draft's stale defect description.
- No full-assembly/namespace run, browser E2E or native pty battery: bounded methods/classes
  cover the changed admission, persistence and projection seams. Documentation unit tests
  and existing seed tests stay inside the closed roster. Tests run on isolated Postgres,
  not SQLite/InMemory for locking/uniqueness claims.
- All commands/counts/costs here are designed, not executed. TestDesign performed source,
  table and whitespace checks only. This shell has pwsh but no dotnet or node executable,
  so it cannot supply compiled discovery/TRX/Vitest evidence; Code uses its configured build
  lane. Do not report these source-derived floors as passing executions.

### Cost

Estimated, not measured. Ordinary Code verification floor =
15 + 10 + 14 + 14 + 12 + 12 + 8 + 7 = **92 minutes**. This includes two isolated builds
(CP-1 and CP-4, budget 6 minutes each within those rows), 338 minimum TUnit results and
8 Vitest results; argument and partial-class expansion is accounted for below. It does
not assume one build can be reused after S4/S5 change the source. Slot queue time may
increase elapsed time; a slot timeout is reported, never bypassed.

Authoring estimate for implementation, deterministic fixtures, tests, migration and S5 =
240-330 minutes. Code ExpectAbout = **332-422 minutes** (authoring + ordinary floor).
Red-first checkpoint reruns add the actual time of the rerun rows and must be reported;
they are not disguised as green runs or free work.

Mutation floor = **206 minutes**: 8 for isolated setup/build + 31 variants × 6 minutes
(each exact-method green/compiling-defect/red/restore/fresh-build/green cycle) + 12 for
guard discovery, evidence and restoration reporting. Filters are the exact method selectors
in the PC table; no PC runs a whole class. Ordinary + Mutation verification floor =
**298 minutes**; authoring + both floors = **538-628 minutes** before the separate Review.
A full ordinary Review rerun budgets another 92 minutes (total 630-720 excluding review
analysis). Keep Code and Mutation estimates separate when commissioning.

Build reuse saves five redundant backend builds compared with rebuilding for each of the
seven TUnit rows: 5 × 6 = approximately **30 minutes**. Extra red/fix reruns and slot waits
are not included in that saving. No saving is claimed for dropping a named invariant.

### Checkpoints

Closed list, exactly eight rows. Run the checkpoint tool once after committed S1-S3 with
--after S1-S3 --rows CP-1,CP-2,CP-3, then once after committed S1-S5 with
--after S1-S5 --rows CP-4,CP-5,CP-6,CP-7,CP-8. The After selector is cumulative: S1-S5
alone would select CP-1..3 again, so the explicit rows are required. Await every run; exit 75
means call wait again, not success. Use --max-wait 50s to keep foreground waits bounded.
The launcher itself is a build driver: invoke it through
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c505-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-29-card-0505-configurable-dispatch-concurrency-plan.md --after S1-S3 --rows CP-1,CP-2,CP-3 --max-wait 50s
(substitute the second After/rows selection above for the second group). Rows acquire their
own slots. Any additional
red-first/build/test command is reported with its reason; prefer owning CP row reruns.

Floors are source-derived roster counts, not measured discovery: service 17; gate 25+16=41;
holds 28+17=45; pipeline 58+12+4=74; endpoints 10+5 argument expansions=15; unchanged
host service/endpoints + task service 8+4+120=132; docs 4+1+5 plus script 4=14.
The new script class is included in CP-7. CP-8 covers the otherwise omitted client change;
its Min is n/a because Vitest results are not TUnit executions. It must report all eight
named client results with zero failures/skips. Its slot wrapper is explicit for command-row
compatibility. Existing regression methods are retained; additions cannot replace those
counts. Exact executed identities and argument rows must appear in fresh reports.

Combined-class filters use parenthesized operands with suffix wildcards for TUnit 1.44;
confirm only the intended classes execute. Build reuse stays within the same After group.
The two partial test classes and Program factory are not a license to truncate their roster.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | tests/Antiphon.Tests -> bin-c505/ | policy-service | /*/*/DispatchPolicyServiceTests/* | V-1, V-2 | all listed, 0 failed/skipped | 17 | 15 | false |
| CP-2 | S1-S3 | CP-1 | create-gate | /*/*/AgentTaskConcurrencyLimitTests/* | V-3, R-3 | all listed, 0 failed/skipped | 41 | 10 | false |
| CP-3 | S1-S3 | CP-1 | dispatch-hold | /*/*/DispatchHoldVisibilityTests/* | V-4, R-1 | all listed, 0 failed/skipped | 45 | 14 | false |
| CP-4 | S1-S5 | tests/Antiphon.Tests -> bin-c505b/ | pipeline-status | /*/*/(AgentTaskPipelineStatusTests*)\|(AgentTaskPipelineEndpointTests*)/* | V-5, R-1, R-3 | all listed, 0 failed/skipped | 74 | 14 | false |
| CP-5 | S1-S5 | CP-4 | policy-endpoints | /*/*/DispatchPolicyEndpointTests/* | V-2, V-3, V-5, V-6 | all listed, 0 failed/skipped | 15 | 12 | false |
| CP-6 | S1-S5 | CP-4 | unchanged-regressions | /*/*/(HostBudgetServiceTests*)\|(HostEndpointTests*)\|(AgentTaskServiceIntegrationTests*)/* | R-1, R-3, V-6 | all listed, 0 failed/skipped | 132 | 12 | false |
| CP-7 | S1-S5 | CP-4 | policy-docs-script | /*/*/(StandingPipelinePolicyDocumentationTests*)\|(TaskPlatformGuidanceTests*)\|(DispatchPolicyScriptTests*)/* | R-2 | all listed, 0 failed/skipped | 14 | 8 | false |
| CP-8 | S1-S5 | n/a | policy-client | pwsh -NoProfile -File scripts/build-slot.ps1 -Label c505-client -- pwsh -NoProfile -File scripts/test-client.ps1 src/hooks/useSignalRInvalidation.test.ts | V-2, V-5 | all 8 Vitest results, 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 7 | true |
