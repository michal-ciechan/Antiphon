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

No builds/tests are run in this Plan stage; the census below is static source evidence,
not a TRX. TestDesign must author concrete tests for the new roster, pin the transaction
interleavings and confirm selectors before Code. Do not replace outcome assertions with
tests of copied formulas or settings-only assertions that never exercise the real gate.

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

### Positive controls

Mutation executes each control method-scoped on a restored baseline and records the named
assertion turning red. Compile errors, fixture failures, self-comparisons and zero tests
are not a red control. TestDesign supplies deterministic barriers for PC-7/8/9.

| PC | Deliberate production defect | Required red assertion |
|---|---|---|
| PC-1 | Import shipped defaults instead of bound config. | V-2 `Import_preserves_bound_values_once`: seeded Code=5/Plan=3 remain those values, not 2/1. |
| PC-2 | Reverse global/project precedence or treat explicit null as absent. | V-1 `Project_then_global_then_seed` / `Missing_inherits_null_is_unbounded_zero_pauses_queue`: exact value and source. |
| PC-3 | Clear by copying globals into project overrides. | V-2 `Project_override_isolated_and_clear_restores_inheritance`: a later global edit flows into the cleared project. |
| PC-4 | Remove revision comparison or Auto-over-Human guard (separate mutants). | V-2 stale/conflicting/Auto methods: refusal plus unchanged revision/history and saved values. |
| PC-5 | Count only running tasks in LegacyOpen. | V-5 `Unconfigured_create_keeps_legacy_decisions`: second queued Plan is 409, no inserted row. |
| PC-6 | Use open instead of queued count in SeparateQueues, or allow override past queue cap. | V-5 queue/override methods: working rows do not spend queue slots; full queue still 409 with the flag. |
| PC-7 | Remove count+insert advisory serialization. | V-5 `Concurrent_last_queue_slot_has_one_winner`: exactly one accepted insert and one 409 under a controlled two-context interleaving. |
| PC-8 | Remove final shared dispatch gate, or guard only cold spawn. | V-6 `Competing_claims_admit_only_one_task` / `All_launch_paths_obey_role_cap`: one Dispatched row, no second fake launch/delivery. |
| PC-9 | Reuse stale tick policy after a committed PUT. | V-6 `Put_racing_claim_observes_one_complete_policy`: queued task remains held under the lower committed revision. |
| PC-10 | Drop ProjectId filter or specialist exclusion. | V-5 scope/exemption methods: foreign occupants absent, other project still accepts; specialist has no capacity cost. |
| PC-11 | Kill, cancel or fail existing work on a lower cap. | V-6 `Lowering_never_kills_or_discards_accepted_work`: identical accepted row IDs/statuses and empty kill/prompt calls. |
| PC-12 | Lose settings on restart or reimport env every read. | V-2 restart/V-4 restart methods: saved overrides/revisions persist with deliberately different second-process config. |
| PC-13 | Read old IOptions values in pipeline or use fleet count for a scoped gate. | V-7 match/PUT methods: exact non-default scoped limit/source/revision and counts agree with POST/dispatch. |
| PC-14 | Remove population/canOverride or truncate totals to the displayed occupant count. | V-5 problem method: queued refusal cannot advertise an effective bypass; total remains >12, list length=12. |
| PC-15 | Re-send a script PUT after a 409. | V-8 conflict method: recorder has exactly one PUT and nonzero script result. |

### Cost

Ordinary Code checkpoint floor is **36 minutes** (8 + 16 + 12), estimated, not measured.
Allow approximately 120–180 additional minutes for implementation and test authoring;
set the commissioned duration from that work plus the floor. Post-land Mutation is
separately budgeted for 15 method-scoped controls and restoration. The existing 146 and
planned new 54 results yield **200 expected ordinary executions**; those counts are not
minutes or internal assertion counts. TestDesign must update the manifest before Code
if it changes data expansion, names or coverage.

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
