# CARD-0881: one effective dispatch-settings read

Date: 2026-10-01. Stage: Plan; TestDesign follows. Baseline: `origin/master` = `fccef27eb03f2522f2a40574dc1c764335d4a490`, also the assigned branch's starting commit and the remote master observed with `git ls-remote`. Branch: `feat/card-task-456be05e`. No rebase, merge, reset or runtime change is part of this task.

## Outcome and scope

Deliver `GET /api/settings/dispatch?projectId=<guid>` (or `?unscoped=true`) as the one read an orchestrator uses for effective admission limits, stage occupancy, host capacity and runner placement. First deliver the small scoped `absoluteLimit`/`openCount` addition to pipeline. Last, **mandatorily**, replace CARD-0880's multi-route instructions in the actual orchestrator bundle and every policy copy, with a net-shorter bundle change.

This is the READ half of CARD-0505. Its revisioned store, resolver, LegacyOpen/SeparateQueues modes, enforcement, PUT/history APIs and audit authority remain the only owners of concurrency settings. Land this after CARD-0505, or as ordered slices of the same coordinated delivery. No setting value, admission predicate, refusal precedence, override permission, queue behavior or host budget changes here. No new settings writer, table, migration, provider launch, UI, deployment or instruction-notification transport.

The response states what the gates would decide against its snapshot. It is not a reservation: another create, settings write, runner disconnect or dispatch can occur after GET. The acceptance promise is **no missing concurrency information**, not an impossible guarantee against all later 409s. Scope collisions, routing, provider authentication/quota, platform requirements and worktree preparation can still hold/refuse work. Clients re-read after a refusal and defer; GET grants no override authority.

## Evidence and limits

Read the full assigned brief and CARD-0505 card, its landed Plan/TestDesign artifact, CARD-0884, CARD-0835, CARD-0788, CARD-0822, CARD-0826, CARD-0778 and the eight named flake cards through `scripts/card.ps1 get ... -Board Antiphon`. Used CARD-0826 and CARD-0849 plans as the format/checkpoint examples. Read the project, testing/build and orchestration/HTTP owners before designing their changes. All source and live measurements below were read-only. No builds/tests were executed in Plan.

| Measured fact | Evidence at the baseline | Design consequence |
|---|---|---|
| Create population | `DelegationOpenGate`: Queued + Dispatched + Working, `AgentTaskRoles.NotSpecialist`, exact `AgentTask.ProjectId` equality; null is its own bucket. Gate takes the transaction advisory key before counting and insertion. | Share its predicate/policy projection; never use pipeline fleet in-flight or a board's rows as a project count. Blocked is visible but excluded. |
| Existing limits | `DelegationSettings`: MaxOpenTasks 6, MaxConcurrentTasks 2; Code/Review role defaults 2, other configured roles 1, Custom null. | Defaults are not a measurement of the running configuration. Keep host and project limits separate. |
| Pipeline | `AgentTaskPipelineStatusService.GetAsync`: fleet arrays, 14 non-specialist roles, Dispatched/Working in-flight; Queued and Blocked separate; ready is a derived card/handoff population. | A ready card does not consume admission slots. Preserve fleet compatibility and CARD-0505's scoped extension. |
| Host projection | `HostBudgetService.Limit`: local budget else MaxConcurrentTasks; remote min(persisted budget, runner declaration), or the non-null operand. `HostEndpoints.ProjectAsync` and pipeline duplicate occupancy queries. | Reuse/extract their read projection, preserve the asymmetrical local/remote predicates, report both operands and the binding source(s). |
| Remote occupancy | Created/Starting/Running/Stopping sessions + Queued tasks with remote worktree and no session + `RemoteWorkspacePreparer.InFlightCount(host, Guid.Empty)`. Includes seats used outside this project and by specialists. | Host occupancy is fleet-wide; a project filter cannot create physical seats. This is not the project open count. |
| Local occupancy | Non-specialist Dispatched/Working, null/empty RunnerId, excluding CapacityWaitRetained. Catalogue calls it `delegatedTasks`, not physical seats. | Preserve that capacity kind. Retained tasks still consume project WIP. Map host `local` explicitly to catalogue runner `desktop`. |
| Runner eligibility | `SessionRunnerCatalogue` exposes availability, stale, dispatchEligible, draining, acceptingNewWork, platform and features; one remote failure retains the other rows. | An eligible connection alone is insufficient: a drained/retired runner accepts no new work. Unknown values stay null. |
| Default-reader trap | `RunnerDefaultSettingsService.GetAsync` calls EnsureInitializedAsync. `ReadSnapshotAsync` is already nonmutating; private ProjectAsync provides the existing DTO. | The aggregate GET must use a noninitializing read, not call the existing GET method and accidentally seed a row. |
| CARD-0505 contract | Its D-1/D-4/D-5 separate open, queued and parallel populations; default/global/project sources; consistent revisions; scoped pipeline and `concurrencyScopes`; queue 409 precedence. | Consume that resolver. The card's stale original absolute-3/per-role-1 wording is explicitly superseded by its 2026-10-01 update. |
| Provenance gap | CARD-0505 specifies `seedOrigin: startupConfiguration`, but does not distinguish absent code defaults from explicitly bound config leaves. Equal numeric values cannot establish source. | Add seed-origin metadata through its existing seed path, not a second store or an inference from value equality. |
| Bundle | LF-normalized, trimmed `orchestrator.md`: 14,146 characters; SHA-256 first eight lowercase hex = `bac5c61d`. `InstructionBundles.ReadNormalised/HashOf` computes it on embedded-resource load. | Editing the Markdown and rebuilding regenerates the version; there is no manually maintained hash file. |
| CARD-0884 budget | Its review measured composed worst case 29,805; guard budget 30,000; argument estimate adds about 124, leaving about 71. | The raw 195-character text margin is not launch headroom. Do not raise the budget or weaken the guard. |

Fresh API measurement at **2026-10-01T09:53:11Z**, via ANTIPHON_API and the task-token header without printing credentials:

- `/api/version`: `56dce415c0e273e7d4f0c23ada86ff5da3e0045d`, capabilities `land-v2`, `operator-shutdown-v1`. This differs from source HEAD; source publication is not activation.
- Pipeline: local 0/2; server2 6/10; Code recommendation 5, in-flight 2, queued 0, ready 4; Plan 3/3; Review recommendation 5; Investigate recommendation 3; TestDesign/Mutation 1. These are fleet observations, not project headroom.
- `/api/session-runners`: desktop Windows 0/2 and server2 Linux 6/10 available/eligible/accepting; server2-temp unavailable, stale, draining, capacity and occupancy null.
- `/api/runner-defaults`: revision 2, Human, global server2, no kind overrides. `/api/hosts`: no persisted budgets; server2 declaration/effective 10, in-flight 6.
- No route in that running build discloses absolute admission limit/open count. No speculative create was sent to infer it.

Reproduction: `git status --short`, `git rev-parse HEAD origin/master`, `git ls-remote origin refs/heads/master`; read the named files with `rg`/`sed`; use `pwsh -NoProfile -File scripts/card.ps1 get CARD-0505 -Board Antiphon`; read the five named GETs with `Invoke-RestMethod`, selecting only the fields above. Parenthesize/assign API arrays before piping (CARD-0546). Do not dump environment, configuration providers, connection strings or tokens. Live measurements are non-atomic historical observations, not execution authorization.

## Design decisions

### D-1: explicit scope; additive pipeline first

Adopt CARD-0505's mutually exclusive `projectId=<guid>` and `unscoped=true` selectors. A known project with no tasks returns its effective policy and zero counts. Unknown project is 404 `not_found`; malformed/conflicting selectors are 422 `validation_failed`. `boardId` is deliberately unsupported: a board is not the gate's project scope. Reject unknown query keys with 400 `unknown_query_parameter` instead of silently treating a misspelled selector as the fleet.

For **pipeline only**, no selector retains fleet arrays. Add `scope` (null for fleet), nullable top-level `absoluteLimit` and `openCount` (both null for fleet); each CARD-0505 `concurrencyScopes` entry gains the same named fields and scope. Always include the null-project bucket; include represented projects, and an explicitly selected empty project. Do not enumerate every unused project. Scoped arrays/ready/backlog follow CARD-0505's existing predicate; host rows stay fleet. `openCount` is Queued + Dispatched + Working. `absoluteLimit` is the effective combined-open ceiling in LegacyOpen; **null in SeparateQueues**, where there is no combined-open ceiling. Its `maxParallel`/queued limits remain in the policy snapshot. This avoids renaming a running cap to MaxOpenTasks.

For **the new route**, exactly one selector is required (422 if absent). This intentionally prevents a caller from accidentally consuming a fleet count as project admission headroom. No implicit current-task/project inference. Reuse CARD-0505 scope validation; no new AgentTaskService create/list behavior.

Rejected: summing fleet open tasks under a single project cap; treating null scope as fleet; inventing board concurrency; duplicating CARD-0505's `concurrencyScopes` with a second incompatible scope list.

### D-2: one aggregate read contract, modes preserved

Use a new concrete `DispatchEffectiveSettingsService` and DTOs, exposed by `DispatchSettingsEndpoints`. Resolve one CARD-0505 effective policy/revision snapshot and one set of project task counts. Use a short read-only repeatable-read database snapshot for policy, counts, host budget rows and runner-default rows; no admission advisory lock, SaveChanges, initialization, event publication, runner mutation, Git or dispatch. Pass that snapshot into projection helpers; do not query the same population independently for each field. Capture runner/preparation observations once outside the DB transaction; do not hold the transaction across network I/O. No concurrent EF queries on one DbContext.

Response includes `asOf`, runner observation times, `consistency: snapshotNotReservation` and `schemaVersion: 1`. SQL facts are consistent within the read; in-memory preparation and runner state have separately observed freshness and cannot be made atomic by a read API. Limit sources/revisions are from the exact resolved object, never re-resolved while serializing. `CancellationToken` reaches every read. The aggregate database read remains single-attempt under docs/resilience.md; do not wrap its transaction in an admitted-read executor or broaden resilience allowlists. Desktop capability observation keeps its existing bounded read; cancellation or a read failure never becomes a successful empty SQL snapshot.

The exact shape is defined by the following specimen plus the type/cardinality rules beneath it. Numbers/IDs are synthetic examples, not new defaults. `stages` contains all 14 non-specialist AgentTaskRole values, exactly once; `isStage` identifies the six pipeline stages. A helper can consume the absolute budget, so omitting it would hide occupancy. `hosts` and `runners` contain every configured host/runner, including unavailable entries. No task lists, tokens, workspace paths, connection details or raw config provider values are returned.

```json
{
  "schemaVersion": 1,
  "asOf": "2026-10-01T10:00:00Z",
  "consistency": "snapshotNotReservation",
  "scope": { "kind": "project", "projectId": "11111111-1111-1111-1111-111111111111" },
  "mode": "LegacyOpen",
  "revisions": { "global": 3, "project": 2 },
  "absoluteLimit": 6,
  "openCount": 3,
  "absolute": {
    "limit": { "value": 6, "population": "open", "enforcedAt": "create", "source": { "kind": "runtimeOverride", "scope": "project", "key": "maxParallel", "revision": 2 } },
    "inFlightCount": 2,
    "queuedCount": 1,
    "freeSlots": 3,
    "queueLimit": { "value": null, "population": "queued", "enforcedAt": "create", "source": { "kind": "codeDefault", "scope": "seed", "key": "maxQueued", "revision": 1 } },
    "queueFreeSlots": null,
    "admissionFreeSlots": 3,
    "dispatchFreeSlots": null
  },
  "stages": [
    {
      "role": "Code", "isStage": true,
      "limit": { "value": 4, "population": "open", "enforcedAt": "create", "source": { "kind": "configuration", "scope": "seed", "key": "Delegation:RolePolicy:Code:RecommendedInFlight", "revision": 1 } },
      "openCount": 2, "inFlightCount": 1, "queuedCount": 1, "readyCount": 1,
      "freeSlots": 2,
      "queueLimit": { "value": null, "population": "queued", "enforcedAt": "create", "source": { "kind": "codeDefault", "scope": "seed", "key": "roles.Code.maxQueued", "revision": 1 } },
      "queueFreeSlots": null, "admissionFreeSlots": 2, "dispatchFreeSlots": null
    }
  ],
  "hosts": [
    {
      "hostId": "server2", "runnerId": "server2", "scope": "fleet",
      "configuredBudget": { "value": 6, "source": { "kind": "runtimeOverride", "scope": "host", "key": "HostBudgets.maxInFlight", "revision": 1 } },
      "declaredCapacity": { "value": 10, "source": { "kind": "runnerDeclared", "scope": "runner", "key": "capacity", "revision": null } },
      "fallbackLimit": null,
      "effectiveLimit": 6, "bindingSources": ["configuredBudget"],
      "budgetRevision": 1, "budgetUpdatedAt": "2026-10-01T09:00:00Z",
      "inFlight": 3,
      "occupiedBreakdown": { "delegatedTasks": 0, "sessions": 2, "pendingLaunch": 1, "inFlightMirrors": 0 },
      "taskOpenCount": 3, "taskInFlightCount": 2,
      "available": true, "dispatchEligible": true, "acceptingNewWork": true,
      "freeSlots": 3, "dispatchableSlots": 3
    }
  ],
  "runners": [
    {
      "runnerId": "server2", "hostId": "server2", "displayName": "server2",
      "platform": "linux", "platformObservedAt": "2026-10-01T09:59:59Z",
      "capacity": { "value": 10, "source": { "kind": "runnerDeclared", "scope": "runner", "key": "capacity", "revision": null } },
      "occupied": 3, "capacityKind": "sessions", "capacityObservedAt": "2026-10-01T09:59:59Z",
      "available": true, "dispatchEligible": true, "acceptingNewWork": true,
      "stale": false, "draining": false, "unavailableReason": null,
      "features": ["workspacePublishV1"]
    }
  ],
  "runnerDefaults": {
    "revision": 2, "globalRunnerId": "server2", "globalSource": "runtimeOverride",
    "kindDefaults": [ { "agentKind": "Codex", "runnerId": "server2", "inheritedRunnerId": "server2", "source": "KindDefault" } ],
    "supportedKinds": ["Grok", "ClaudeCode", "Codex"],
    "unresolvedReferences": []
  }
}
```

All keys above are mandatory; nullable values serialize as explicit JSON null. GUIDs are canonical strings; UTC timestamps use the server's normal JSON timestamp format. `scope.kind` is `project` or `unscoped`; the latter requires projectId null. Counts are nonnegative integers. `limit.value`, `queueLimit.value`, and derived slot counts allow null for an unbounded axis; `absolute.limit.value` stays finite under CARD-0505. `limit.population`/`enforcedAt` become `parallel`/`dispatch` in SeparateQueues. `absoluteLimit` then becomes null, while `openCount` remains diagnostic. Explicit queue zero means no fresh admission. Existing overages remain visible as counts greater than limits with slots clamped to zero.

`runnerDefaults.kindDefaults` preserves the existing DTO's explicit override rows and `source` vocabulary; it is not required to list inherited-only kinds. The illustrative Codex row represents an explicit stored override equal to the global choice, and therefore uses the existing store's `KindDefault` source (see D-4). No platform-default field is invented. Resolve a kind absent from the array via globalRunnerId; unresolved references remain visible, never silently replaced by server2. `globalSource` is `runtimeOverride`, `configuration`, `codeDefault` or `unknown` using the existing revision provenance/import metadata.

Rejected: server-side HTTP fan-out to the existing routes; an enlarged full pipeline payload as the only settings API; a promise of launch success; labels that treat SeparateQueues parallel saturation as create-time 409.

### D-3: slot arithmetic and host semantics

Define `remaining(limit,count) = null` for unbounded, else `max(0, limit-count)`. The minimum of nullable capacities treats null as infinity, but all-null is null. Unknown runner capacity is **not** unbounded operational capacity.

- `open = queued + inFlight`, where inFlight = Dispatched + Working, across all runners in the selected project/null bucket. Specialists, Blocked, terminal and ready rows are excluded. Retained capacity-wait tasks still count here.
- Each `freeSlots` uses its limit's named population. `queueFreeSlots` uses Queued. Absolute admissionFreeSlots is min(open-free, queue-free) in LegacyOpen, queue-free in SeparateQueues. Stage admissionFreeSlots also includes both absolute admission constraints. This is the maximum additional ordinary queued creates against an unchanged snapshot; it is not summed across stages because they share the absolute budget.
- `dispatchFreeSlots` is null in LegacyOpen (no independent project running gate). In SeparateQueues it is remaining(maxParallel,inFlight); the stage field is min(absolute running-free, role running-free). Queued admission and running headroom are independent. Existing queued tasks need no new admission slot.
- Host `freeSlots` is remaining(effectiveLimit,inFlight). Local effective = budget if present, otherwise configured/code MaxConcurrentTasks; remote = min of present budget/declaration. For a tie, bindingSources lists both; a budget larger than a runner declaration is **not** the binding source. `fallbackLimit` is the local MaxConcurrentTasks sourced value; null remotely. An absent configured budget is `{value:null,source:null}`. A missing declaration is `{value:null,source:null}`. Unknown effective host capacity yields freeSlots null.
- `dispatchableSlots` is zero if unavailable, stale, draining, retired, not accepting, or headroom cannot be established; otherwise host freeSlots. Preserve separate eligibility flags from the catalogue. Hosts report known DB occupancy even when a runner is offline; runner occupied/capacity observation can be null. Remote pending/mirror counts are additive exactly as the current gate reports, not deduplicated by a new policy. Local occupiedBreakdown.delegatedTasks explains its count; the other local terms are zero.
- Host taskOpenCount/taskInFlightCount are fleet non-specialist tasks assigned to that host, with the project population definitions above. They expose task totals separately from session/prepare seats. Normalize only the existing desktop aliases to local/desktop; never combine server2-temp into server2. Task rows with no resolved remote runner remain local under current behavior.
- A conservative number of fresh tasks of a stage to start on host H is min(stage.admissionFreeSlots, stage.dispatchFreeSlots, host.dispatchableSlots), then bounded by operator policy and existing queues. A pre-existing queue has priority; the numbers are upper bounds, not an allocation across different stages. ReadyCount is for Code feed-depth policy, never for the gate's arithmetic.

The policy overlay remains up to four concurrent tasks per stage and at most six running tasks on server2 across stages unless the operator says otherwise. Use min(4, any finite effective stage cap); lower hard limits always win. Server2 running headroom is max(0, 6-taskInFlightCount), additionally bounded by dispatchableSlots. Before commissioning fresh tasks, reserve capacity for its already assigned queued successors: using max(0, 6-taskOpenCount) is a conservative scheduling calculation, not a new server queue/admission limit. Code feed depth additionally counts ready. These operator ceilings are prose policy, not invented enforced server settings; a larger API limit is not permission to spend more. For task-specific routing/provider/scope decisions use their existing checks.

Rejected: treating the desktop catalogue's capacity as a physical-seat counter; subtracting project tasks from a fleet host budget; using ready/Blocked to reduce admission capacity; adding host free slots across stages; silently normalizing unknown/offline to unlimited.

### D-4: report provenance honestly, once

Common source object: `{kind,scope,key,revision}`. `kind` is `codeDefault|configuration|runtimeOverride|runnerDeclared|unknown`; `scope` is `seed|global|project|host|runner`; key is an allowlisted setting/field name, never a provider value/path; revision is nullable integer. Override source is the effective field's global/project revision, not merely the existence of a project settings row. Explicit nullable overrides keep their source. Empty project overrides inherit the global source. Host min operands retain independent provenance; bindingSources refers to operand keys.

Add typed startup-origin metadata captured in Program at configuration binding, where IConfiguration is allowed: check explicit leaf presence, including explicit null overrides, rather than comparing the bound value with a newly constructed default. Pass only known key/source labels to services. Extend CARD-0505's existing SeedJson metadata using its existing one-time initialization path so origin survives later startup-config changes. No value changes, reseeding, extra audit revision, new initialization-on-GET, settings migration or second writer. This is a required small contract amendment to CARD-0505 before/with its initial delivery. Already seeded historical rows lacking provenance report `unknown`; do not claim to reconstruct history. Tests must cover both fresh accurate code/config origin and honest historical unknown.

Reuse the runtime resolver's `default/global/project` precedence; adapt its source labels to the wire vocabulary above. Add a noninitializing RunnerDefaultSettingsService read for the aggregate; missing initialized settings return 503 `dispatch_settings_uninitialized` through HttpException. Startup remains the initialization owner. Imported placement metadata can be unknown if the old store did not preserve presence; do not classify Migration as Human. Preserve existing kind source values by reusing ProjectAsync's mapping.

Rejected: a parallel config store; returning raw IConfiguration; calling EnsureInitializedAsync from GET; calling every seed `codeDefault`; inferring origin from a value equal to its shipped default.

### D-5: mandatory final policy slice and CARD-0822

S4 is the last slice and cannot be dropped. Replace the multi-GET wording in `server/Bundles/orchestrator.md`, `AGENTS.md`, `docs/orchestration-loop.md` section 1, `.claude/skills/antiphon-orchestrator/SKILL.md`; update `docs/ops-http.md`, `docs/antiphon-api.md` and the pins in StandingPipelinePolicyDocumentationTests. Discover mirrors with `rg -n 'three-route|CARD-0881|effective concurrency limits|GET /api/runner-defaults' AGENTS.md docs server/Bundles .claude` and edit policy mirrors, not historical plans/investigations or generated docs/cards. CLAUDE.md is only an import pointer, not another prose copy.

Policy must say **read the effective-settings endpoint and use its limits**, with the explicit scope, min-bound operator defaults, server2 preference, desktop shielding, Code depth including ready, per-task Worktree and same-source-area deferral retained. Preserve the absolute-axis override exception only when `population=open`, `canOverride=true` and no same-stage occupant; queue or role saturation defers. If the 409 occupant list is truncated, confirm zero same-stage open tasks from the scoped read; absence from a capped list is not absence of an occupant. No independent GET /api/hosts prerequisite remains. Host budget PUT stays documented as an operator action.

Use a net-shorter replacement, measured against the exact policy block on the Code base, not just this historical file size. Do not change CommandLineBudgetChars, remove argument accounting, narrow the composition test or hand-edit versions. Rebuild embeds the edited text and InstructionBundles computes the new content hash. Record before/after normalized block length, full bundle length, computed version and actual worst-case composition plus argument estimate. CARD-0884's broader transport/headroom redesign remains separate.

CARD-0822's generated file must consume the **same effective settings projection** for caps, modes, sources, host limits and defaults. Its existing plan builds those directly from DelegationSettings and several services; amend its `OrchestratorInstructionsSnapshotBuilder` contract to use this service's stable policy component. Do not put occupancy/asOf in the generated file's content hash: those would produce notices on every task transition. Its file tells orchestrators to GET the scoped live route before dispatch; its global file must not present a single project override as fleet policy. CARD-0822 owns file lifecycle, setting-change signals and delivery receipts. It is currently held/unimplemented at this baseline: the dependency is CARD-0505 -> CARD-0881 -> CARD-0822; there are no imaginary generated files to edit here. If that ordering changes, amend the exact footprint and checkpoint roster for its landed builder/renderer before Code, rather than silently shipping duplicate settings sources.

Rejected: updating only docs and leaving the delivered bundle stale; adding long JSON examples to argv; generating numbers from separate settings reads; repurposing this read-only card into CARD-0822's notification pipeline.

### D-6: concrete net-shorter bundle edit

Use this replacement for the current standing-policy prefix, from `When you are working a board` up to (not including) `On every completion`. Keep the completion, landing, retrospective and same-area paragraphs after it; make the two small substitutions below.

```text
When you are working a board through its pipeline, this is the standing policy unless the user
says otherwise this session. Read GET /api/settings/dispatch?projectId=<id> (or ?unscoped=true),
the effective-settings endpoint, and use its limits, occupancy and free slots before dispatch.
Operator defaults where Antiphon sets none: every stage at up to four; at most six tasks on
server2 across stages. Use the lower effective stage cap; Antiphon's limits are the ceiling.
Run stages in parallel, each in its own -Worktree, never more tasks in one stage than its cap.
Prefer server2 (-Runner server2); use desktop/Windows only when work absolutely requires it,
scoped to that piece. Host budget writes require an operator request.
```

Change `in flight, queued and ready from GET /api/agent-tasks/pipeline.` to `in flight, queued and ready in that snapshot.`. Replace the existing absolute-axis condition with this exact text (ordinary Markdown backticks, not escaped bytes):

```text
only for `axis: absolute`, `population: open`, `canOverride: true` and no same-stage occupant;
```

Replace the Platform paragraph prefix up to, but not including, `Omit -Platform:` with:

```text
Platform: use runnerDefaults and runners from the effective-settings read. Do not embed a fleet location. Normally omit -Runner; the runtime default places the task. Pin the operator's server2 preference with -Runner server2 only when eligible.
```

Append one space when joining the unchanged remainder of the Platform paragraph; preserve its platform inheritance/unpinning instructions and operator-only settings-write procedure. The remaining GET/PUT runner-defaults reference describes a user-requested write workflow, not another prerequisite to ordinary dispatch.

Read-only string replacement against the frozen source measured the standing prefix at **922 -> 730** characters, Platform prefix **363 -> 245**, Code-count sentence saving **18**, and override condition adding **9**: net **319** characters removed. The full normalized bundle would be **14,146 -> 13,827**. This is a lexical measurement, not an executed composition test. CARD-0505/0835 may change surrounding content, so Code must measure its actual base and still satisfy net-shorter plus the unchanged full composition/argument guard. Do not discard any unrelated instruction to hit a historical number.

Rejected: leaving the separate Platform prerequisite in place; adding the detailed API schema to the prompt; treating a text-length calculation as proof that the runtime argv guard passed.

## Exact implementation footprint and slices

This task edits only this plan. The following is the future Code footprint; prerequisites' code is not present merely because their plans are on master. Do not edit generated `docs/cards/`.

| Slice | Exact files (relative to repository root) | Test-first result |
|---|---|---|
| S1, smallest first: scoped pipeline admission facts | `server/Application/Dtos/AgentTaskPipelineDtos.cs`; `server/Application/Services/AgentTaskPipelineStatusService.cs`; `server/Api/Endpoints/AgentTaskEndpoints.cs`; **new** `tests/Antiphon.Tests/Application/PipelineAdmissionSnapshotTests.cs`; **new** `tests/Antiphon.Tests/TestHelpers/DispatchEffectiveSettingsTestHost.cs` | Add named wire/gate assertions before projection fields. Reuse CARD-0505's scope resolver, policy and population definitions. Do not expose an unlocked create operation or change DelegationOpenGate enforcement to make reads convenient. |
| S2: common effective projection, origins, host reads | **new** `server/Application/Dtos/DispatchEffectiveSettingsDtos.cs`; **new** `server/Application/Services/DispatchEffectiveSettingsService.cs`; **new** `server/Application/Settings/DispatchConcurrencySeedOrigins.cs`; CARD-0505's `server/Application/Dtos/DispatchConcurrencyDtos.cs`, `server/Application/Services/DispatchConcurrencySettingsService.cs`; **new** `server/Application/Services/HostDispatchSnapshotService.cs`; `server/Application/Services/HostBudgetService.cs`; `server/Application/Services/RunnerDefaultSettingsService.cs`; `server/Application/Services/SessionRunnerCatalogue.cs`; `server/Api/Endpoints/HostEndpoints.cs`; `server/Program.cs`; **new** `tests/Antiphon.Tests/Application/DispatchEffectiveSettingsProjectionTests.cs`; S1 helper/pipeline service | Source and arithmetic tests first; extract existing host projection without changing its predicates; optional catalogue projection overload accepts captured counts and an injected observation time while old callers preserve their contract. Origins use CARD-0505's existing SeedJson writer. Local fallback uses typed startup-origin metadata. |
| S3: one HTTP route and boundary evidence | **new** `server/Api/Endpoints/DispatchSettingsEndpoints.cs`; `server/Program.cs` route registration; **new** `tests/Antiphon.Tests/Application/DispatchEffectiveSettingsWireTests.cs`; S1 helper and S2 DTO/service as required by boundary failures | Real HTTP serialization, validation, cancellation and no-write assertions, and actual isolated admission/dispatch decisions agree with the response. No self-HTTP fan-out. |
| S4, **S-last, mandatory**: delivered policy, docs and pins | `server/Bundles/orchestrator.md`; `AGENTS.md`; `docs/orchestration-loop.md` section 1, the WIP defaults paragraph around baseline line 613, and its duplicated platform-read sentence; `docs/ops-http.md`; `docs/antiphon-api.md`; `.claude/skills/antiphon-orchestrator/SKILL.md`; `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`; `tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs` (contains RunnerDefaultGuidanceTests too) | Pin the new scoped route and retained policy invariants first; remove multi-route prerequisites in **both** the standing-policy and Platform paragraphs. Document the exact shape, all populations/sources and snapshot caveat. Remove the WIP paragraph's stale claims that the absolute limit is readable only in a 409 and that role settings have no runtime API; point to CARD-0505 for audited writes. New route appears before policy switches. Normal build regenerates bundle hashes; no hash constant edit. |

Existing gate, policy, host, runner-default and bundle test classes below are read/run as regressions; changing their setup only to wire a required constructor is permitted in these exact files: `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`, `tests/Antiphon.Tests/Application/HostEndpointTests.cs`, `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs`. Retain assertions. Any additional production file, new migration, changed enforcement predicate or CARD-0822 integration file requires an explicit plan amendment before Code.

**Source-area scope (future Code):** `server/Application/Dtos/AgentTaskPipelineDtos.cs,server/Application/Dtos/DispatchEffectiveSettingsDtos.cs,server/Application/Dtos/DispatchConcurrencyDtos.cs,server/Application/Services/AgentTaskPipelineStatusService.cs,server/Application/Services/DispatchEffectiveSettingsService.cs,server/Application/Services/DispatchConcurrencySettingsService.cs,server/Application/Services/HostDispatchSnapshotService.cs,server/Application/Services/HostBudgetService.cs,server/Application/Services/RunnerDefaultSettingsService.cs,server/Application/Services/SessionRunnerCatalogue.cs,server/Application/Settings/DispatchConcurrencySeedOrigins.cs,server/Api/Endpoints/AgentTaskEndpoints.cs,server/Api/Endpoints/HostEndpoints.cs,server/Api/Endpoints/DispatchSettingsEndpoints.cs,server/Program.cs,server/Bundles/orchestrator.md,AGENTS.md,docs/orchestration-loop.md,docs/ops-http.md,docs/antiphon-api.md,.claude/skills/antiphon-orchestrator/SKILL.md,tests/Antiphon.Tests/Application/PipelineAdmissionSnapshotTests.cs,tests/Antiphon.Tests/Application/DispatchEffectiveSettingsProjectionTests.cs,tests/Antiphon.Tests/Application/DispatchEffectiveSettingsWireTests.cs,tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs,tests/Antiphon.Tests/Application/HostEndpointTests.cs,tests/Antiphon.Tests/Application/RunnerDefaultTests.cs,tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs,tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs,tests/Antiphon.Tests/TestHelpers/DispatchEffectiveSettingsTestHost.cs`.

### Collision and landing order

| Other card | Exact intersection / boundary | Dispatch decision |
|---|---|---|
| CARD-0505 | Pipeline service/DTO/endpoint; DispatchConcurrencyDtos/SettingsService; Program; pipeline test setup; orchestrator bundle; ops/API/orchestration docs. It also touches AgentTaskService, AgentTaskDtos, gate/dispatcher and EF, which this plan deliberately does not. | Hard prerequisite: land its implementation and seed-origin amendment first, or commission one combined serialized owner with S1 here after its store/gates. Re-read source and recount tests after land. Its older two-per-stage policy paragraph is superseded by fccef27e and this S4; do not resurrect it. |
| CARD-0835 | Shared docs `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/antiphon-api.md`. Its changed `delegate-basics.md` also affects the composition-budget regression. No direct edit here to its tools, scripts, AgentTaskService, AgentTaskReplyService, LandApproval, AgentTaskDtos, model snapshot or migrations. CARD-0505 itself overlaps its AgentTaskService work. | **No: do not start this card's normal all-slices Code before CARD-0835 lands.** This avoids required final-doc collisions and the transitive CARD-0505 collision. Plan/TestDesign can finish now. After land use the new receipt contract; the brief's commit-before-each-run rule applies immediately. |
| CARD-0778 | Its `tests/Antiphon.Tests/Agents` fixtures and `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs` are outside the footprint. | No source collision and no Windows need here. Share host build slots only. |
| CARD-0788, held Code-ready | `docs/ops-http.md`, `docs/orchestration-loop.md`; none of its land/base/reply services are edited here. | Serialize if admitted first; keep it held while this Code owns those paths. Separate sections do not waive the same-file collision rule. |
| CARD-0822, held Code-ready | Program; HostBudgetService, RunnerDefaultSettingsService signals; bundle, AGENTS, orchestration/ops docs, skill and StandingPipelinePolicyDocumentationTests. New builder is currently absent. | Keep behind CARD-0881. Amend its builder to consume this projection as D-5; any reversed order requires footprint/tests for an actual adapter, not an unimplemented promise. |
| CARD-0826, held Code-ready | `server/Program.cs`, `docs/ops-http.md`. No cleanup, EF, checkpoint, stage-code/review bundle or runner-file edits here. | Do not run concurrently with its Code on these paths. Operator chooses which goes first after CARD-0835; re-read Program registration changes. |
| CARD-0880 | Baseline fccef27e already contains its policy and phrase pins. | Landed prerequisite satisfied. S4 explicitly replaces its interim three-route rule. |
| CARD-0884 | This card edits the orchestrator bundle whose headroom it reports. | No dependency on its redesign; preserve the guard and shorten text. Serialize any concurrent bundle redesign. |

Before actual dispatch, the orchestrator re-reads current scoped task occupancy and checks these exact paths; card column status alone does not establish a landed implementation. No delegation, card writes or runtime settings edits were performed by this Plan.

## Verification design

### Fixtures, boundaries and test-first order

Use TUnit through `dotnet run --project tests/Antiphon.Tests`, driven by `scripts/run-checkpoint.ps1`. No real provider or production runner launches. Use the existing isolated PostgreSQL schema fixture, CARD-0505's fake dispatch/admission host and `DelegationTestServices` registration; add the narrow aggregate route to that host. Do not boot real Program without its isolated-runner guard. Pure arithmetic/source tests may use immutable fixture records, but scope, database populations, HTTP JSON and 409 comparisons must cross the real service/EF/endpoint boundary. A stub response or an expected value derived by the same projection is insufficient.

Use `FakeTimeProvider` at a fixed UTC instant for asOf, source timestamps and runner staleness. Advance explicitly across the configured stale boundary; no 20–50 ms margins or sleep-based assertions. Use awaited task gates for snapshot/read-versus-write ordering and release/join them in finally. A harness timeout is infrastructure failure, never a positive-control red. Include a cancellation token and a gated reader for cancellation propagation.

This API creates no asynchronous delivery obligation. Its producer is the HTTP request, destination the requesting orchestrator, persistence the existing task/settings/host tables, and observable receipt the JSON response with scope/revisions/asOf. Reads neither persist a new snapshot nor enqueue a notice. CARD-0822's later file/notice producer and transcript receipt remain that card's responsibility.

For each slice: implement and commit the named assertions/scaffold; run that slice's exact CP row as a declared preparatory red and record expected assertion failures; implement and commit/push the production change. S1-S3 form one final runtime verification group: run CP-1, CP-2 and CP-3 green only after all three are committed, so the S2 shared-host extraction cannot leave S1 with stale evidence. S4 then runs CP-4 green. Preparatory red invocations use the same exact row/filter before its final After group, are explicitly authorized here, and are reported as red-round reruns. If absent symbols initially prevent compilation, first add minimal callable scaffolding returning incomplete values, then require the named assertion to fail; a compile error does not count as a useful red. S2/S3 use the actual resolved CARD-0505 provider, not a replacement test-only rules engine. S4 red-first edits the pins before policy text. Commit before every invocation; never edit while a driver owns the worktree. Record every red/fix rerun. Deliberate production mutations are post-land Mutation work, not ordinary Code.

### Required named tests and field coverage

The following are **proposed individual `[Test]` methods**, each one execution and no Arguments/Repeat/OS skip. Counts are a design roster, not tests claimed to exist or pass. TestDesign must reconcile it against landed CARD-0505 and then Code must recount the implemented source and fresh TRX; a changed roster requires a committed table amendment.

| ID | New class and exact method | Required independently asserted result |
|---|---|---|
| V-01 | PipelineAdmissionSnapshotTests.Scoped_pipeline_reports_project_limit_and_open_count | Project A has Queued/Dispatched/Working across local+server2: absoluteLimit 6, openCount 3, scope A; unrelated project B cannot change A's count. |
| V-02 | PipelineAdmissionSnapshotTests.Blocked_and_specialists_do_not_consume_open | One counted row plus Blocked/terminal and Check/Distill/Diagnose in every open status gives openCount 1. Check every excluded enum and all three included statuses with explicit expected IDs/counts. |
| V-03 | PipelineAdmissionSnapshotTests.Null_project_has_own_bucket | Null project and two named projects do not bleed; pipeline `?unscoped=true` counts only null. |
| V-04 | PipelineAdmissionSnapshotTests.Empty_project_returns_effective_policy | Existing empty project returns its inherited/overridden cap, count zero; represented scopes include the null bucket. |
| V-05 | PipelineAdmissionSnapshotTests.Fleet_preserves_arrays_and_labels_each_scope | No-query retains fleet ready/backlog/task arrays and old fields; top scope/absoluteLimit/openCount null; per-scope facts named, never fleet count versus project cap. |
| V-06 | PipelineAdmissionSnapshotTests.Unknown_project_is_not_found | Unknown GUID gives 404 not_found and no fallback policy. |
| V-07 | PipelineAdmissionSnapshotTests.Conflicting_scope_refuses | Conflicting selector, malformed GUID, unsupported boardId/typo keys get documented 422/400; no silently widened query. |
| V-08 | PipelineAdmissionSnapshotTests.SeparateQueues_does_not_claim_open_cap | Mode SeparateQueues keeps openCount but absoluteLimit null and correct parallel/queued effective limits. |
| V-09 | PipelineAdmissionSnapshotTests.Absolute_409_matches_scoped_read | With fixed DB/policy, real fresh CreateAsync refusal axis absolute has count/limit/project/population/source/revisions matching the scoped read. Also both-full keeps absolute precedence; no task inserted. |
| V-10 | PipelineAdmissionSnapshotTests.Role_409_matches_scoped_read | Same for role axis, below absolute cap; Custom null role cap does not spuriously refuse; override exemption behavior remains CARD-0505's. |
| V-11 | DispatchEffectiveSettingsProjectionTests.Code_default_and_explicit_config_have_distinct_sources | Two equal numeric values, one absent config leaf and one explicit, produce codeDefault vs configuration; include explicit-null role leaf, key and persisted seed revision. Restart with different config retains seed origin/value. |
| V-12 | DispatchEffectiveSettingsProjectionTests.Override_precedence_and_clear_keep_field_sources | Global and project overrides, inherited fields, explicit nullable role/queue and empty project overrides report exact field values/source/revisions. Project row presence does not change inherited source. |
| V-13 | DispatchEffectiveSettingsProjectionTests.Historical_seed_origin_stays_unknown | Seed without origin metadata stays unknown; no current-config or equal-value inference, no revision/value update during read. |
| V-14 | DispatchEffectiveSettingsProjectionTests.Legacy_open_headroom_counts_queue_but_not_ready | Synthetic project open 3/6, Code open 2/4 yields absolute free 3 and Code free/admission 2; queued and ready counted separately; dispatchFreeSlots null. All roles/isStage correct. |
| V-15 | DispatchEffectiveSettingsProjectionTests.Separate_queue_and_parallel_axes_are_independent | Parallel full with queued headroom reports admission >0 and dispatch 0; project/role queue zero gives admission 0; null preserves unbounded; min across role/project, without summing shared capacity. |
| V-16 | DispatchEffectiveSettingsProjectionTests.Over_limit_counts_remain_visible_and_free_slots_clamp | Lower limit below counted work preserves counts/statuses, free zero, null distinguished from zero; LegacyOpen and SeparateQueues. |
| V-17 | DispatchEffectiveSettingsProjectionTests.Remote_host_minimum_reports_both_operands_and_binding_sources | Budget below/equal/above declared, no budget, no declaration, both missing and zero budget; effective arithmetic and exact bindingSources, revisions/timestamp/provenance. No limit/declaration mutation. |
| V-18 | DispatchEffectiveSettingsProjectionTests.Local_retained_work_consumes_project_but_not_host | Local budget vs fallback origin; exclude retained from local host, retain in project/open/task counts, correct local/desktop identity and delegatedTasks breakdown. |
| V-19 | DispatchEffectiveSettingsProjectionTests.Remote_host_counts_all_projects_sessions_pending_and_mirrors | Fixture session in each active status, exited control, prepared no-session queued row, in-memory preparation, specialist session and two projects; exact fleet sum and separate taskOpen/taskInFlight counts. No project filter on host occupancy. |
| V-20 | DispatchEffectiveSettingsProjectionTests.Runner_freshness_and_drain_prevent_new_dispatch | Controlled clock crossing stale boundary; unavailable, recovering, drained and retired entries keep flags/reasons, unknown values null, dispatchable zero; fresh eligible positive control has nonzero headroom. |
| V-21 | DispatchEffectiveSettingsProjectionTests.Placement_defaults_and_aliases_preserve_existing_contract | Revision, global source, explicit KindDefault row, inheritedRunnerId, supportedKinds, unresolvedReferences; local/desktop mapping; server2-temp distinct. Defaults never invent a platform mapping. |
| V-22 | DispatchEffectiveSettingsProjectionTests.One_read_uses_one_policy_revision_and_one_count_snapshot | Pause after the policy read; a second context atomically commits changed policy and task occupancy; resume counts. Assert old policy, old counts and one complete revision pair from the reader's repeatable-read snapshot. Single captured runner/prep inputs; asOf and observed timestamps use controlled clock. |
| V-23 | DispatchEffectiveSettingsWireTests.Full_response_serializes_every_documented_field | Real GET over seeded fixture asserts exact property sets/types recursively, all Limit/Source fields, scope/mode/revisions, absolute/stages/hosts/runners/defaults, enum spelling, counts and timestamps; assert all 14 roles, every configured host/runner. Expected literals do not call the production calculator. |
| V-24 | DispatchEffectiveSettingsWireTests.Scope_validation_and_empty_scope_are_explicit | New route: missing/both/malformed/board/unknown-key/unknown-project responses as D-1; known empty and null bucket succeed. |
| V-25 | DispatchEffectiveSettingsWireTests.Null_and_zero_survive_json_serialization | Explicit null keys are present for unbounded, unknown, optional timestamp/source; zero capacity is numeric zero with dispatchable zero; SeparateQueues absoluteLimit null. |
| V-26 | DispatchEffectiveSettingsWireTests.Read_changes_no_rows_events_pins_or_settings | Two reads: row values/counts/revisions, task statuses, budget/declaration, source seed, expired pin and audit/event sinks unchanged; no SaveChanges, initialization, notification, launch, input, stop or runner mutation. |
| V-27 | DispatchEffectiveSettingsWireTests.Uninitialized_settings_return_503_without_seeding | Missing concurrency store and missing runner-default row independently produce dispatch_settings_uninitialized and leave zero inserted rows/history. Initialized positive case succeeds. |
| V-28 | DispatchEffectiveSettingsWireTests.Reported_admission_headroom_fills_then_refuses | From fixed snapshot admit exactly the reported ordinary creates into Queued, next create refuses; separate absolute-bound and role-bound scenarios; assert status/count and 409 fields, no provider launch. |
| V-29 | DispatchEffectiveSettingsWireTests.Queue_409_axes_and_precedence_match_reported_sources | CARD-0505 LegacyOpen+queue and SeparateQueues fixtures: project/role queue limits, queue-before-open and absolute-before-role precedence; population/count/limit/source/revisions match, canOverride false for queue. |
| V-30 | DispatchEffectiveSettingsWireTests.Parallel_full_holds_accepted_work_without_create_409 | SeparateQueues GET says admission available/dispatch zero; real isolated create succeeds Queued, dispatch fake holds with matching project/role parallel facts, no launch. Releasing occupant permits the same accepted task via CARD-0505 harness. |
| V-31 | DispatchEffectiveSettingsWireTests.Committed_override_appears_on_next_read | Existing revisioned PUT in isolated host changes no more than its own store; next aggregate/pipeline/gate read agrees without restart, clear restores inherited metadata. CARD-0881 adds no PUT. |
| V-32 | DispatchEffectiveSettingsWireTests.Failed_runner_keeps_other_rows_and_null_facts | One catalogue failure retains desktop/server2 and failed entry with unknown facts/reason; no misleading positive dispatchable count. |
| V-33 | DispatchEffectiveSettingsWireTests.Request_cancellation_reaches_pending_reader | Cancellation of a gated read propagates token and joins work, no response cached as valid zero and no mutation. |
| V-34 | DispatchEffectiveSettingsWireTests.Http_snapshot_carries_real_revision_and_freshness | Gated write/read plus frozen/advanced clock, response revision pair and observation times correspond to service snapshot; no second resolution after response construction. |
| V-35 | DispatchEffectiveSettingsWireTests.Existing_host_catalogue_and_default_reads_keep_their_contracts | Compare aggregate with old endpoints on fixed state, retaining old DTO property sets/source vocabulary/capacityKind; pipeline host rows agree. Exercise actual shared projection. |
| V-36 | DispatchEffectiveSettingsWireTests.Secret_sentinels_never_enter_the_response | Seed harmless secret-like sentinels in excluded settings/provider/path/reason fields; recursively inspect real JSON, absent; approved field keys and safe source labels present. |
| V-37 | StandingPipelinePolicyDocumentationTests.Effective_settings_route_and_fields_are_documented | ops/API docs name exact route/selectors, LegacyOpen/SeparateQueues, sources, slot null/zero arithmetic and snapshot-not-reservation; old operator host PUT route still documented; WIP defaults no longer claims 409-only visibility or startup-only settings. |
| V-38 | StandingPipelinePolicyDocumentationTests.Policy_bundle_is_shorter_than_the_frozen_baseline | LF-normalized trimmed embedded bundle below 14,146 chars; compare changed policy block before/after Code base in report too. Runtime computed version matches hash of embedded text. |
| V-39 | StandingPipelinePolicyDocumentationTests.Policy_uses_scoped_read_and_preserves_override_conditions | Standing and Platform policy copies require effective endpoint; no three-route prerequisite; min-four/min-six fallback, prefer server2, shield desktop, ready depth, same-area deferral and open/absolute/canOverride/no-same-stage exception retained. |

R-1: AgentTaskConcurrencyLimitTests preserves existing default, create and override semantics (25 executions). R-2: all partial AgentTaskPipelineStatusTests preserve ready/backlog/queue-reason and existing host semantics (58). R-3: HostBudgetServiceTests preserves budget range, zero/clear and audits (8). R-4: HostEndpointTests preserves HTTP budget contract (4). R-5: RunnerDefaultSettingsTests (6) and RunnerDefaultsWireTests (5) preserve revisioned defaults and their wire contract. R-6: existing StandingPipelinePolicyDocumentationTests (7), InstructionBundleTests (60), TaskPlatformGuidanceTests (5) and RunnerDefaultGuidanceTests (4) preserve delivery, launch budget/hash and platform intent. Update only obsolete aggregate-read pins; do not remove guard assertions.

### Source census and OS counts

Recounted from source on this baseline, following class declarations across partial files and expanding Arguments, rather than counting filenames or assertions:

| Class | Source methods -> executed cases (Windows / Linux) | Census detail |
|---|---|---|
| AgentTaskConcurrencyLimitTests | 24 -> 25 / 25 | One two-argument validation method; no OS skips. |
| AgentTaskPipelineStatusTests | 49 -> 58 / 58 | `AgentTaskPipelineStatusTests.cs`: 26 -> 35; `AgentTaskPipelineStatusC557Tests.cs`: 19 -> 19; `HostBudgetPipelineTests.cs`: 4 -> 4. The separately declared AgentTaskPipelineEndpointTests (4 methods) is **not** selected by the class filter. |
| HostBudgetServiceTests | 7 -> 8 / 8 | One two-argument method; no OS skips. |
| HostEndpointTests | 4 -> 4 / 4 | No parameter expansion/skips. |
| RunnerDefaultSettingsTests | 6 -> 6 / 6 | In RunnerDefaultTests.cs; no parameter expansion/skips. |
| RunnerDefaultsWireTests | 5 -> 5 / 5 | Same physical file. OutboundConversionPublicCreateTests (1), RunnerDefaultPlacementTests (9) and RunnerDefaultMigrationTests (5) are separate classes, not selected; there is no class named RunnerDefaultTests. |
| InstructionBundleTests | 41 -> 60 / 60 | Arguments expansions 8, 6, 3, 6 replace four single executions. No OS skips. |
| StandingPipelinePolicyDocumentationTests | 4 -> 7 / 7 currently; proposed 7 -> 10 / 10 | Four policy-copy arguments plus three ordinary existing methods; add the three named V-37..39 methods. |
| TaskPlatformGuidanceTests | 5 -> 5 / 5 | Same physical file also declares the next class. |
| RunnerDefaultGuidanceTests | 4 -> 4 / 4 | Lives in TaskPlatformGuidanceTests.cs; no separate file. |
| New PipelineAdmissionSnapshotTests | proposed 10 -> 10 / 10 | V-01..10, no parameter attributes. |
| New DispatchEffectiveSettingsProjectionTests | proposed 12 -> 12 / 12 | V-11..22, no parameter attributes. |
| New DispatchEffectiveSettingsWireTests | proposed 14 -> 14 / 14 | V-23..36, no parameter attributes. |

Source inventory recipe: `rg -n '\[Test\]|\[Arguments|class |Skip|OperatingSystem|DataSource|Repeat'` over those exact files, including the two other partial files, then sum `max(1, ArgumentsCount)` per method. New counts above derive from the named proposed roster, not fictional source. No source-level OS skip was found in this selected roster; expected skips are zero on either platform. Shared class data fixtures are not extra executions. Recount after CARD-0505/0835 lands; counts are not frozen authority over changed source.

### Positive controls (post-land Mutation)

Each control names a concrete production mutation and an exact detecting test/assertion. Run baseline green -> mutate -> named assertion red -> restore exact bytes -> rebuild -> green. Method filter is `/*/Antiphon.Tests.Application/<Class>*/<Method>`; argument loops still count as one execution. Never accept compiler/fixture errors, empty selections or a harness timeout as red. Independent files can be batched only when controls cannot interfere; do not mutate shared count/projection methods concurrently.

| PC | Concrete mutation | Detecting test (class abbreviations expanded immediately below) and named red assertion |
|---|---|---|
| PC-01 | Drop project equality from the snapshot count query. | P.Scoped_pipeline_reports_project_limit_and_open_count: project A openCount == 3, not A+B. |
| PC-02 | Include Blocked in open status predicate. | P.Blocked_and_specialists_do_not_consume_open: openCount == 1. |
| PC-03 | Remove NotSpecialist from the read query. | P.Blocked_and_specialists_do_not_consume_open: openCount == 1 despite all three specialists. |
| PC-04 | Treat null project as no filter. | P.Null_project_has_own_bucket: returned null-bucket count/IDs exclude named projects. |
| PC-05 | Return MaxConcurrentTasks as absoluteLimit. | P.Absolute_409_matches_scoped_read: reported limit equals actual absolute refusal limit, distinct fixture values. |
| PC-06 | Use global role policy instead of project override. | P.Role_409_matches_scoped_read: report's role limit/source equals real refusal. |
| PC-07 | Set SeparateQueues compatibility absoluteLimit to maxParallel. | P.SeparateQueues_does_not_claim_open_cap: absoluteLimit is null. |
| PC-08 | Classify equal-to-default configured value as codeDefault. | E.Code_default_and_explicit_config_have_distinct_sources: explicit equal value source.kind == configuration. |
| PC-09 | Give every field project source whenever a project row exists. | E.Override_precedence_and_clear_keep_field_sources: inherited field source.scope == global/seed with matching revision. |
| PC-10 | Fill historical origin from current configuration. | E.Historical_seed_origin_stays_unknown: source.kind == unknown and stored seed unchanged. |
| PC-11 | Compute LegacyOpen free from inFlight only. | E.Legacy_open_headroom_counts_queue_but_not_ready: Code freeSlots == 2. |
| PC-12 | Treat queue zero as null/unbounded. | E.Separate_queue_and_parallel_axes_are_independent: admissionFreeSlots == 0 for zero queue. |
| PC-13 | Remove max(0, ...) clamp. | E.Over_limit_counts_remain_visible_and_free_slots_clamp: slots == 0 with count above limit. |
| PC-14 | Prefer configured host budget even when declaration is lower. | E.Remote_host_minimum_reports_both_operands_and_binding_sources: effectiveLimit == declaration and bindingSources == [declaredCapacity]. |
| PC-15 | Count CapacityWaitRetained against local host. | E.Local_retained_work_consumes_project_but_not_host: host inFlight excludes retained while project open includes it. |
| PC-16 | Omit pending launches or preparation mirrors from host sum (two sequential variants). | E.Remote_host_counts_all_projects_sessions_pending_and_mirrors: exact inFlight and breakdown sum includes each separately seeded nonzero term. |
| PC-17 | Replace the eligibility guard with unconditional known host freeSlots (fixture has a finite persisted budget even when stale). | E.Runner_freshness_and_drain_prevent_new_dispatch: slots == 0 after controlled stale boundary and during drain. |
| PC-18 | Change the aggregate read transaction from RepeatableRead to ReadCommitted. | E.One_read_uses_one_policy_revision_and_one_count_snapshot: old occupancy remains paired with old policy after the gated atomic writer commits; mixed old-policy/new-count is red. |
| PC-19 | Call RunnerDefaultSettingsService.GetAsync from aggregate GET. | W.Uninitialized_settings_return_503_without_seeding: settings/history rows remain absent and response is 503. |
| PC-20 | Omit role queue bound from admissionFreeSlots. | W.Queue_409_axes_and_precedence_match_reported_sources: zero reported slots and same role/queued limit/source as 409. |
| PC-21 | Treat parallel-full as zero fresh admission in SeparateQueues. | W.Parallel_full_holds_accepted_work_without_create_409: response admission > 0, actual accepted row Queued and held parallel. |
| PC-22 | Make DispatchSettingsEndpoints serialize its result with JsonIgnoreCondition.WhenWritingNull. | W.Null_and_zero_survive_json_serialization: GetProperty finds explicit null limit/source/timestamps. |
| PC-23 | Restore multi-route standing/Platform prerequisite in bundle. | D.Policy_uses_scoped_read_and_preserves_override_conditions: policy read uses /api/settings/dispatch and has no old prerequisite. |
| PC-24 | Append enough text to exceed the 14,146-character baseline without changing guard settings. | D.Policy_bundle_is_shorter_than_the_frozen_baseline: normalized length < 14146; InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget still checks actual argument accounting independently. |

P = PipelineAdmissionSnapshotTests; E = DispatchEffectiveSettingsProjectionTests; W = DispatchEffectiveSettingsWireTests; D = StandingPipelinePolicyDocumentationTests. PC-16 has two independent variants, so this inventory prescribes 25 mutations, not 24. All use fake clocks/gates where timing matters. Post-land Mutation may discover further controls, but ordinary Code never silently adds a broad test run to this closed list.

### Known flakes and exclusions

Read card evidence; none of these classes is in the selection, and none is permission to waive a new failure:

- CARD-0791/0794: RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision, RunnerClaudeAdapterEffortPromptTests.Attach_without_launch_effort_preserves_current (tight timing); CARD-0794 also RunnerMultilinePromptDeliveryTests.The_pty_receives_the_marker_line_of_a_multiline_prompt (Linux broken pipe/hang).
- CARD-0818: CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap can wedge disposal after timeout.
- CARD-0820: EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open, BuildSlotClientTests renewal, checkpoint owner uncertainty, C448_V36_EachAuthorityCoordinatePrecedesMutation, Windows ControlledLandingGitTests.C688_UnregisterDropsHeadFilesWithoutTouchingSetAsideOrRecreatedPath(False), and inherited resilience timing.
- CARD-0828: CheckpointTaskOwnershipTests uncertainty/late-settlement timeouts can leak watchers/executors and recreate roots; no checkpoint namespace sweep here.
- CARD-0848: DetachedLauncherTests.executor_survives_its_starter, fixed ten-second marker window under load.
- CARD-0878: HerdrAlwaysOnChannelParityTests.Standing_native_wire_missing_target_never_creates(ClaudeCode, PtyHost, False), Linux classification race; CARD-0879: Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry, Windows DetectTimeout/PaneClosed race.
- ScaledTimeProviderTests.Speed_10 (CARD-0757); HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines (CARD-0751); ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget (CARD-0820).

No Unit lane, full assembly, native Pty/Herdr/Grok suite, UI/E2E, real provider, host write, runner restart or benchmark is required. A selected failure is reported with its named assertion and baseline investigation; do not invent a known-flaky exception. The default test assembly hooks may still have their own housekeeping behavior; do not add extra cleanup or disable required hooks silently.

### Runnable checkpoint procedure

The assigned brief explicitly requires the direct self-leasing driver because the checkpoint tool can refuse owner-unverified (CARD-0853). That instruction takes precedence over the general CARD-0723 launcher preference for this task lineage. Do not edit the driver/tool. Commit/push each slice group first and run its rows with fresh results roots (CP-1..3 after S1-S3; CP-4 after S4). No build-slot wrapper around this driver, no NoSlot, no dotnet test. Exit 4 is a slot timeout to report, never a reason to run unleased.

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c881-s1/ -Filter '/*/Antiphon.Tests.Application/(PipelineAdmissionSnapshotTests*)|(AgentTaskPipelineStatusTests*)|(AgentTaskConcurrencyLimitTests*)/*' -Expect PipelineAdmissionSnapshotTests,AgentTaskPipelineStatusTests,AgentTaskConcurrencyLimitTests -MinExecuted 93 -ResultsRoot .antiphon/c881-cp1-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c881-s2/ -Filter '/*/Antiphon.Tests.Application/(DispatchEffectiveSettingsProjectionTests*)|(HostBudgetServiceTests*)/*' -Expect DispatchEffectiveSettingsProjectionTests,HostBudgetServiceTests -MinExecuted 20 -ResultsRoot .antiphon/c881-cp2-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c881-s3/ -Filter '/*/Antiphon.Tests.Application/(DispatchEffectiveSettingsWireTests*)|(HostEndpointTests*)|(RunnerDefaultSettingsTests*)|(RunnerDefaultsWireTests*)/*' -Expect DispatchEffectiveSettingsWireTests,HostEndpointTests,RunnerDefaultSettingsTests,RunnerDefaultsWireTests -MinExecuted 29 -ResultsRoot .antiphon/c881-cp3-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c881-s4/ -Filter '/*/Antiphon.Tests.Application/(StandingPipelinePolicyDocumentationTests*)|(InstructionBundleTests*)|(TaskPlatformGuidanceTests*)|(RunnerDefaultGuidanceTests*)/*' -Expect StandingPipelinePolicyDocumentationTests,InstructionBundleTests,TaskPlatformGuidanceTests,RunnerDefaultGuidanceTests -MinExecuted 79 -ResultsRoot .antiphon/c881-cp4-green
```

These direct commands use literal `|` inside a single-quoted filter. The Markdown table escapes each pipe as `\|`; the manifest importer unescapes it. Do not pass a literal backslash to TUnit. Linux driver adds UseAppHost=false; retain its current behavior. TUnit project executions stay sequential relative to Antiphon.Agents.Pty.Tests; no such second assembly is requested here. Every CP receipt must name committed SHA, build/filter, executed/passed/failed/skipped and fresh TRX; preserve CARD-0835 clean-source fields when landed. A wrong/empty roster is a failed checkpoint even if native exit is zero.

### Checkpoints

Closed ordinary list: one isolated build and one exact filter per row. Red-first and necessary repair reruns are the same row, reported explicitly; no extra unlisted build. All rows run on server2/Linux. Portable expected Windows counts are shown to prevent hidden OS skip accounting, **not** to commission duplicate desktop runs. **No row requires Windows.** A newly identified Windows-only requirement must get a separate bounded amendment, not move this whole selection to the desktop.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c881-s1/` | scoped-admission | `/*/Antiphon.Tests.Application/(PipelineAdmissionSnapshotTests*)\|(AgentTaskPipelineStatusTests*)\|(AgentTaskConcurrencyLimitTests*)/*` | V-01..10, R-1..2 | Linux 93 / Windows 93 executed; 0 failed, 0 skipped; every named class | 93 | 8 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c881-s2/` | effective-projection | `/*/Antiphon.Tests.Application/(DispatchEffectiveSettingsProjectionTests*)\|(HostBudgetServiceTests*)/*` | V-11..22, R-3 | Linux 20 / Windows 20 executed; 0 failed, 0 skipped; every named class | 20 | 6 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c881-s3/` | aggregate-wire | `/*/Antiphon.Tests.Application/(DispatchEffectiveSettingsWireTests*)\|(HostEndpointTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultsWireTests*)/*` | V-23..36, R-4..5 | Linux 29 / Windows 29 executed; 0 failed, 0 skipped; every named class | 29 | 8 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c881-s4/` | final-policy | `/*/Antiphon.Tests.Application/(StandingPipelinePolicyDocumentationTests*)\|(InstructionBundleTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)/*` | V-37..39, R-6 | Linux 79 / Windows 79 executed; 0 failed, 0 skipped; every named class | 79 | 5 |

## Activation and rollback

After Review/land, activation is an operator-owned ordinary server deployment. This Plan performs none. Do not enable SeparateQueues, raise/lower concurrency, write host budgets, alter runner defaults or send test dispatches to prove a read feature. Confirm active `/api/version` SHA directly, then read the scoped new route and compare its counts/limits to the existing pipeline/host/catalogue/defaults on a quiet observation; record any intervening task/config changes. Missing endpoint on the running SHA means not activated; use the documented interim read procedure and defer when admission headroom is unknown.

Rebuild computes bundle version from the changed Markdown. Activation acceptance includes a newly composed orchestrator carrying that version (or the existing idle refresh path), with the effective-settings read instruction and a passing full argv budget check. Do not kill a working session just to refresh policy. CARD-0822 integration/notification activation belongs to its subsequent delivery and must use this projection.

Rollback is a reviewed revert of this card's read projection/route/policy changes together. Keep CARD-0505's settings/gates and persisted values/audit history; optional seed-origin metadata is additive and may remain. Revert S4's route instruction with the route so orchestrators do not call a removed API. No destructive schema rollback, settings reset or runner capacity write is needed.

### Cost

Estimates, not measured timings: ordinary Code green floor = **27 minutes** (8 + 6 + 8 + 5), four isolated builds included. Roster = **221 executions** (93 + 20 + 29 + 79), all on Linux, zero planned skips. If run on Windows the portable roster is also 221/zero skips; **Windows commissioned count is zero**. Red-first rounds add up to another 27 minutes; necessary same-row fixes are separately reported. Final Review reruns the complete green list, estimated another 27 minutes. No repeated full suite or launch-tool bootstrap is budgeted.

TestDesign/source refresh estimate 1–2 hours. Code authoring estimate 4–6 hours plus the 27-minute green floor and explicitly recorded red rounds; choose ExpectAbout from that sum, not the 221 test count. Mutation has **25 variants**, estimated 3 minutes each including its baseline/red/restored-green build/run cycle = 75 minutes, plus 15 minutes for custody/restoration/reporting = **90 minutes**. Ordinary Code green + Final Review + Mutation estimate = 144 minutes, excluding authoring and test-first reds. Actual build-slot waits are reported separately. The seed-origin amendment and shared read extraction are the main implementation costs; no measured speedup is claimed.
