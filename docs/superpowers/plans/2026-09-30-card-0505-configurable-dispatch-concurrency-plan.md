# CARD-0505: configurable project and stage dispatch concurrency

Plan, 2026-09-30. Source baseline: `5ae016a7de1313f4313ee8f7e4fc784175fa80e8`.
TestDesign, 2026-09-30, against `c45c4ba27cf5a9ceab976179c2836a76238132b4`.
Next stage: Code, subject to the source-collision ordering below. This artifact changes
no product code or live settings.

## Outcome and scope

Add durable, audited global defaults and per-project overrides for delegate admission,
parallel execution and queue depth. Reuse the runner-defaults settings pattern. Preserve
the current combined-open admission policy until a human explicitly selects separate
parallel and queue limits. Apply subsequent API edits without restarting the server.

CARD-0505's September 13 description is historical: neither absolute three nor a
universal role cap of one describes this checkout. The related UI card already exists:
**CARD-0506**. **CARD-0749**, currently Review, overlaps the runtime-settings portion;
coordinate it before commissioning Code. Its description is not evidence that its
implementation is present in this baseline.

### Verified current state

All file:line references below refer to the source baseline, not the eventual implementation.

| Concern | Verified behavior and source |
|---|---|
| Create entry point | `POST /api/agent-tasks` calls `AgentTaskService.CreateAsync` and returns 201, not proof of execution (`server/Api/Endpoints/AgentTaskEndpoints.cs:21`). |
| Project admission | `DelegationSettings.MaxOpenTasks` defaults to **6**, per `ProjectId`; null project is its own bucket (`server/Application/Settings/DelegationSettings.cs:29`). It is different from the local dispatch cap `MaxConcurrentTasks`, default **2** (`:23`). |
| Role admission | Startup configuration `Delegation:RolePolicy:<role>:RecommendedInFlight` is already configurable. Shipped Code/Review are **2**, other configured roles **1**; absent Custom is unbounded on the role axis (`server/Application/Settings/DelegationSettings.cs:336`, `:1055`, `:1078`). Helpers are roles too; specialists are Check/Distill/Diagnose (`server/Domain/Enums/AgentTaskEnums.cs:26`, `:427`). |
| Counted states | The create gate counts **Queued + Dispatched + Working**, excluding specialists and Blocked, in the same project. It does not count only running processes (`server/Application/Services/DelegationOpenGate.cs:20`, `:112`). Live follow-up requests bypass admission; retired-agent continuations do not (`server/Application/Services/AgentTaskService.cs:471`, `:1422`). Once inserted, a non-specialist follow-up is not excluded from other requests' occupancy counts. |
| Atomic create | PostgreSQL transaction advisory lock `antiphon.delegation.max-open-tasks` serializes count plus insertion; the lock itself is fleet-wide even though its query is project-scoped (`server/Application/Services/DelegationOpenGate.cs:63`, `:107`; `server/Application/Services/AgentTaskService.cs:1431`, `:1571`). |
| Refusal and override | 409 `concurrency_limit` names `axis`, role, count, limit, project, and up to 12 occupants. Absolute wins when both axes are full (`server/Application/Services/DelegationOpenGate.cs:77`; `server/Application/Exceptions/ConcurrencyLimitException.cs:18`, `:80`). `IgnoreConcurrencyLimit` skips both create limits and writes a Warning if exceeded; it does not raise host capacity (`server/Application/Services/AgentTaskService.cs:1555`; `scripts/delegate.ps1:207`, `:1142`). |
| Settings lifetime | Gate and pipeline capture `IOptions<DelegationSettings>.Value`; configuration binding is in `server/Program.cs:167`. There is no project-specific concurrency store or runtime role-limit API in this baseline (`server/Application/Services/DelegationOpenGate.cs:27`; `server/Application/Services/AgentTaskPipelineStatusService.cs:41`). `ProjectSetupDtos`' `MaxConcurrentTasks` is a catalog projection of the same local setting, not a project override (`server/Application/Services/ProjectSetupService.cs:206`). |
| Pipeline | `GET /api/agent-tasks/pipeline` is fleet-wide, with no project filter (`server/Api/Endpoints/AgentTaskEndpoints.cs:69`). It exposes role recommendations and separate in-flight/queued/blocked/ready arrays. In-flight is Dispatched/Working, whereas admission includes Queued. `recommendationsAreAdvisory=true` describes this projection; it does not disable the create gate (`server/Application/Services/AgentTaskPipelineStatusService.cs:153`, `:183`, `:223`; `server/Application/Dtos/AgentTaskPipelineDtos.cs:8`). |
| Dispatcher | Local non-specialist Dispatched/Working, excluding retained capacity waits and runner-bound work, consumes the local host budget. FIFO queued iteration can hold on host capacity, checkout/scope, pins, model availability, preparation and other eligibility gates (`server/Application/Services/AgentTaskDispatcher.cs:405`, `:427`, `:561`). It currently has no independent project/role running cap. Task `FOR UPDATE` prevents duplicate launch of one task, not two different tasks exceeding a new shared cap (`:4404`). |
| Existing host controls | `HostBudgets` persists `PUT /api/hosts/{hostId}/budget`, range 0..512 or null; null restores config/declaration, zero holds new host work. Remote effective limit is `min(budget, declared capacity)` (`server/Application/Services/HostBudgetService.cs:35`, `:71`). Runner capacity PUT is already live, constrained by configured MaxCapacity (`server/Api/Endpoints/SessionRunnerEndpoints.cs:64`; `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:482`). |
| Readiness and drain | `dispatchEligible` describes a live recovered connection. `acceptingNewWork` additionally excludes draining/retired runners; neither is a project or stage budget (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:625`). Read both; an available runner need not accept new tasks. |
| Placement settings | `GET/PUT /api/runner-defaults` stores a global runner and per-kind defaults, imports startup config once, uses expectedRevision, reason, Human/Auto provenance, history and post-commit notification (`server/Application/Services/RunnerDefaultSettingsService.cs:52`, `:114`, `:204`; `server/Api/Endpoints/RunnerDefaultEndpoints.cs:19`). Its tables and revision token are in `server/Infrastructure/Data/AppDbContext.cs:2213`. Placement defaults do not move existing queued tasks. |
| Routing pins | `/api/routing-pins` and `scripts/routing-pin.ps1 get\|set\|clear` choose agent/model and dated eligibility at card+role or role grain; they are not capacity overrides. Human provenance protects operator choices from Auto edits (`scripts/routing-pin.ps1:14`; `docs/antiphon-api.md:567`). |
| Orchestrator policy | Default Code/Review at two, other stages at one, Code feed depth two, same-source-area deferral and restricted absolute-axis override are operator policy (`docs/orchestration-loop.md:337`, `:526`). Feed depth includes ready cards; it is not a durable queue-size limit. |

Read-only live observation, **2026-09-30T16:55:10Z**, through the assigned API:

- `/api/version`: `7063845a9d8e85d7a7cbc7ffcbc2f528ea850c4b`, capabilities include `land-v2`.
- Pipeline recommendations: Code **5**, Review **5**, Plan **3**, Investigate **3**,
  TestDesign/Mutation **1**. Other configured helpers report 1; Custom reports null.
  The brief's earlier Plan 2 and Review 2 observations are therefore not current values
  to seed blindly. The endpoint does not disclose the configuration provider or expose
  live `MaxOpenTasks`; **6 is source/default plus brief evidence, not a newly measured
  live absolute limit**. No test dispatch was sent to infer it.
- Local effective host limit **2**; server2 effective/declaration **10**, no persisted host
  override. Pipeline reported local in-flight 1 and server2 host occupancy 6.
- Runner defaults revision **2**, Human, global `server2`, no per-kind overrides.
- server2 accepts new work. server2-temp's session-runner listing had `dispatchEligible=true`
  but `acceptingNewWork=false`; `/api/hosts` reported that host ineligible for new admission.

These are dated observations, not desired constants. Re-read at deployment. The plan neither
reads secret configuration nor edits operator settings.

### Gap and related-card reconciliation

The missing pieces are runtime audited project/role settings, inheritance/reset, explicit
queue admission limits, an independent running gate, and consistent source-aware diagnostics.
Merely replacing `MaxOpenTasks` with a database integer would still count the queue against
parallelism and would not satisfy the request.

Duplicate search was exhaustive: `pwsh -NoProfile -File scripts/card.ps1 search 'concurrency'
-Board Antiphon -All` returned 60 cards; `get CARD-0506` and `get CARD-0749` read both full texts.
CARD-0506 is Backlog and already declares its dependency on CARD-0505. Caller follow-up:
cross-link this plan into those two cards and CARD-0505; reuse CARD-0506 rather than filing
another UI card. No cards were created or edited by this Plan task.

**TestDesign reconciliation (read-only board/task evidence, 2026-09-30).** The verified
CARD-0505 title is **Configurable dispatch concurrency: parallel + queued limits per
stage/project**. CARD-0749 is **Make delegation concurrency limits runtime-driven and
API-overridable**, still Review, revision 16 (last move 2026-09-26T20:12:13Z). Its task
list is not evidence that its original runtime scope is implemented:

- Code `fd9aa660-f418-4a17-904e-4f89d2e32094` implemented **defaults only** at
  `2197f3d8e524b2c50cf5de421bc8553a1090891f`. Landing operation
  `710b302a-8bd7-492b-8c66-2d1e3022ddf2` reports Complete/Landed to master, confirmed
  2026-09-26T12:38:42Z. Review `b1b80c61` is clean for that narrower scope.
- Later CARD-0749 tasks concerned platform guidance, not a concurrency provider.
  Code `b2059dec` landed as `e6806e723c1260690e92023d82a9b7c241d5560d` (operation
  `c29111c1-eeaa-4d89-bc2e-9b6244067bf7`, confirmed 2026-09-26T20:15:15Z);
  Review `663a02ac` is clean for that guidance. Both landed SHAs are ancestors of this
  TestDesign baseline (`git merge-base --is-ancestor`, both exit 0).
- The defaults diff from `7d6e3fe9` to `2197f3d8` overlaps this plan at exactly
  `server/Application/Settings/DelegationSettings.cs`,
  `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs`,
  `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`,
  `scripts/delegate.ps1`, `docs/antiphon-api.md`, `docs/orchestration-loop.md` and
  `server/Bundles/orchestrator.md`. Its other files were `AGENTS.md`,
  `.claude/skills/antiphon-orchestrator/SKILL.md`,
  `client/src/test/fixtures/contract/pipeline.json`,
  `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs` and
  `tests/Antiphon.Tests/Application/PostLandMutationAdmissionTests.cs`; those stay
  outside CARD-0505's edits. Platform guidance overlaps only the already-listed
  `scripts/delegate.ps1`, `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md`.
- Original **unimplemented behavioral overlap**, now folded into S1-S3 here: audited
  seed-once runtime limits, revisioned GET/PUT/history and provenance, live gate/pipeline/
  refusal readers, and project inheritance. The specific consumer collisions are
  `DelegationOpenGate.cs`, `AgentTaskDispatcher.cs`, `AgentTaskPipelineStatusService.cs`,
  `ConcurrencyLimitException.cs`, `Program.cs`, `AppDbContext.cs`, the migration snapshot,
  and the new DispatchConcurrency files listed below. There is no existing runtime
  concurrency route/store to extend in this baseline. Do not launch a second CARD-0749
  implementation of these behaviors. Its historical `MaxConcurrentTasks` absolute-cap
  wording must not conflate a host budget with `MaxOpenTasks` project admission.
- **Ordering is resolved:** the defaults/guidance prerequisites have landed. Code can
  implement this plan's single store after the other active source collisions clear;
  no additional CARD-0749 land is needed. Caller records the residual runtime scope as
  covered by CARD-0505 on CARD-0749, without treating the defaults-only work as completion
  of the runtime request. Existing host/runner capacity APIs remain CARD-0654's controls.
  CARD-0506 remains the existing UI follow-up; do not create or implement another UI card.

Reproduce the reconciliation with `card.ps1 get CARD-0749 -Board Antiphon`, its history,
the board-scoped task list, and GET of the named tasks (detail uses `summary` plus
`result`/`landing`). These were reads only; this task did not edit cards or settings.

## Design decisions

### D-1. Preserve combined-open behavior; separate queueing is explicit

Use one effective project policy, with a role map. All 14 non-specialist enum roles are
supported; do not invent a second stage enum. Each policy has:

| Field | Meaning and imported default |
|---|---|
| `mode` | `LegacyOpen` by default; `SeparateQueues` is an explicit configuration change. |
| `maxParallel` | Required finite project value, imported from actual bound `MaxOpenTasks` (shipped 6). In LegacyOpen it retains the combined-open meaning. In SeparateQueues it bounds Dispatched/Working. |
| `maxQueued` | Optional project queued-admission bound, default null (no independent queue bound). |
| `roles[role].maxParallel` | Imported bound RecommendedInFlight, including null for unbounded; shipped Code/Review 2, others 1, Custom null. Meaning follows mode. |
| `roles[role].maxQueued` | Optional queued-admission bound for that role, default null. |

The compatibility mode is necessary: today's default refuses a second Plan even if the
first is only Queued. Automatically turning that into an accepted waiting Plan changes live
queue behavior. A policy read labels LegacyOpen caps as **open task limits**; the UI must
not label those as running-only limits. Mode is resolved once per project, not per role.

Count definitions, always scoped by `AgentTask.ProjectId` and excluding specialists:

- `open = Queued + Dispatched + Working`, exactly the existing admission predicate.
- `parallel = Dispatched + Working`, including retained capacity-wait tasks. They retain
  an accepted project task slot even while they release a host seat. This deliberately
  differs from the host-budget predicate and avoids claiming that provider recovery frees
  project WIP. A retained return does not acquire a second project slot.
- `queued = Queued`, including pin dates, model/host/repository holds and remote preparation.
- Blocked and terminal tasks consume neither admission population. Pipeline `ready` rows,
  backlog cards and session message queues are not dispatched AgentTask queue entries.

Fresh create in LegacyOpen preserves the existing open-cap checks and optionally checks
configured queued bounds. Fresh create in SeparateQueues checks queue bounds, then returns
201/Queued. Parallel saturation is a **hold at dispatch**, not a failed accepted task.
If a queue bound is reached, reject the next fresh create with 409 and insert nothing.
There is no second overflow queue and no background automatic retry of refused creates.

Every create passes through Queued, even if the next tick can launch it. Thus `maxQueued=0`
means pause fresh admission, not a synchronous/no-buffer dispatch mode. Positive queue
limits count that transient row. Null is explicitly unbounded on that axis; overall
parallel remains finite. Existing specialized producers, live follow-ups and recovery of
already accepted tasks retain their admission exemptions. Their queued rows still count
when considering later ordinary creates. Recovery may therefore put occupancy over the
configured queue bound; report that overage and refuse additional ordinary creates until
it drains. Do not fail or strand previously accepted work to force an exact cardinality.

Example: project SeparateQueues `maxParallel=6,maxQueued=12`, Code `maxParallel=2,maxQueued=3`.
Two working Code tasks may have three accepted queued Code successors. A fourth queued
successor is refused. A queued Code dispatches when the Code count drops below two and
project count below six, subject to all existing host, scope, routing and provider gates.
An unrelated Plan can dispatch if its own and project slots are free.

### D-2. Durable settings use the existing revisioned-store pattern

Add concrete `DispatchConcurrencySettingsService`, pure `DispatchConcurrencyPolicy`, typed
DTOs and two entities: `DispatchConcurrencySettings` and `DispatchConcurrencyRevision`.
Follow RunnerDefaultSettingsService rather than writing appsettings/env files or repurposing
runner placement/HostBudgets tables. No new process-wide mutable static state or generic
repository/settings framework.

- Settings row key: `global` or the canonical project GUID string; nullable `ProjectId`
  identifies project scope. Unique scope key; verify project existence on project writes.
  Store schema-versioned `OverridesJson` as jsonb, monotonically increasing Revision,
  UpdatedAt, LastReason, LastProvenance and LastCallerTaskId. Revision is an EF concurrency
  token. The global row additionally stores immutable `SeedJson`.
- History: unique `(SettingsId,Revision)`, previous revision, complete scope snapshot,
  timestamp, reason, provenance and caller task ID, matching runner-defaults history.
  Preserve history when overrides are cleared; keep an empty settings row/tombstone so
  revisions cannot reset to zero and permit an ABA stale write. Project deletion may leave
  audit rows keyed by historical project ID; a settings row is not project ownership.
- First initialization copies **the deployed bound settings**, not newly constructed
  shipped defaults, under an advisory transaction lock. Save seed plus initial revision
  atomically, `provenance=Migration`, reason naming the imported settings. Initialize on
  startup before dispatch consumers, with an idempotent service fallback for test hosts.
  Initialization must finish before either admission gate; a gate never initializes the
  store while holding the parallel key and thereby acquires locks in reverse order.
  Pipeline GET remains a read-only projection. A race initializes once.
- After initialization, startup config is seed history, not a live override. An explicit
  clear restores the persisted seed (or inherited global value), not a fresh import on
  restart. Import preserves operator-set Code/Plan/Review values and explicit null roles.
- CLI-generated additive migration creates only these tables/indexes; no AgentTask/status
  rewrite, queue backfill, mass data seed or runner migration. Register through Program.

Resolution is **project field override > global field override > imported default**. Store
only explicitly supplied override fields; absence inherits, while explicit null means
unbounded for nullable limit fields. Empty `roles.Code` inherits both fields. Empty
`overrides:{}` clears the whole scope. Role-specific null does not disable the finite
project bound. `mode:null` and project-wide `maxParallel:null` are invalid overrides.

Return raw overrides, inherited values, effective values and per-field `source`:
`default`, `global`, `project`. Imported defaults additionally identify `seedOrigin:
startupConfiguration`, import time and seed values; do not pretend these were necessarily
the shipped defaults. Include both globalRevision and projectRevision in every effective
snapshot. Project revision is zero while no project row has ever been written.

No cache is needed initially. Resolve a consistent global+project snapshot with bounded
queries; a GET uses a read-only snapshot transaction or one composed query. Gate decisions
re-read the policy inside their admission transaction. Settings initialization/writes take
the existing create-admission advisory key and then the new parallel-admission key (D-4),
making the before/after relationship to a PUT definite for both kinds of admission.
Do not automatically retry ambiguous writes or broaden read-resilience policies.

### D-3. Validation, provenance and HTTP contract

Add these routes, keeping settings outside the task-create request:

| Method and route | Contract |
|---|---|
| `GET /api/dispatch-concurrency` | Global seed, overrides, effective policy, supported roles, validation ranges, revision and audit metadata. Also the policy inherited by the null-project bucket. |
| `PUT /api/dispatch-concurrency` | Complete replacement of the global **override object**; requires expectedRevision, overrides, reason, provenance. |
| `GET /api/projects/{projectId}/dispatch-concurrency` | Project raw overrides, inherited global policy and field-resolved effective policy, both revisions and audit metadata. Unknown project is 404. |
| `PUT /api/projects/{projectId}/dispatch-concurrency` | Complete project override replacement, requiring expectedRevision and expectedGlobalRevision, overrides, reason, provenance. An empty override object is revert-to-default. |
| `GET /api/dispatch-concurrency/revisions` and `GET /api/projects/{projectId}/dispatch-concurrency/revisions` | Descending revisions, beforeRevision cursor, limit default 50/max 100, nextBeforeRevision. Reads write nothing. |

Example project write, after reading both revisions (numbers are illustrative):

```json
{
  "expectedRevision": 0,
  "expectedGlobalRevision": 1,
  "overrides": {
    "mode": "SeparateQueues",
    "maxParallel": 6,
    "maxQueued": 12,
    "roles": { "Code": { "maxParallel": 2, "maxQueued": 3 } }
  },
  "reason": "Operator requested two Code workers with three waiting successors.",
  "provenance": "Human"
}
```

Validation is before mutation, returns the HttpException/Problem Details convention:

- Integral parallel limits 1..512, role null allowed; queued limits 0..4096 or null.
  Reject unknown/duplicate roles, specialists, numeric enum values, unknown modes,
  unknown fields, wrong JSON types and malformed bodies. Caps above a parent cap are legal
  but the effective combined bound is the minimum; expose both, never silently clamp.
- Require the outer fields explicitly, following `RunnerDefaultsPut.ReadAsync`
  (`server/Api/Endpoints/RunnerDefaultEndpoints.cs:44`). Missing `overrides` cannot clear
  anything. Absence *inside* that required object has the documented inheritance meaning.
- Trimmed reason 1..400 characters; Human or Auto provenance only. Migration is internal.
  Caller task ID is resolved server-side with the existing settings endpoint attribution
  pattern, not accepted from the body. Human means an actual operator instruction;
  orchestration ticks do not write limits or manufacture that provenance.
- Reject stale scope/global revisions with 409 `dispatch_concurrency_revision_conflict`,
  returning current revision metadata and writing no history/event. Serialize initialization
  and concurrent updates; still retain the EF token/unique revision index as backstops.
- Auto cannot replace or clear a Human scope, or shadow an inherited Human global choice
  through a new project override; return 409 `dispatch_concurrency_human`. A Human project
  override may intentionally differ from a Human global default. Inheritance means a later
  Human global edit may affect fields the project has left inherited.
- Exact semantic no-op keeps the revision/history unchanged. Explicitly claiming an
  existing Auto/default choice as Human is an audit/provenance change, not a no-op.
- Preserve any unchanged out-of-range legacy seed on import; apply the new bounds to
  newly written values. Do not make migration silently clamp an existing deployment.

Return the committed snapshot, including occupancy and whether existing work exceeds new
bounds, plus publish `DispatchConcurrencyChanged` through IEventBus after commit. Failure
to deliver the event does not roll back a saved revision; GET is authoritative. There is
no runner RPC in a settings write and no implicit placement/routing-pin update.

Provide ASCII-only `scripts/dispatch-concurrency.ps1 get|set|clear|history`, with optional
`-Project <guid>`, `-Json`, `-SettingsFile`, `-ReasonFile`/`-Reason`, `-Provenance Human|Auto`.
Like routing-pin.ps1, use ANTIPHON_API and token headers without logging secrets. `set`
reads revisions then sends exactly the supplied override object; `clear` sends `{}` with
the same audit fields. Permit explicit expected revisions for reviewable automation.
Surface a stale-revision 409; never fetch-and-overwrite on behalf of a failed write.

### D-4. Admission, dispatch and concurrency races

Extend DelegationOpenGate to use the effective policy and return structured decisions from
the shared pure policy. Keep existing project/null bucket semantics, specialist exclusions,
live-follow-up admission exemption and absolute-before-role precedence.

In LegacyOpen, `IgnoreConcurrencyLimit` keeps its current one-request bypass of open-cap
checks. **New queued bounds are not bypassed** by that flag. In SeparateQueues the flag
does not lift queue or running limits; a human changes the audited policy for those.
An old request cannot persist a hidden bypass into future dispatches. Document this on the
DTO, script and error. No existing configured behavior changes until a new bound/mode is set.

For SeparateQueues, add a project/role check to **every Queued -> Dispatched path**. The
outer tick may cheaply skip obviously full scopes and trace a Held event, but the final
check is transactional and authoritative. Cold spawn, warm reuse and standing delivery
have status assignments at `AgentTaskDispatcher.cs:4883`, `:6661`, `:6867`; all need the
same check before assignment/save and before any prompt or launch. Specialist paths skip
it structurally. Live non-specialist follow-ups still skip create admission, but consume
a running slot at dispatch, just as they already wait for host/agent eligibility.

Use the existing task claim plus a dedicated PostgreSQL transaction advisory key,
`antiphon.delegation.parallel-tasks`; do not implement a process-local semaphore or trust
a tick-start count. Count committed occupants excluding the candidate, evaluate current
settings, then commit its Dispatched state under the same transaction lock.
Two different queued tasks contending for the last slot must yield one admission. The
lock must be taken late, after worktree/mirror preparation, and held only across the
final count/state-save/commit. It must not surround git, provider probes, runner RPCs,
prompt delivery or retries. Already prepared resources remain owned by a Queued task
when this last check holds it; normal retirement/retry rules retain that custody.

Lock order for the new final-dispatch path is the existing repository lease/task-row
claim, then the parallel key, then settings reads and final persistence. Settings PUT
takes the existing create key, then the parallel key, and touches only settings/history
rows. Dispatch never takes the create key. This separation matters: the current create
transaction does base-preview Git reads and an Interim owner-row latch after its gate
(`AgentTaskService.cs:1475`, `:1492`, `:1510`); naively sharing that key with a dispatcher
already holding a task row would add a lock-order inversion. Preserve that existing create
boundary rather than expanding this card into workspace admission redesign. No code may
take the create key while holding the parallel key, or acquire further owner/repository
locks after the parallel key. Take the parallel key even for a LegacyOpen decision, so
a concurrent mode switch cannot pass through a stale no-gate path. Dispatch decreases
Queued and increases parallel; it need not take the create key to free a queue slot.
TestDesign must pin the interleaving with create, PUT and all three dispatch paths; if
an existing nested helper violates the new late-lock boundary, refactor only that
boundary and amend the declared footprint before Code.

On a hold, roll back tentative task/agent/session database changes, preserve the Queued
row, and use the existing deduplicated `TraceHeldAsync`/HeldAged path. No Failed/Blocked
transition, kill, new stage or new alert sink. The next tick uses the newest policy.
Release/terminal transitions free capacity through durable task status; a restart
recounts the database, with no in-memory reservations to leak. Retained capacity waits
stay counted as project WIP; their existing host-return admission is unchanged.

Requeue, routing-blocked resumption and recovery of accepted tasks may exceed a new queue
bound without losing their work. Their next Queued -> Dispatched attempt goes through
the running gate. Explicitly test this distinction: queue size is a **fresh-admission
bound**, not permission to cancel recovery. Lowering parallel limits holds future starts
until occupancy falls; it never stops existing Dispatched/Working tasks. Lowering a
queue limit preserves every accepted row, and blocks new ordinary admissions.

### D-5. Diagnostics and orchestration policy

Keep 409 code `concurrency_limit` and existing `axis: absolute|role`, count, limit, project,
role, open occupants and override fields. Add `population: open|queued`, mode, per-field
source, global/project revisions, `canOverride`, all exceeded constraints, actual total
and omitted-occupant count. Absolute wins within a population; non-overridable queue
failure wins over overridable legacy-open failure. List only matching project/role
occupants, ordered by CreatedAt then ID, cap 12. Legacy `open` remains the compatible
occupant-list field; population identifies what it contains. Text says "queued" when
appropriate and does not recommend a flag that cannot help. Keep `override` as the
legacy flag name for wire compatibility; `canOverride=false` is authoritative.

Parallel-full SeparateQueues tasks have a structured Held detail with population=parallel,
the same effective limits/revisions/sources and matching holder IDs. Reuse its projection
for pipeline queue reasons `projectParallelLimit` and `roleParallelLimit`, without masking
an already-dominant lease, sibling-land or routing-date reason. Report counts above a
lowered limit as over-limit, not an inconsistent negative remaining capacity.

Extend the pipeline additively:

- No-query request retains the existing fleet arrays and legacy advisory fields. Add
  `concurrencyScopes` for represented projects and the null bucket, each with its
  effective policy/sources/revisions and open/parallel/queued counts and remaining slots.
  Legacy `recommendedInFlight` uses the effective global role value for compatibility;
  never compare fleet totals to a project limit and call that project admission evidence.
- Add mutually exclusive `?projectId=<guid>` and `?unscoped=true`; filter task, ready and
  backlog data consistently. Unknown project is 404, invalid/conflicting scope is 422.
  A scoped read includes its policy even with no tasks. Per-stage effective limits and
  `atLimit` use that project's policy. Host summaries stay fleet-wide and explicitly
  labelled so project filtering cannot suggest extra physical seats.
- The new policy snapshots carry enforcement/mode metadata. Retain
  `recommendationsAreAdvisory` for the old recommendation presentation, not as an assertion
  that no gates exist. Read-only GET never changes task state, clears pins or seeds settings.

Orchestrators use the scoped policy and queue populations before commissioning work.
The documented standing Code/Review=2, other stages=1 and Code-feed=2 policy remains the
default operator instruction; an API ceiling is not permission to spend up to it. Explicit
operator instructions may choose another desired concurrency, bounded by the effective
server limits. Existing same-source-area deferral always applies. Preserve the standing
absolute-axis override rule, additionally requiring `population=open` and `canOverride=true`;
a same-stage collision or queue-full refusal never justifies IgnoreConcurrencyLimit.
No new feed-depth setting and no automatic dispatch of pipeline `ready` rows in this card.

### D-6. UI contract and separate work

CARD-0506 can build global/project forms directly from the read DTO: supported roles,
ranges, seed, inherited/effective values, sources, mode, revisions, audit reason and live
occupancy. It must distinguish omitted/inherited, explicit unbounded null and queue pause
zero; show LegacyOpen's combined count; preview that lowering holds new work; handle 409
by reloading for review. Revert uses a PUT with an empty override object, never copied
global values. SignalR invalidates global and affected project settings/pipeline queries.
Its form supplies Human provenance only for the user's edit. Client hooks, UI components,
client builds and browser tests belong to CARD-0506, not this backend plan.

## Slices and exact footprint

This Plan task changes only this Markdown file. Implementation paths below are the
collision contract; do not broaden them silently. New paths are marked **new**.

**S1 — settings, inheritance and audited API.**

- **new** `server/Domain/Entities/DispatchConcurrencySettings.cs` (settings and revision entities).
- **new** `server/Application/Dtos/DispatchConcurrencyDtos.cs`.
- **new** `server/Application/Services/DispatchConcurrencyPolicy.cs`.
- **new** `server/Application/Services/DispatchConcurrencySettingsService.cs`.
- **new** `server/Api/Endpoints/DispatchConcurrencyEndpoints.cs`.
- `server/Infrastructure/Data/AppDbContext.cs`, `server/Program.cs`.
- CLI-generated `server/Migrations/<timestamp>_AddDispatchConcurrencySettings.cs`, its
  `.Designer.cs`, and `server/Migrations/AppDbContextModelSnapshot.cs`. The timestamp is
  the sole not-yet-known filename; reserve this one migration pair, not the whole folder.
- **new** `tests/Antiphon.Tests/Application/DispatchConcurrencyPolicyTests.cs`,
  `DispatchConcurrencySettingsTests.cs`, `DispatchConcurrencyWireTests.cs`,
  `DispatchConcurrencyMigrationTests.cs` in that same directory.
- **new** `tests/Antiphon.Tests/TestHelpers/DispatchConcurrencyTestHost.cs`.

**S2 — gate and dispatch semantics.**

- `server/Application/Services/DelegationOpenGate.cs`, `AgentTaskService.cs`,
  `AgentTaskDispatcher.cs`, `DispatchHoldDetails.cs` in the same Services directory.
- `server/Application/Exceptions/ConcurrencyLimitException.cs`.
- `server/Application/Dtos/AgentTaskDtos.cs` and `server/Application/Settings/DelegationSettings.cs`
  (compatibility/seed and override documentation; retain old binding keys).
- **new** `tests/Antiphon.Tests/Application/DispatchConcurrencyAdmissionTests.cs`,
  `DispatchConcurrencyDispatchTests.cs`.
- `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs` (provider wiring,
  retain existing assertions), `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`
  and the new test host from S1 (shared registration; avoid copied DI graphs).

**S3 — projection, script, documentation and handoff.**

- `server/Application/Services/AgentTaskPipelineStatusService.cs`,
  `server/Application/Dtos/AgentTaskPipelineDtos.cs`, `server/Api/Endpoints/AgentTaskEndpoints.cs`.
- **new** `scripts/dispatch-concurrency.ps1`; `scripts/delegate.ps1` (help/refusal guidance only).
- **new** `tests/Antiphon.Tests/Application/DispatchConcurrencyPipelineTests.cs`,
  **new** `tests/Antiphon.Tests/Scripts/DispatchConcurrencyScriptTests.cs`.
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` (provider wiring only).
- `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/orchestration-loop.md`,
  `docs/session-runtime-invariants.md`, `server/Bundles/orchestrator.md`.
- This plan receives final TestDesign roster/counts and any justified footprint amendment.

No `src/Antiphon.SessionRunner` or shared runner protocol changes, no UI, host-capacity
implementation, routing-pin store, provider credentials/readiness, or deployment scripts.
The board-scoped live task read on 2026-09-30 showed CARD-0849 Code `02b47ebb` and
CARD-0778 Code `386a95bf` Dispatched, and CARD-0719 Review `1c31680d` Dispatched.
CARD-0849's exact plan footprint (both server2 runner Compose files; c590-remote,
c590-real, verify-docker-stack, deploy-server2, test-deploy-server2, both c727 fixture
scripts, verify-card0849-caches; DockerStackContractTests, RemoteScriptContractTests;
docs/docker-stack.md and docs/bootstrap.md) has **zero intersection** with S1-S3.
Do not add deployment/cache changes here. CARD-0719 intersects at `docs/ops-http.md`;
land that reviewed work before editing the same documentation. CARD-0778 intersects
at `docs/session-runtime-invariants.md` (its actual readiness implementation is in
RunnerGrokAdapter/FakeGrok and separate tests). Under the standing same-area rule,
defer CARD-0505 Code until CARD-0778 lands; preserve both invariant sections when composing
S3. Re-read the scoped task list at Code dispatch: a new owner of AgentTaskDispatcher,
Program/AppDbContext, the migration snapshot or any listed shared doc also defers Code.
The CARD-0749 defaults/guidance dependency is already landed, as reconciled above.

## Rollout and risks

1. Before deployment record the running SHA, scoped queue/parallel census, configured
   MaxOpenTasks/role values through the authorized configuration owner, host budgets and
   runner-default revision. In particular preserve the operator's live role overrides;
   the snapshot above is not deployment input. Reconcile CARD-0749 first.
2. Apply the additive CLI-generated migration through normal server startup/deployment;
   import config atomically before dispatcher consumers run. Test populated pre-upgrade
   schemas, concurrent initialization and failure rollback. Do not seed deployment values
   in migration SQL or run a down migration on the live DB.
3. **Server deployment/restart only; no runner redeploy is required.** This is server-only
   enforcement with unchanged runner protocol. Use the canonical main-checkout restart
   runbook, not a worktree restart, and verify `/api/version` equals the activated source
   SHA plus `land-v2`. Health alone is insufficient. This Plan task performs no restart.
4. Verify effective imported values and LegacyOpen behavior, including a populated queue,
   with no limits PUT. Controlled acceptance uses an isolated test project and fake runner;
   do not manufacture production delegates to exercise a refusal.
5. A Human PUT may opt one intended project into SeparateQueues with explicit queue budgets.
   Inspect its first holds/admissions and 409 diagnostics, then expand deliberately. Future
   settings edits need no restart. Do not opt all projects in as a migration side effect.
6. Operational rollback is an audited PUT to the recorded policy/mode. Existing work is
   retained; an overfull queue drains. A binary rollback keeps the additive tables but the
   old server ignores them and uses startup config, so reconcile those values first and
   expect its historical admission semantics. No runner rollback or DB deletion.

Material risks: loosening admission can cause a burst on the next tick; lowering limits
can prolong accepted queues; null queue limits permit unbounded waiting after explicit
SeparateQueues opt-in; retained waits continue consuming project WIP. Global changes can
affect many inheriting projects. Cross-process count/claim races must be tested, not assumed
solved by the existing per-task lock. A late failed admission can retain prepared workspace
resources; it must preserve ownership and produce a hold. Schema/index locks, seed races,
revision conflicts and live config/server SHA drift are deployment concerns. Scope the
new parallel advisory critical section to DB admission so it adds no Git/remote wait to
settings writes. The existing create critical section's base-preview duration is retained
and should be measured if settings PUT latency is high. No limit reduction grants
kill/release authority.

## Verification design

No builds/tests were run in Plan or TestDesign; the census and manifest checks below are
static source evidence, not a TRX or a claim that the proposed tests already pass.
The following fixtures, case bodies and interleavings are the Code authoring contract.
Do not replace outcome assertions with copied formulas or settings-only assertions
that never exercise the real gate.

### Inspection

| Bodies read | Boundary covered |
|---|---|
| DelegationOpenGate, AgentTaskService create transaction, ConcurrencyLimitException, DelegationSettings/role enum; all AgentTaskConcurrencyLimitTests | V-1/V-5/R-2; project/null/specialist/follow-up predicates and insert atomicity. Current regression fixture uses ScratchGitRepo, so the old CARD-0749 report's plain-directory failure is already repaired here. |
| RunnerDefaultSettingsService/Endpoints, RunnerDefaultTests (including DefaultsHost, World and ThrowingBus), AppDbContext settings mapping | V-2/V-3/V-4/R-1; first seed, revision/audit, wire parsing, migration and restart patterns. Do not copy its lazy GET initialization into the pipeline. |
| AgentTaskDispatcher DispatchOneAsync, TryReuseWarmAgentAsync, PlaceOnStandingAgentAsync, DeliverReuseMessagesAsync; DispatchHoldVisibilityTests and HostBudgetAdmissionTests | V-6/R-3; three status writers, early preparation, final claim, commit-before-delivery, held deduplication and retained-host semantics. |
| DelegationTestServices, TestDbFixture/IsolatedTestSchema, AntiphonWebAppFactory, ModelAvailabilityDispatcherTests, BridgeQueueHarness, SessionQueueTranscriptPump, ScriptedSessionRunnerClient, ScratchGitRepo | Harness registration, database custody, safe fake clients and transcript evidence. SessionQueueTranscriptPump uses the shared default store: do not reuse it unchanged for an isolated clone. |
| Pipeline service/DTO/endpoint; all three pipeline partial files and separate endpoint class; HostBudgetServiceTests, dispatcher predicate and standing-policy docs tests | V-7/R-3/R-4/R-5; fleet/project distinction and exact census. |
| RoutingPinScriptTests/its StubApi and process runner; routing-pin.ps1 and delegate.ps1; checkpoint PlanTableImporter | V-8; HTTP recording through real pwsh, no retry, exact escaped-pipe manifest shape. |

### Named fixtures and file ownership

All **new** helpers below live in the already reserved
`tests/Antiphon.Tests/TestHelpers/DispatchConcurrencyTestHost.cs`; keep script-only helpers
nested in `DispatchConcurrencyScriptTests.cs`. No additional helper file is implicit.

| Fixture | Concrete setup and lifetime |
|---|---|
| F-P `ConcurrencyPolicyCases` | Immutable inputs to the real DispatchConcurrencyPolicy. Explicit literal expected values and decisions; never another implementation of resolution/counting. All 14 ordinary roles and three specialist roles are enumerated from AgentTaskRole and asserted against the explicit supported-role set. |
| F-S `DispatchConcurrencyTestHost` | Owns one `IsolatedTestSchema` from TestDbFixture (despite the name it is a **cloned database**, not a shared SearchPath schema), FakeTimeProvider fixed at 2026-09-30T12:00Z, recording event bus, projects P/Q, stable ordered GUIDs, and distinct contexts per caller. Seed configuration: MaxOpenTasks=9, MaxConcurrentTasks=2, Code=5, Review=4, Plan=3, Custom=null; explicit mode LegacyOpen and null queued limits after import. Host capacity is deliberately independent. Initialize explicitly before consumers. Dispose scopes, host, fake runtime and database in that order. |
| F-H `ConcurrencyHttpHost` | Loopback Kestrel port 0, production exception middleware and real settings/project routes, JSON enum configuration, real AgentTaskService and production task/pipeline route map with its dependencies registered. Uses F-S store/services, a deterministic Caller resolved through the actual attribution path, no Hangfire dispatch loop. GET/PUT response parsing is separate from persisted DB readback. The existing AntiphonWebAppFactory/ProductionRunnerGuard remains the R-4 full-Program smoke; never configure a real runner URL. |
| F-D `ConcurrencyDispatchWorld` | F-S plus real AgentTaskDispatcher, AgentSessionRuntime and SessionMessageQueueService; `AddDelegationWorktrees` through DelegationTestServices. Recording fake launch adapter/client and directory have local/runner-a/runner-b with ample seats (32), valid profiles/auth and controllable readiness. Use explicit ReadOnly for fresh create cases; explicit Shared plus real idle sessions for warm/standing dispatch; prepared Worktree+RemoteWorktreePath for mirror cases. Temp paths are owned directories; no native provider or real Git needed for these cases. Scope/pin/date/provider gates are initially clear. Record launches, queue inserts, submits, kills, session rows and workspace ownership separately. |
| F-R `ConcurrencyRaceProbe` | Per-context DbCommandInterceptor, SaveChangesInterceptor and DbTransactionInterceptor, plus a separate Npgsql observer connection. Records backend PID, transaction ID, advisory-key attempts, candidate row claim, final write-entry, commit and rollback. Awaitable gates use RunContinuationsAsynchronously; only the test's chosen operation is paused. Observe `pg_blocking_pids`/`pg_locks` for the specific PID and key, or a competing write-entry signal; do not infer blocking from elapsed time. Cleanup releases every barrier in finally, cancels/awaits workers, and rolls back observer transactions. No process-wide lock or static fixture state. |
| F-M `ConcurrencyMigrationWorld` | Own F-S clone, rewind only that clone through IMigrator to the migration immediately before AddDispatchConcurrencySettings, seed old task/project/session/host-budget/default-routing rows, migrate forward and compare full persisted snapshots. A transaction interceptor throws once before the initial import commit to model interruption. No manual SQL substitute for the actual new migration. |
| F-Q `ConcurrencyRecipient` | FakeAgentProtocolAdapter.OnSubmitted persists exactly the submitted body as a timestamped UserPrompt in the **same cloned DB**, then controlled TurnEnd, following BridgeQueueHarness. Register it in the real runtime/queue. Busy/idle is driven by transcript rows and the fake clock; queue processing and confirmation are real. Recording client launch is a substitute for the provider process, not native readiness/delivery evidence. |
| F-X `ConcurrencyScriptRecorder` and `ConcurrencyScriptProcess` | Loopback HTTP recorder answers ordered GET/PUT/history fixtures. Real `pwsh -NoProfile -File scripts/dispatch-concurrency.ps1` with ArgumentList, owned temp settings/reason files, fake task-token sentinel, isolated ANTIPHON_API, captured stdout/stderr/exit. Clear inherited capability/token credentials before supplying the sentinel; assert it is absent from output. Always stop/await child and listener. Class carries assembly-local ParallelLimiter<ProcessSpawnLimit>. |

F-S readbacks use new contexts/AsNoTracking; a tracked entity echo is not persistence proof.
Use fresh stores per internal scenario where counts/revisions reset. Negative wire/settings
cases compare settings, history, task/event and host/routing row snapshots before/after;
fixture import is outside that comparison. The wire host must explicitly finish startup
import before serving even its first GET. F-R selects operations by context/candidate ID,
not a brittle nth SQL command. Match EF entity states at SaveChanges for insert/claim pauses.

**Narrow S2 seam amendment, within the existing footprint:** add an internal two-argument
DispatchOneAsync overload that forwards to the existing private three-argument method
with null siblingObservation; make DispatchOneResult internal. Keep the private
SiblingBaseGuard/UnlandedSibling types private. F-R can then call the real final transaction
on two distinct candidate IDs. This avoids both ticks selecting the same FIFO task and
testing only its row lock. Ordinary V-6 cases still use TickAsync. For the stale-tick scenario, pause the
candidate's existing `FOR UPDATE` through F-R before execution, after the outer tick read.
No alternate admission implementation and no production test-only gate is introduced.

**Late-lock boundary:** warm/standing helpers currently set Dispatched inside their own
branches, and warm reuse mutates prior token ownership. Extract their eligibility/read
phase from final assignment; acquire any prior-task/owner row locks before the parallel
key, then apply status/token/agent changes and save under that key. Cold verification
reservation, optional expiry and workspace preparation also precede the key. Move
irreversible RawTokens removal until the decision has committed. A held decision must
restore agent idle fields, prior token ownership and session/task bindings as well as
Queued status. F-R asserts the observed order and F-D compares these fields after holds.
No new owner/repository lock or external I/O is allowed after the parallel key and before
commit. All of this stays in AgentTaskDispatcher.cs and the named helper file.

### Static census at the source baseline

TestDesign independently repeated the source census at `c45c4ba2`: **123 methods /
146 expanded results**, exactly matching the Plan. Enumerated every `.cs` path under
`tests/Antiphon.Tests` with `rg --files`, selected containing top-level class names,
counted `[Test]` methods and `max(1, Arguments-count)`, then inspected the selected
attributes/bodies for generators and platform skips. Partial-file contributions:
DispatchHoldVisibilityTests 14/17 + HostBudgetAdmissionTests 9/11 = 23/28;
AgentTaskPipelineStatusTests 26/35 + AgentTaskPipelineStatusC557Tests 19/19 +
HostBudgetPipelineTests 4/4 = 49/58. The endpoint class contributes its own 4/4.
RunnerDefaultTests contains other classes deliberately outside CP-1. There are no
method data sources/matrices or OS skips in this selected roster. Shared constructor
`ClassDataSource<AntiphonWebAppFactory>` does not multiply the endpoint tests.

Census walked test attributes and containing class names in `tests/Antiphon.Tests`,
expanded `[Arguments]`, and merged partial classes. None of these selected methods uses
a method data source/matrix. Constructor fixture injection is not argument multiplication.
Existing exact selected class totals:

| Class | Methods | Expanded results | Source evidence |
|---|---:|---:|---|
| AgentTaskConcurrencyLimitTests | 24 | 25 | `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs:25`; only zero/negative validation has two arguments (`:35`). The three-open fixtures explicitly set MaxOpenTasks=3 at `:669`; do not misread their names as shipped defaults. |
| AgentTaskDispatcherPredicateTests | 1 | 8 | `tests/Antiphon.Tests/Application/AgentTaskDispatcherPredicateTests.cs:17`. |
| DispatchHoldVisibilityTests | 23 | 28 | `tests/Antiphon.Tests/Application/DispatchHoldVisibilityTests.cs` plus `HostBudgetAdmissionTests.cs`; the latter contributes 9 methods/11 results, not a class named HostBudgetAdmissionTests. |
| HostBudgetServiceTests | 7 | 8 | `tests/Antiphon.Tests/Application/HostBudgetServiceTests.cs:16`; two invalid-budget arguments. |
| AgentTaskPipelineStatusTests | 49 | 58 | Three partial files: `AgentTaskPipelineStatusTests.cs`, `AgentTaskPipelineStatusC557Tests.cs`, `HostBudgetPipelineTests.cs` under `tests/Antiphon.Tests/Application/`. Expanded cases: landing evidence x6 (`:27`), specialist roles x3 (`:295`), non-stage handoffs x3 (`:417`) in the first file. |
| AgentTaskPipelineEndpointTests | 4 | 4 | A second class in `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:946`. |
| RunnerDefaultSettingsTests | 6 | 6 | `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs:213`. |
| RunnerDefaultMigrationTests | 5 | 5 | `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs:520`. |
| StandingPipelinePolicyDocumentationTests | 4 | 4 | `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs:31`. |

Total existing selected coverage: **123 methods, 146 expanded results**. Checkpoint
baseline subtotals are CP-1 **11**, CP-2 **69**, CP-3 **66**. Reproduce with
`rg -n '\[Test\]|\[Arguments\(|class ' <the files above>`, counting class membership and
partial files, not filenames; inspect source for generators before counting. No
`--list-tests` invocation is execution evidence.

### New test roster and required assertions

The names below are the planned TestDesign contract: **54 new unparameterized tests**,
not claimed existing tests. Each item separated by a semicolon is one method. TestDesign
must implement these names or update the exact manifest/expanded arithmetic before Code.
Group IDs map coverage and positive controls; the fixed count is not an excuse to omit
edge-case assertions within a method. Unit policy tests use production policy; persistence
and gate tests use isolated PostgreSQL schemas, not EF InMemory.

| Coverage | New class (count) | Methods and outcomes |
|---|---|---|
| V-1 | DispatchConcurrencyPolicyTests (8, Unit) | `Legacy_counts_queued_as_open`; `Separate_counts_parallel_and_queue_independently`; `Project_then_global_then_seed`; `Missing_inherits_null_is_unbounded_zero_pauses_queue`; `Queue_failure_precedes_legacy_override`; `Absolute_precedes_role_and_reports_all_failures`; `Status_and_specialist_populations_match_contract`; `Validate_modes_roles_fields_and_bounds`. Assert exact decisions, resolved sources and validation failures, including role/overall boundary +/-1, duplicate/numeric role keys and explicit null. |
| V-2 | DispatchConcurrencySettingsTests (10, Integration) | `Import_preserves_bound_values_once`; `Concurrent_initialization_writes_one_seed`; `Global_put_changes_existing_service_reads`; `Project_override_isolated_and_clear_restores_inheritance`; `Stale_scope_or_global_revision_writes_nothing`; `Concurrent_puts_have_one_winner`; `Auto_cannot_replace_or_shadow_human`; `Noop_preserves_revision_human_claim_audits`; `History_paginates_and_survives_clear`; `Restart_and_event_failure_preserve_committed_policy`. Assert DB snapshots/history and subsequent real reads, not DTO self-comparison. |
| V-3 | DispatchConcurrencyWireTests (6, Integration) | `Global_get_put_roundtrip`; `Project_get_put_clear_roundtrip`; `Missing_fields_invalid_body_and_unknown_project_write_nothing`; `Revision_conflict_returns_current_revisions`; `Human_reason_and_server_caller_are_audited`; `Revision_routes_are_readonly_and_bounded`. Use a real loopback endpoint host with exception middleware; assert status/body, sources, DB non-mutation and attribution. |
| V-4 | DispatchConcurrencyMigrationTests (3, Integration) | `Upgrade_populated_database_preserves_task_rows`; `Initialize_from_nondefault_config_then_restart`; `Interrupted_initialization_rolls_back_seed_and_history`. Migrate from the immediately preceding migration in an isolated schema; verify unique scope/revision constraints and task/state equality. No live DB migration test. |
| V-5 | DispatchConcurrencyAdmissionTests (8, Integration) | `Unconfigured_create_keeps_legacy_decisions`; `Global_and_project_put_change_real_create`; `Project_and_role_queue_boundaries_refuse_without_insert`; `Other_project_and_null_bucket_do_not_interfere`; `Specialists_and_live_followups_keep_admission_exemptions`; `Override_only_bypasses_legacy_open`; `Concurrent_last_queue_slot_has_one_winner`; `Problem_names_population_sources_revisions_and_bounded_occupants`. Exercise POST/service CreateAsync with non-default values, expired/live follow-up contrast, Blocked/terminal contrast, absolute+role saturation and more than 12 matching occupants. |
| V-6 | DispatchConcurrencyDispatchTests (8, Integration) | `Parallel_cap_holds_then_next_tick_releases`; `Project_cap_spans_local_and_remote_hosts`; `All_launch_paths_obey_role_cap`; `Competing_claims_admit_only_one_task`; `Put_racing_claim_observes_one_complete_policy`; `Lowering_never_kills_or_discards_accepted_work`; `Restart_recounts_without_leaked_slots`; `Recovery_and_retained_wait_preserve_owned_slots`. Use real dispatcher and isolated DB with fake launches; assert zero runner/prompt calls for refused claims, exact task/session state, deduplicated held reason, durable revision and eventual next-tick progress. Cold, warm, standing, rollback/cancel and prepared-mirror cases are mandatory within the named methods. |
| V-7 | DispatchConcurrencyPipelineTests (5, Integration) | `Scoped_pipeline_matches_create_and_dispatch_policy`; `Empty_project_and_unscoped_have_correct_limits`; `Fleet_contract_and_host_totals_remain_distinct`; `Queued_hold_and_overage_match_gate_without_writes`; `Effective_policy_changes_after_put_without_restart`. Assert wire JSON sources/revisions and open/parallel/queued counts, project filtering of ready/backlog as well as task arrays, global compatibility recommendation, zero DB mutations. |
| V-8 | DispatchConcurrencyScriptTests (6, Integration) | `Get_and_history_use_correct_scope`; `Set_reads_then_sends_exact_snapshot`; `Clear_sends_empty_override_with_reason`; `Human_and_reasonfile_are_preserved`; `Conflict_is_not_retried_or_overwritten`; `Missing_input_and_invalid_json_do_not_write`. Use a loopback HTTP recorder and real pwsh with the assembly-local ProcessSpawnLimit, following RoutingPinScriptTests. Assert method/path/body/exit code and no secret output. |

Regression groups: **R-1** existing runner defaults/settings migrations (11); **R-2**
legacy create gate (25); **R-3** existing dispatch/host admission/predicates (44);
**R-4** pipeline service/HTTP contract (62); **R-5** standing-policy copies (4).
No full assembly or native-provider battery is justified by this server-only change.
Use DelegationTestServices for dispatcher harness registrations, controlled clocks/barriers
for races, and isolated/fake runner clients. A host booting real Program must use the
established production-runner guard. Mark any genuinely slow new fixture Slow and add its
exact class to `tests/Antiphon.Tests/slow-tests-allowlist.txt` only if measurement requires
it; record that as a justified footprint amendment, not an assumed new file change.

### Proves it works now: the 54 concrete method bodies

Every row below is **one unparameterized method**, with labeled internal scenarios,
not one TUnit execution per scenario. Preserve the existing roster's exact class/method
names. Assertion names in backticks are Shouldly assertion messages for diagnosis and
Mutation, not helper methods that merely return a constant. Unless a row overrides it,
use the F-S seed and fresh state per scenario. S1 has 27, S2 16, S3 11 new results.

**V-1 — DispatchConcurrencyPolicyTests, F-P (8).**

| Method | Input, action and decisive expected result |
|---|---|
| `Legacy_counts_queued_as_open` | LegacyOpen project=3/Plan=1; one queued Plan refuses another on role; queued+dispatched+working Custom at 3 refuses absolute. Removing one occupant admits. Assert population=open and exact counts (`legacy-queued-refusal`); no queue limit is implied by maxParallel. |
| `Separate_counts_parallel_and_queue_independently` | SeparateQueues project 3/4 and Code 2/3 (parallel/queue). Two running Code plus 2 queued allow create, but hold dispatch; 3 queued refuse create. A free Plan role dispatches while project parallel=2, then holds at 3. Assert both decision kinds, populations, count/limit and remaining=0 at/above limits. |
| `Project_then_global_then_seed` | Seed 9/Code5/Plan3; global project parallel=8 and Code=4; project Code=2 only. Expect project parallel 8/global, Code2/project, Plan3/default. Clear global then project independently; assert complete resolved values and every source (`precedence`). |
| `Missing_inherits_null_is_unbounded_zero_pauses_queue` | Compare missing roles, empty Code object, Code maxParallel=null, project maxQueued=0 and null. Empty inherits; null is unbounded on that role only; zero refuses fresh create with count=0 even with an available host. Project parallel=2 still holds a null-role candidate at 2 (`null-not-absent`, `zero-pauses`). |
| `Queue_failure_precedes_legacy_override` | LegacyOpen open=3/limit3 and queued=2/limit2 on both axes. With flag false and true, primary refusal is queued/absolute, canOverride=false; all failures include open+queued and absolute+role. Remove queue saturation: flag false refuses open, true admits. |
| `Absolute_precedes_role_and_reports_all_failures` | Project limit=3, Code=2, with 2 Code+1 Plan; primary=absolute and exceeded constraints also contain Code. Raise project to4: role primary. Role cap above parent (Code5/project3) remains visible as5, combined bound3. Assert ordered full constraint identities and totals, not just text. |
| `Status_and_specialist_populations_match_contract` | One of every status for each of 14 ordinary roles and all 3 specialists; for P expect open=42, parallel=28, queued=14, per ordinary role=3/2/1. Q and null copies do not change P; Blocked/Succeeded/Failed/Canceled cost0. A retained Working ordinary row adds1 parallel, a retained specialist adds0. |
| `Validate_modes_roles_fields_and_bounds` | Literal input matrix: parallel 1/512 accepted, 0/-1/513 refused; queue 0/4096/null accepted, -1/4097 refused. Validate all ordinary role names including Custom; reject specialists, unknown/numeric/duplicate role keys, duplicate/unknown fields, fractional/string/bool/array limit, mode null/unknown/numeric, project parallel null. Role parallel null accepted. Reuse raw JSON corpus in V-3 to prove wire parser behavior (`invalid-policy-refused`). |

**V-2 — DispatchConcurrencySettingsTests, F-S/F-R (10).**

| Method | Input, action and decisive expected result |
|---|---|
| `Import_preserves_bound_values_once` | Import F-S seed: global revision1, project revision0, Migration reason/time, immutable seed 9/5/4/3/null, LegacyOpen, null queues. New service with default2/1 role config still reads original seed (`bound-seed`). Separate fresh clone imports unchanged legacy MaxOpenTasks=600 and Code=700 without clamping; PUT newly writing those values is rejected, clear preserves the original seed. |
| `Concurrent_initialization_writes_one_seed` | Two service providers/contexts run initialization through schedule I below, with same bound values. Exactly one scope and revision1/history row; both answers same seed/time; losing initializer does not update provenance or emit a second change (`one-seed`). |
| `Global_put_changes_existing_service_reads` | Construct reader before PUT. Human global change project parallel7, Code4 and queue6; original reader's next read returns new values/global source and revision2. P/Q/null inherit them; host budgets, runner defaults, roles' routing and existing tasks byte-equal. |
| `Project_override_isolated_and_clear_restores_inheritance` | P writes Code2, Code queue null and project queue0; Q/null retain global. Clear P with `{}` increments its revision and persists empty overrides/history. Later global Code4->3 flows into P; explicit-null and zero effects disappear. A stale pre-clear write fails (`clear-inherits`, `clear-keeps-revision`). |
| `Stale_scope_or_global_revision_writes_nothing` | Independently stale global expectedRevision, stale P expectedRevision, stale expectedGlobalRevision, then P write/clear/write with the pre-clear token. Each throws revision-conflict with current two revisions, zero new history/events and exact saved snapshot unchanged (`stale-scope`, `stale-global`, `no-aba`). |
| `Concurrent_puts_have_one_winner` | Same-revision competing global PUTs (7 vs8), then competing P PUTs with same pair of revisions. Schedule U; one success, one revision-conflict, exactly one history increment/event; winning snapshot equals its complete request, no mixed fields (`one-put-winner`). Also race P PUT against global PUT: global-first makes P stale, P-first commits P then global; asserted revision pair identifies the order. |
| `Auto_cannot_replace_or_shadow_human` | Independent Human global replace/clear by Auto; Human P replace/clear by Auto; new Auto P shadows Human global. All refuse `dispatch_concurrency_human`, unchanged snapshots (`human-scope`, `human-inheritance`). Positive controls in same method: Auto over Migration/Auto succeeds, and explicit Human P may differ from Human global. |
| `Noop_preserves_revision_human_claim_audits` | Auto put equivalent values/field order with trimmed reason does not change revision, time/history/events. Claim same Auto choice as Human creates one new audited revision, then identical Human repeat is no-op. Missing vs explicit-null overrides are different even if current effective values coincide (`human-claim-audits`). |
| `History_paginates_and_survives_clear` | Write 5 revisions including a clear; read descending limit2 pages with exclusive beforeRevision; all IDs appear once, previousRevision links form chain, final cursor null. Limit bounds exercised at service level. Clear keeps earlier complete snapshot and caller/reason provenance; history reads save nothing. |
| `Restart_and_event_failure_preserve_committed_policy` | Event bus queries through an independent connection before returning/throwing and must see committed revision. Force event exception after PUT: GET still returns saved policy. Rebuild provider on same DB with divergent config; seed/overrides/history unchanged (`restart-durable`, `event-after-commit`); no runner call. The real create consequence is V-5 after S2, so CP-1 does not depend on a future slice. |

**V-3 — DispatchConcurrencyWireTests, F-H (6).**

| Method | Input, action and decisive expected result |
|---|---|
| `Global_get_put_roundtrip` | GET supports exactly14 ordinary roles, ranges, seed origin/time and revision1. PUT full audited override with finite/null/zero fields returns200 committed snapshot; follow-up GET and new DB context agree, camelCase field/source/mode/population metadata present. Independent named fields are asserted, not serialization compared with itself. |
| `Project_get_put_clear_roundtrip` | P GET gives projectRevision0 and globalRevision1. PUT reads both, sends partial Code override; GET Q unchanged. PUT `{}` then global change verifies inherited P and nonzero P revision. Unknown project GET/PUT both404 and create no scope/history row. |
| `Missing_fields_invalid_body_and_unknown_project_write_nothing` | Raw JSON corpus: missing/null outer required fields, unknown fields, duplicate keys, wrong type/enum, empty/malformed body, trailing garbage, V-1 bounds, reason blank/401 chars, Migration provenance, forged callerTaskId. Follow RunnerDefaultsPut's explicit reader: catch malformed JSON/type conversion as ValidationException, so all these body cases are422/validation_failed; unknown project is404. Every case has its literal expected status/code and unchanged DB snapshot (`invalid-wire-no-write`); reason1/400, empty overrides and explicit nullable null are positive cases. |
| `Revision_conflict_returns_current_revisions` | Stale global and P/global-pair writes return409 Problem Details code dispatch_concurrency_revision_conflict with current revisions, not a generic500. P/global race from schedule U is also exercised over HTTP. Auto-over-Human returns409 dispatch_concurrency_human. Both leave DB and event list unchanged. |
| `Human_reason_and_server_caller_are_audited` | PUT as seeded authorized task caller with body Human and padded reason; history stores trimmed text and server caller ID. Anonymous allowed caller records null, never a body ID. Forged body ID is rejected before mutation. Assert complete audit fields (`server-caller-only`), event follows commit, and no placement/routing/host write. |
| `Revision_routes_are_readonly_and_bounded` | Exercise both route families, default50/max100, explicit1, exclusive cursor and end-of-pages; seed101 history rows via service. Invalid limit0/101 and invalid cursor refused422. Two GET/history calls preserve table snapshots and invoke no save/initialization; absent project404 (`get-is-readonly`). |

**V-4 — DispatchConcurrencyMigrationTests, F-M (3).**

| Method | Input, action and decisive expected result |
|---|---|
| `Upgrade_populated_database_preserves_task_rows` | At preceding migration seed P/Q/null tasks in every status, sessions, host override0 and runner-default revision. Apply actual migration; old row snapshots are identical and no task is requeued. Verify schema/indexes by attempting duplicate scope and duplicate (SettingsId,Revision) writes in separately rolled-back transactions: both unique violations. Ensure initialization later supplies config rather than migration SQL constants. |
| `Initialize_from_nondefault_config_then_restart` | Upgrade then initialize with F-S seed; persist global mode SeparateQueues and P override; restart provider with MaxOpenTasks=2/Code1. GET still honors saved policy, clear restores seed9/Code5, global/project revisions never reset. Real create/dispatch consequences belong to S2's V-5/V-6. |
| `Interrupted_initialization_rolls_back_seed_and_history` | Throw once immediately before import commit, after both insert commands; fresh context sees zero settings and history, no event (`atomic-import`). Retry succeeds with exactly one complete revision1. Do not use a failure before any SQL as the rollback proof. |

**V-5 — DispatchConcurrencyAdmissionTests, F-H/F-S/F-R (8).**

| Method | Input, action and decisive expected result |
|---|---|
| `Unconfigured_create_keeps_legacy_decisions` | No limit PUT: imported Plan3 admits first3 Queued Plan rows, fourth409/open/role; explicit seed Plan1 subcase admits first and refuses second. Overall9 uses mixed roles/Custom; tenth409/absolute. Blocked/terminal controls admit. Assert inserted IDs and zero failed-request rows; mode stays LegacyOpen (`legacy-real-create`). |
| `Global_and_project_put_change_real_create` | Same AgentTaskService instance: global Legacy Plan1 refuses second; Human global Plan2 admits it. P override Plan3 affects only P; clear restores current global. Reconstruct the provider with divergent seed options and repeat CreateAsync: saved limits still decide. Schedule A/U below races a lower PUT with a fresh create in both orders, proving no stale policy and exactly the legal committed row set. |
| `Project_and_role_queue_boundaries_refuse_without_insert` | Separate project parallel3/queue4, Code parallel2/queue3. Two Working Code do not spend queue allowance: first3 queued Code accepted, fourth role409; a Plan fills fourth project slot, next Custom absolute409. Complete a queued row and retry accepts one. Loop role/project queue0 and null: zero pauses at empty, null does not bound; keep finite running gate. Compare task/event count after every refusal (`queue-count-only`, `queue-refuses`). |
| `Other_project_and_null_bucket_do_not_interfere` | Fill P queue and parallel; Q and null each accept their own first row. Fill Q separately; it cannot block null. Requests for P list only P occupants; blocked/terminal/specialists from P and ordinary Q/null never contaminate its count. Check parent-derived project scope and explicit null bucket (`project-isolation`). |
| `Specialists_and_live_followups_keep_admission_exemptions` | Full queue/open: internal producer paths for Check/Distill/Diagnose admit, and accepted specialist rows do not increase later ordinary counts. Live ordinary follow-up admits; its queued row DOES increase subsequent ordinary count. Retired/absent-agent continuation is fresh admission and409. Use valid existing specialized-producer setup, not an invalid public request for an internal role (`specialist-exempt`, `followup-only-live`). |
| `Override_only_bypasses_legacy_open` | Legacy full open/no queue bound: flag accepts plus warning. Add full queue: flag still409/canOverride=false with no insert. SeparateQueues: flag cannot lift role/project queue bounds, nor subsequent running hold; no persisted bypass on accepted row (`override-cannot-lift-queue`). |
| `Concurrent_last_queue_slot_has_one_winner` | Schedule A: project queue1 with unbounded role, then role queue1/project4, plus null bucket. A/B contexts and distinct requests: 1 accepted, 1 concurrency_limit409, exactly1 matching queued row and Created event, no leftover partial audit/event. F-R records B's real lock wait before A commit (`one-create-winner`). |
| `Problem_names_population_sources_revisions_and_bounded_occupants` | Seed15 matching queued occupants plus foreign/specialist distractors, ordered timestamps with equal-time GUID tie. Refusal reports total15, list12 in CreatedAt/ID order, omitted3, project/role, mode, field sources and exact two revisions, all exceeded constraints, queue primary/canOverride=false; old `open` and `override` keys remain. Legacy subcase reports open/canOverride=true and only matching role occupants (`problem-total`, `problem-capability`, `problem-population`). |

**V-6 — DispatchConcurrencyDispatchTests, F-D/F-R/F-Q (8).**

| Method | Input, action and decisive expected result |
|---|---|
| `Parallel_cap_holds_then_next_tick_releases` | Separate P parallel1; Working occupant and queued successor. Three ticks dispatch0, preserve row/session/agent fields and one Held detail at recorded revision. Advance fake clock to HeldAged boundary; deduplicated warning as existing contract. Terminalize occupant, tick again dispatch1 and deliver exactly one complete successor brief. Role cap and project cap run independently. Other free role/project can progress (`held-zero-launch`). |
| `Project_cap_spans_local_and_remote_hosts` | P parallel2 with local+runner-a Working and queued runner-b; every host still has spare seats. Runner-b stays Queued; Q runner-b dispatches. Complete local occupant then P progresses. Conversely host budget0 holds despite free project slot, and clearing it releases; retained Working spends P slot while releasing host budget. Assert host and project counts separately (`cross-host-project-cap`). |
| `All_launch_paths_obey_role_cap` | Matrix cold local, cold prepared-mirror remote, warm pooled, standing non-specialist, live follow-up. Each has distinct eligible agent/session, role cap1 already full, project/host ample; zero launch/queue/prompt for held candidate and unchanged tokens/agent idle/session bindings. Drain role and tick: one Dispatched row; fake cold launch once, warm/standing reuse same session with zero cold starts; real queue produces complete UserPrompt. Repeat idle recipient and recipient becoming busy after commit: no submit until TurnEnd. Inject claim rollback, cancellation before commit and enqueue failure as delivery inventory below (`every-path-gated`, `held-custody`, `complete-recipient-prompt`). |
| `Competing_claims_admit_only_one_task` | Schedule D on two distinct queued IDs, project-last-slot and role-last-slot; cold/cold, warm/standing and local/prepared-remote pairs. Exactly one durable Dispatched + one Queued, one candidate has launch/delivery, held candidate has none; no shared-agent/host constraint may mask the project lock. Repeat with first claimant canceled/rolled back: second dispatches, first remains queued with owned preparation (`one-dispatch-winner`). |
| `Put_racing_claim_observes_one_complete_policy` | Schedule P for every cold/warm/standing path and prepared mirror: old project cap2/Code2, one active, commit cap1/Code1 while tick paused before final claim. Candidate held with new revision, no launch. Reverse order allows claim at old revision then PUT reports overage without killing. Include LegacyOpen->SeparateQueues switch (outer tick must not skip final key), and global+P fields with intentionally distinguishable revision pairs (`latest-claim-policy`, `lock-order`, `no-external-io-under-key`). |
| `Lowering_never_kills_or_discards_accepted_work` | 3 running+4 queued; lower parallel3->1 and queue4->0, then tick/create. IDs, running statuses/sessions and queued ownership unchanged; create409, holds report parallel overage2 and queue overage4, zero kills/cancels/releases. Drain running through explicit test state transitions; no start at occupancy1, next starts at0 despite queue0 because it was accepted earlier (`lowering-preserves-work`). |
| `Restart_recounts_without_leaked_slots` | Admit one, cancel or roll back a second pre-commit claim, dispose provider, rebuild against same DB; no phantom slot, recount1. At cap1 candidate holds; complete admitted task and next tick dispatches once. A transaction committed before provider disposal retains its slot even if delivery is pending; no replay launch on ordinary next tick (`restart-recounts`). |
| `Recovery_and_retained_wait_preserve_owned_slots` | Accepted task requeue/routing-blocked resume goes Queued even when queue0/full; fresh create still409. Its redispatch obeys current parallel cap. Retained capacity wait remains Working and counted, returns on existing host admission without spending another project slot; new queued work cannot steal it. Preserve task/session identity and custody on provider rebuild/recovery (`recovery-owned-slot`). |

**V-7 — DispatchConcurrencyPipelineTests, F-H/F-D (5).**

| Method | Input, action and decisive expected result |
|---|---|
| `Scoped_pipeline_matches_create_and_dispatch_policy` | P Separate override, Q Legacy and null seed; mix open/parallel/queue/retained/specialist statuses. P GET arrays/counts/effective role limits/sources/revisions agree with the subsequent real POST refusal and dispatcher hold. Seed bound ready and backlog cards in P/Q and assert both arrays filter, not merely active tasks (`scoped-policy-matches`). |
| `Empty_project_and_unscoped_have_correct_limits` | Empty P still has a policy snapshot and counts0; `unscoped=true` gives only null bucket; no-query includes represented scopes and null. Unknown project404, malformed GUID/invalid unscoped/conflicting parameters422, no writes. |
| `Fleet_contract_and_host_totals_remain_distinct` | No-query retains legacy arrays/recommendationsAreAdvisory and global recommendedInFlight (e.g. Code4 after PUT), while concurrencyScopes names real project counts. Host summaries remain identical fleet totals in scoped/unscoped reads and explicitly fleet-scoped; runner sessions do not inflate local cap. F-S MaxConcurrentTasks2 remains host configuration only. |
| `Queued_hold_and_overage_match_gate_without_writes` | Role/project parallel holds emit matching IDs/sources/revisions; lease/sibling-land/routing-date reason still dominates when present. Below/at/above limits gives nonnegative remaining and explicit overage. Pin expiry and reading empty initialized policy write nothing; compare tasks, settings/history, pins, events before/after (`pipeline-readonly`). |
| `Effective_policy_changes_after_put_without_restart` | Construct service/HTTP host once, read P, PUT global then P then clear; same service returns changed values/sources/revisions, ready counts and atLimit accurately. Schedule P read barrier around global+project snapshot: returned pair is wholly before or after a committed update, never assembled from incompatible revisions (`pipeline-live-revision`). |

**V-8 — DispatchConcurrencyScriptTests, F-X (6).**

| Method | Input, action and decisive expected result |
|---|---|
| `Get_and_history_use_correct_scope` | Run get/history global and `-Project <P>`; recorder sees exact four route families, GET only, proper cursor/limit and no PUT. JSON output parses with expected scope/revisions; token sentinel absent. |
| `Set_reads_then_sends_exact_snapshot` | `-SettingsFile` and inline JSON subcases contain explicit null, zero and omitted role fields. Default sequence GET then single PUT with obtained expected revisions and exact supplied override; explicit revision arguments are sent unchanged (do not silently substitute newer GET revisions). Project request carries both revisions, global only its own; success exit0. |
| `Clear_sends_empty_override_with_reason` | Global and P clear read correct revision(s), send literal empty overrides object and supplied audit fields, never a materialized global/default policy. Exactly one PUT each; reason retained, exit0. |
| `Human_and_reasonfile_are_preserved` | UTF-8 multiline reason file with quotes/backticks is passed literally except server-owned trimming; Human remains Human. Inline reason and Auto subcase preserved. Assert sentinel absent from stdout/stderr and header present at recorder; ASCII-only script source parses without PowerShell5.1-unsafe syntax (no Windows-only test requirement). |
| `Conflict_is_not_retried_or_overwritten` | Recorder returns409 revision conflict then would accept a second PUT. Run returns nonzero and displays conflict; ordered requests contain one GET/one PUT and no later GET/PUT (`single-conflict-put`). No sleep/retry or hidden change in revision. |
| `Missing_input_and_invalid_json_do_not_write` | Missing settings, malformed JSON, missing reason, invalid provenance/project and mutually exclusive inputs fail nonzero locally with zero PUT; unsafe missing expected fields are never synthesized into a clear. Assert diagnostics identify bad input, not token. |

### Deterministic schedules for admission, settings PUT and dispatch

No test waits for two entrants **inside** a lock that correctly admits one. No fixed
sleep establishes order. F-R's observation race returns `BlockedOnExpectedKey`,
`ReachedWrite`, `Completed` or `Faulted`; it polls only the identified PostgreSQL PID
and has a bounded harness deadline. A deadline is a harness failure, **never a positive
control red**. For a removed lock, the other entrant reaches the write checkpoint or
completes, so the named exclusion assertion fails immediately. Release the barriers in
finally and collect outcomes even on that failure. Use ReadCommitted transactions;
cross-context probes must not reuse A's connection or wait for its transaction to commit.

| Schedule | Ordered operations and assertion; methods that own it |
|---|---|
| I — first initialization | A imports and pauses at final SaveChanges after its locked existence read. B starts initialization. Observe B blocked on create key OR reaching its own write/unique failure. Assert `initializer-B-blocked=true`; release A and expect identical revision1 responses and one persisted seed/history. V-2 concurrent initialization; V-4 interruption covers rollback. |
| A — last queue slot | A's real CreateAsync counts free capacity, then pauses on its Added AgentTask SaveChanges before INSERT. B's CreateAsync proceeds until PostgreSQL says it waits for A's create key, or B reaches its own insert checkpoint. Assert `create-B-crossed=false`; release A, then B; expect one201/one409. Removing TakeLock lets both reach insert; released stale decisions also yield two rows, failing `one-create-winner`. No callback is placed only inside the removed gate. V-5 concurrent-last-slot. |
| U — same revision PUT | A has read revision r and pauses immediately before settings/history SaveChanges. B PUT expected r waits on create key (then parallel key) or crosses to save. Release A, let B re-read and conflict. `put-winners=1`, `history-delta=1`. To kill revision-comparison mutants deterministically, run the same stale request after A commits too; a removed comparison must fail `stale-scope`/`stale-global` regardless of the remaining EF token. V-2 concurrent/stale, V-3 revision response. |
| A/U — create against PUT | Variant1: create A pauses pre-insert while holding create key; lower-limit PUT B must wait, then commits after A. Existing accepted A remains and PUT reports occupancy/overage. Variant2: PUT A pauses pre-commit holding create+parallel keys; create B must wait, then refuses under committed queue0/new revision with no insert. Assert `admission-used-revision` and `create-B-crossed=false`; run LegacyOpen and SeparateQueues. V-5 Global_and_project_put_change_real_create. |
| D — final claims | Direct real DispatchOneAsync(A) is paused by SaveChangesInterceptor when A changes Queued->Dispatched, after policy evaluation and before commit. B has a different task row/session and reaches either a **parallel-key** wait or final status save. Assert `claim-B-crossed=false`; release A; B sees committed occupant and stays Queued. B must not be waiting on the same task, agent, host or repository lease (use distinct roots or already prepared mirrors). Lock-removed mutant reaches save with stale count; gate-removed mutant also reaches save. Both terminate at `one-dispatch-winner`/`launch-count=1`, not a barrier timeout. V-6 competing claims. |
| P — PUT beats stale tick | Start real tick at policy r, pause candidate FOR UPDATE command before execution (its outer snapshot is already read). PUT lower limit/mode at r+1 commits; release tick. Final transactional read must use r+1 and hold with no launch. Converse: pause final claim pre-commit while holding parallel key, start PUT holding create key; observe it wait on parallel, release claim then PUT, expect existing task retained. Run cold/warm/standing/prepared paths. Inspect `lock-order`: create->parallel on PUT; task/prior-owner locks->parallel on dispatch; no reverse acquisitions. V-6 PUT race. |
| P/read — coherent projection | Pause settings read after first result is available; commit global+P edits using other contexts; finish reader. Its snapshot must contain a pair that existed together (record pre/post pairs). A repeatable-read transaction or composed query passes; two independent ReadCommitted reads can return a never-valid pair and fail `snapshot-pair-existed`. V-7 live policy read. |

For D and P, record outgoing fake runner/Git/provider calls with their transaction IDs.
An observer's successful try-lock of the parallel key at the outgoing launch/submit point
proves it is no longer held; `external-call-key-free=true` is required. At preparation
and provider probe points it must also be free, before the late gate. A missing final
gate cannot pass on that assertion alone: the status/launch and key-observation assertions
are mandatory. A denied claim must roll back Added agent/session/event rows and token
changes; reset the context before tracing Held so a later SaveChanges cannot flush them.

### Delivery inventory and limits of the substitutes

This card changes authorization to enter the existing delivery paths, not their transport.
All denied-claim assertions concern **zero attempted delivery**. Successful-path evidence
uses F-Q's real queue plus submitted-body transcript; fake launch-call counts alone never
prove a brief arrived. Native Pty/provider behavior is unchanged and excluded here.

| Producer/destination and durable identity | Persistence, recovery and observable receipt |
|---|---|
| CreateAsync -> queued AgentTask, TaskId/ProjectId | Create key held through INSERT commit; rollback/409 inserts nothing. V-5 A schedule proves exact accepted IDs. Queued is acceptance, not session delivery. |
| DispatchOneAsync -> cold launch or warm/standing queue, TaskId/AgentSessionId plus session generation | Parallel key held only through Dispatched+binding commit. V-6 all-path/competing/restart methods inject failure/cancellation before final save and after save-before-commit: no session/launch/queue leaks, Queued retry uses current policy. Prepared mirror remains owned. |
| Existing runtime/DeliverReuseMessagesAsync -> SessionMessageQueueService -> recipient | V-6 All_launch_paths uses the real queue for idle and busy-after-commit recipients; complete UserPrompt must contain task marker and entire expected brief with matching SessionId and sequence above the pre-delivery watermark. Busy recipient has no submit until TurnEnd; then one receipt. Inject enqueue throw via the existing ReuseEnqueueOverride for a separate fault scenario: persisted Dispatched owner plus existing delivery-failure incident, **no false Delivered claim**. Reset override and exercise the documented retry/delivery entry once, then require the whole prompt. Do not assume ordinary Tick automatically retries a failed enqueue. |
| Committed claim -> provider restart/recovery, same TaskId/session | V-6 Restart_recounts and Recovery_and_retained_wait preserve counted slot and binding if process dies after commit/before delivery. The existing recovery path owns retransmission; do not invent a second queue or launch from this settings feature. Persisted queue-message recovery after enqueue is exercised by disposing/reopening queue runtime against same DB before F-Q submit. Assert one whole recipient UserPrompt and no second Dispatched event/launch. |
| Settings PUT -> IEventBus DispatchConcurrencyChanged, scope and revision pair | V-2 event test queries from a separate connection at publish: revision already committed. Throwing event leaves durable GET/history authoritative; no persisted outbox or replay is promised. This is invalidation, not transcript delivery; CARD-0506 owns UI observation. |

### Guards the regression and exclusions

R-1 keeps runner-default seed/history/placement settings independent (11 results); R-2
keeps legacy project/specialist/follow-up behavior and warning text (25); R-3 keeps the
host-budget min/declaration/zero/drain rule, held trace/custody and prepared-mirror
predicate (44); R-4 retains fleet arrays and literal pipeline HTTP routing (62); R-5
retains standing Code/Review=2, other stages1 and feed2 policy (4). Keep existing assertions,
not merely the method names, when wiring the new provider.

No Windows row is required: PostgreSQL locking, migrations, fake dispatcher/recipient,
loopback HTTP and pwsh run on Linux and Windows. New fixture code must not use cmd.exe,
ConPTY, native fakeclaude/fakegrok apphosts, WMI or a live provider. Existing ScratchGitRepo
regression setup uses portable git. Apply ProcessSpawnLimit to any new class that later
chooses real Git; F-X already needs it. No client/E2E/runner deployment or full assembly
acceptance run; CARD-0506 owns UI, CARD-0778 native readiness. These fake receipts prove
the admission/queue boundary, not native terminal rendering or provider authentication.

### Positive controls

The Plan's **15 control groups** contain independently bypassable guards. TestDesign
splits them into **44 compiling mutation variants**, each with its own guard ID and
named assertion. One guard maps to one variant: **guards=44, mapped=44, missing=0,
duplicate PC maps=0**. This does not add ordinary test methods or change checkpoint Min.
Other validation, history and diagnostic assertions remain in the 54 ordinary methods.
Existing host, scope, routing, queue transport and native custody guards remain their
own cards' responsibility and are regression coverage here, not silently new mutants.

For each row, Mutation changes only the named production behavior, runs
`/*/*/<class for V-n>/<exact method>` with Min=1, restores, rebuilds and runs the same
method green. Compilation/setup failures, zero tests, deadlines and self-comparisons
are not RED evidence. All expected failures below are ordinary outcome/Shouldly
assertions. No mutant has been executed by this TestDesign task.

| Guard | PC | Compiling production defect | Exact method and failing assertion (class from V-n) |
|---|---|---|---|
| G-1a bound deployment seed | PC-1a | Initialize with new DelegationSettings instead of bound options. | V-2 `Import_preserves_bound_values_once`: `bound-seed`, Code5/Plan3/overall9. |
| G-1b atomic seed/history | PC-1b | Persist the seed with a separately committed context before writing history in the import transaction. | V-4 `Interrupted_initialization_rolls_back_seed_and_history`: `atomic-import`, zero rows after injected pre-commit failure (mutant leaves the seed). |
| G-1c single initializer | PC-1c | Omit initializer advisory acquisition, retain unique indexes. | V-2 `Concurrent_initialization_writes_one_seed`: `initializer-B-blocked=true`; B instead reaches save (or collected unique-fault), then `one-seed` checks both successful answers. |
| G-2a resolution precedence | PC-2a | Resolve global before project. | V-1 `Project_then_global_then_seed`: `precedence`, Code2/project. |
| G-2b explicit null | PC-2b | Treat present-null role field as missing. | V-1 `Missing_inherits_null_is_unbounded_zero_pauses_queue`: `null-not-absent`, null/project source and still finite project cap. |
| G-2c validated limit range | PC-2c | Accept parallel0 in policy validation. | V-1 `Validate_modes_roles_fields_and_bounds`: `invalid-policy-refused` for parallel0. |
| G-3a clear inherits | PC-3a | Materialize global values into cleared project overrides. | V-2 `Project_override_isolated_and_clear_restores_inheritance`: `clear-inherits`, later global Code3 reaches P. |
| G-3b no revision ABA | PC-3b | Delete the project settings row on clear. | V-2 `Project_override_isolated_and_clear_restores_inheritance`: `clear-keeps-revision`, row/revision remain and stale pre-clear write conflicts. |
| G-4a scope CAS | PC-4a | Skip expectedRevision comparison. | V-2 `Stale_scope_or_global_revision_writes_nothing`: `stale-scope`, deterministic post-commit stale call throws409 and writes nothing. |
| G-4b inherited CAS | PC-4b | Skip expectedGlobalRevision comparison on P PUT. | Same V-2 method: `stale-global`, current two revisions and no mutation. |
| G-4c Human scope custody | PC-4c | Permit Auto to overwrite Human scope. | V-2 `Auto_cannot_replace_or_shadow_human`: `human-scope`, dispatch_concurrency_human and identical snapshot. |
| G-4d inherited Human custody | PC-4d | Permit Auto project shadow over Human global. | Same V-2 method: `human-inheritance`, conflict and no P row. |
| G-4e Human audit claim | PC-4e | Treat Auto->Human same-value claim as a value-only no-op. | V-2 `Noop_preserves_revision_human_claim_audits`: `human-claim-audits`, one new Human history revision. |
| G-4f explicit replacement intent | PC-4f | Default missing outer overrides to empty object. | V-3 `Missing_fields_invalid_body_and_unknown_project_write_nothing`: `invalid-wire-no-write`, request rejected and Human policy retained. |
| G-4g server attribution | PC-4g | Persist callerTaskId=null rather than resolved authorized caller. | V-3 `Human_reason_and_server_caller_are_audited`: `server-caller-only`, exact seeded caller ID. |
| G-5a legacy population | PC-5a | Omit Queued from LegacyOpen count. | V-5 `Unconfigured_create_keeps_legacy_decisions`: `legacy-real-create`, second Plan at explicit seed1 is409/no insert. |
| G-5b opt-in mode | PC-5b | Seed SeparateQueues instead of LegacyOpen. | Same V-5 method: `legacy-real-create`, queued saturation is refused without a PUT. |
| G-5c live-only continuation exemption | PC-5c | Exempt retired-agent continuations as though live. | V-5 `Specialists_and_live_followups_keep_admission_exemptions`: `followup-only-live`, retired case409/no insert. |
| G-6a queued population | PC-6a | Use open count for queue admission. | V-5 `Project_and_role_queue_boundaries_refuse_without_insert`: `queue-count-only`, three queued Code accepted despite two Working. |
| G-6b restricted override | PC-6b | Apply ignoreConcurrencyLimit to queue/running checks. | V-5 `Override_only_bypasses_legacy_open`: `override-cannot-lift-queue`, flagged full queue409/no insert. |
| G-6c zero pauses admission | PC-6c | Treat maxQueued0 as unbounded. | V-5 `Project_and_role_queue_boundaries_refuse_without_insert`: `queue-refuses`, empty queue0 still409. |
| G-7 count/insert serialization | PC-7 | Remove create advisory call. | V-5 `Concurrent_last_queue_slot_has_one_winner`: `create-B-crossed=false` and `one-create-winner`, schedule A. |
| G-8a final authorization | PC-8a | Skip the final shared policy decision on cold claims. | V-6 `Competing_claims_admit_only_one_task`: `one-dispatch-winner`, one Dispatched/one Queued, schedule D. |
| G-8b warm authorization | PC-8b | Bypass only the warm-reuse role gate. | V-6 `All_launch_paths_obey_role_cap`: `every-path-gated`, warm candidate Queued and zero queue/prompt. |
| G-8c standing authorization | PC-8c | Bypass only standing non-specialist gate. | Same V-6 method: `every-path-gated`, standing candidate Queued and same idle session. |
| G-8d shared parallel serialization | PC-8d | Remove parallel advisory call but retain count/decision. | V-6 `Competing_claims_admit_only_one_task`: `claim-B-crossed=false`, then `one-dispatch-winner` under schedule D. |
| G-8e denied-claim rollback | PC-8e | Save tentative agent/session/token changes when a final claim is held. | V-6 `All_launch_paths_obey_role_cap`: `held-custody`, exact before/after bindings/idle/token fields and no Added session row. |
| G-8f commit before external delivery | PC-8f | Move final commit after cold launch or reused delivery. | V-6 `Put_racing_claim_observes_one_complete_policy`: `external-call-key-free=true` fails at the recorded launch/submit; no delivery timeout needed. |
| G-9a fresh dispatch snapshot | PC-9a | Reuse tick-start policy after committed lower PUT. | V-6 `Put_racing_claim_observes_one_complete_policy`: `latest-claim-policy`, Queued at r+1, launch count0, schedule P. |
| G-9b acyclic lock order | PC-9b | Reverse PUT's create->parallel acquisition. | Same V-6 method: `lock-order`, F-R command interceptor asserts the next requested key BEFORE executing a reversed acquisition, so no deadlock timeout is the RED. |
| G-10a project isolation | PC-10a | Drop ProjectId/null bucket predicate in admission. | V-5 `Other_project_and_null_bucket_do_not_interfere`: `project-isolation`, Q/null still accept and P refusal excludes foreign IDs. |
| G-10b specialist exclusion | PC-10b | Count Check/Distill/Diagnose as ordinary occupants. | V-5 `Specialists_and_live_followups_keep_admission_exemptions`: `specialist-exempt`, next ordinary acceptance/count unchanged by specialist rows. |
| G-11a lowering retains accepted work | PC-11a | Cancel an excess accepted task when limits decrease. | V-6 `Lowering_never_kills_or_discards_accepted_work`: `lowering-preserves-work`, same IDs/statuses and kills/releases0. |
| G-11b retained project WIP | PC-11b | Exclude CapacityWaitRetained from parallel count as host count does. | V-6 `Recovery_and_retained_wait_preserve_owned_slots`: `recovery-owned-slot`, queued successor held while retained owner remains Working. |
| G-12a durable overrides | PC-12a | Clear saved overrides when reconstructing the service/provider. | V-2 `Restart_and_event_failure_preserve_committed_policy`: `restart-durable`, exact saved overrides/revisions. |
| G-12b immutable imported seed | PC-12b | Reimport options on each initialization. | V-4 `Initialize_from_nondefault_config_then_restart`: after clear overall9/Code5, not restart config2/Code1. |
| G-13a live pipeline reader | PC-13a | Read captured IOptions for recommendations/effective limits. | V-7 `Effective_policy_changes_after_put_without_restart`: `pipeline-live-revision`, changed values/source/revision through same instance. |
| G-13b scoped projection | PC-13b | Use fleet task population in scoped pipeline. | V-7 `Scoped_pipeline_matches_create_and_dispatch_policy`: `scoped-policy-matches`, exact P-only counts/IDs. |
| G-13c read-only projection | PC-13c | Persist expired pin clearing during pipeline GET. | V-7 `Queued_hold_and_overage_match_gate_without_writes`: `pipeline-readonly`, identical pin/settings/task/event snapshots. |
| G-13d coherent policy snapshot | PC-13d | Split snapshot into independent ReadCommitted global/project reads. | V-7 `Effective_policy_changes_after_put_without_restart`: `snapshot-pair-existed`, schedule P/read. |
| G-14a population diagnostic | PC-14a | Label queue refusal as open. | V-5 `Problem_names_population_sources_revisions_and_bounded_occupants`: `problem-population`, queued exact wire value. |
| G-14b truthful override diagnostic | PC-14b | Set canOverride=true on queue refusal. | Same V-5 method: `problem-capability`, false. |
| G-14c untruncated totals | PC-14c | Count the Take(12) display list as total. | Same V-5 method: `problem-total`, total15/list12/omitted3. |
| G-15 no automatic conflict overwrite | PC-15 | Re-fetch revision and resend PUT once after409. | V-8 `Conflict_is_not_retried_or_overwritten`: `single-conflict-put`, exactly1 PUT/nonzero exit even though recorder would accept retry. |

### Cost

Ordinary Code checkpoint floor is **36 minutes** (8 + 16 + 12), estimated, not measured.
CP-1's 8 minutes estimates one build/store startup (4) plus 38 settings/wire/migration
results (4). CP-2's 16 estimates build/startup (4) plus 85 admission/dispatch/race results
(12). CP-3's 12 estimates build/startup (4) plus 77 pipeline/pwsh results (8). These
are conservative estimates, not historic timings inferred from test counts. CP-2's
16-minute estimate makes the importer warn that 3x16 exceeds its 45-minute timeout ceiling;
the actual ceiling remains 45, not an instruction to broaden or omit the row.

Allow approximately **180–240 additional minutes** for the 54 concrete tests, integration
and late-lock refactor; Code commissioning estimate is **216–276 minutes** including V/R.
Post-land Mutation floor is **220 minutes estimated**: setup/build8 + restored ordinary
V/R36 + 44 method-scoped variants at4 minutes each for edit/build/red/restore/build/green
(176). Each variant selects exactly the method in its table row, Min1; shared assertions
do not multiply execution counts. Record every mutant, assertion, restoration and actual
cost. No full-suite or native-provider battery is hidden in that budget. Targeted classes
and single-method mutants avoid repeated full-assembly builds/runs; no measured savings
claim is made because this task ran neither alternative.

The existing 146 and
planned new 54 results yield **200 expected ordinary executions**; those counts are not
minutes or internal assertion counts. Code must update the manifest before running a
changed roster if it changes data expansion, names or coverage.

Use the checkpoint tool once per committed slice group, for example
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan
docs/superpowers/plans/2026-09-30-card-0505-configurable-dispatch-concurrency-plan.md --after S1`.
Continue `wait` while exit is 75. Tool-owned isolated builds take host slots; do not wrap
the checkpoint driver in a second slot. Any separate EF CLI/build command must use
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0505-migration -- <command>` and be
reported with its schema-generation reason. Run TUnit via dotnet run, never dotnet test.
Linux fake-runner/PostgreSQL evidence is sufficient; no runner deploy, real CLI launch,
Pty suite, client build or E2E run is part of these rows. Unexpected red rows need diagnosis
and the same row rerun, with counts/reruns reported.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0505-s1/` | concurrency-settings | `/*/*/(DispatchConcurrencyPolicyTests*)\|(DispatchConcurrencySettingsTests*)\|(DispatchConcurrencyWireTests*)\|(DispatchConcurrencyMigrationTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultMigrationTests*)/*` | V-1, V-2, V-3, V-4, R-1 | all named methods: 11 existing + 27 new = 38, 0 failed/skipped | 38 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c0505-s2/` | concurrency-admission | `/*/*/(DispatchConcurrencyAdmissionTests*)\|(DispatchConcurrencyDispatchTests*)\|(AgentTaskConcurrencyLimitTests*)\|(AgentTaskDispatcherPredicateTests*)\|(DispatchHoldVisibilityTests*)\|(HostBudgetServiceTests*)/*` | V-5, V-6, R-2, R-3 | all named methods: 69 existing + 16 new = 85, 0 failed/skipped | 85 | 16 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c0505-s3/` | concurrency-surface | `/*/*/(DispatchConcurrencyPipelineTests*)\|(DispatchConcurrencyScriptTests*)\|(AgentTaskPipelineStatusTests*)\|(AgentTaskPipelineEndpointTests*)\|(StandingPipelinePolicyDocumentationTests*)/*` | V-7, V-8, R-4, R-5 | all named methods: 66 existing + 11 new = 77, 0 failed/skipped | 77 | 12 |
