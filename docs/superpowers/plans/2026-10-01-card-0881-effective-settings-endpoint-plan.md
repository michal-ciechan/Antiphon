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

Response includes `asOf`, runner observation times, `consistency: snapshotNotReservation` and `schemaVersion: 1`. SQL facts are consistent within the read; in-memory preparation and runner state have separately observed freshness and cannot be made atomic by a read API. Limit sources/revisions are from the exact resolved object, never re-resolved while serializing. `CancellationToken` reaches every read. Do not broaden resilience allowlists.

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
    "kindDefaults": [ { "agentKind": "Codex", "runnerId": "server2", "inheritedRunnerId": "server2", "source": "global" } ],
    "supportedKinds": ["Grok", "ClaudeCode", "Codex"],
    "unresolvedReferences": []
  }
}
```

All keys above are mandatory; nullable values serialize as explicit JSON null. GUIDs are canonical strings; UTC timestamps use the server's normal JSON timestamp format. `scope.kind` is `project` or `unscoped`; the latter requires projectId null. Counts are nonnegative integers. `limit.value`, `queueLimit.value`, and derived slot counts allow null for an unbounded axis; `absolute.limit.value` stays finite under CARD-0505. `limit.population`/`enforcedAt` become `parallel`/`dispatch` in SeparateQueues. `absoluteLimit` then becomes null, while `openCount` remains diagnostic. Explicit queue zero means no fresh admission. Existing overages remain visible as counts greater than limits with slots clamped to zero.

`runnerDefaults.kindDefaults` preserves the existing DTO's explicit override rows and `source` vocabulary; it is not required to list inherited-only kinds. The illustrative Codex row represents an explicit stored override equal to the global choice, and therefore must use the existing store's `kind` source (see D-4). No platform-default field is invented. Resolve a kind absent from the array via globalRunnerId; unresolved references remain visible, never silently replaced by server2. `globalSource` is `runtimeOverride`, `configuration`, `codeDefault` or `unknown` using the existing revision provenance/import metadata.

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

The policy overlay remains up to four per stage and at most six non-specialist open tasks assigned to server2 unless the operator says otherwise; counting queued successors prevents oversubscribing that intent while launches wait. Use min(4, any finite effective stage cap) and min(6-taskOpenCount, dispatchableSlots); lower hard limits always win. Code feed depth additionally counts ready. These operator ceilings are prose policy, not invented enforced server settings; a larger API limit is not permission to spend more. For task-specific routing/provider/scope decisions use their existing checks.

Rejected: treating the desktop catalogue's capacity as a physical-seat counter; subtracting project tasks from a fleet host budget; using ready/Blocked to reduce admission capacity; adding host free slots across stages; silently normalizing unknown/offline to unlimited.

### D-4: report provenance honestly, once

Common source object: `{kind,scope,key,revision}`. `kind` is `codeDefault|configuration|runtimeOverride|runnerDeclared|unknown`; `scope` is `seed|global|project|host|runner`; key is an allowlisted setting/field name, never a provider value/path; revision is nullable integer. Override source is the effective field's global/project revision, not merely the existence of a project settings row. Explicit nullable overrides keep their source. Empty project overrides inherit the global source. Host min operands retain independent provenance; bindingSources refers to operand keys.

Add typed startup-origin metadata captured in Program at configuration binding, where IConfiguration is allowed: check explicit leaf presence, including explicit null overrides, rather than comparing the bound value with a newly constructed default. Pass only known key/source labels to services. Extend CARD-0505's existing SeedJson metadata using its existing one-time initialization path so origin survives later startup-config changes. No value changes, reseeding, extra audit revision, new initialization-on-GET, settings migration or second writer. This is a required small contract amendment to CARD-0505 before/with its initial delivery. Already seeded historical rows lacking provenance report `unknown`; do not claim to reconstruct history. Tests must cover both fresh accurate code/config origin and honest historical unknown.

Reuse the runtime resolver's `default/global/project` precedence; adapt its source labels to the wire vocabulary above. Add a noninitializing RunnerDefaultSettingsService read for the aggregate; missing initialized settings return 503 `dispatch_settings_uninitialized` through HttpException. Startup remains the initialization owner. Imported placement metadata can be unknown if the old store did not preserve presence; do not classify Migration as Human. Preserve existing kind source values by reusing ProjectAsync's mapping.

Rejected: a parallel config store; returning raw IConfiguration; calling EnsureInitializedAsync from GET; calling every seed `codeDefault`; inferring origin from a value equal to its shipped default.

### D-5: mandatory final policy slice and CARD-0822

S4 is the last slice and cannot be dropped. Replace the multi-GET wording in `server/Bundles/orchestrator.md`, `AGENTS.md`, `docs/orchestration-loop.md` section 1, `.claude/skills/antiphon-orchestrator/SKILL.md`; update `docs/ops-http.md`, `docs/antiphon-api.md` and the pins in StandingPipelinePolicyDocumentationTests. Discover mirrors with `rg -n 'three-route|CARD-0881|effective concurrency limits|GET /api/runner-defaults' AGENTS.md docs server/Bundles .claude` and edit policy mirrors, not historical plans/investigations or generated docs/cards. CLAUDE.md is only an import pointer, not another prose copy.

Policy must say **read the effective-settings endpoint and use its limits**, with the explicit scope, min-bound operator defaults, server2 preference, desktop shielding, Code depth including ready, per-task Worktree and same-source-area deferral retained. Preserve the absolute-axis override exception only when `population=open`, `canOverride=true` and no same-stage occupant; queue or role saturation defers. No independent GET /api/hosts prerequisite remains. Host budget PUT stays documented as an operator action.

Use a net-shorter replacement, measured against the exact policy block on the Code base, not just this historical file size. Do not change CommandLineBudgetChars, remove argument accounting, narrow the composition test or hand-edit versions. Rebuild embeds the edited text and InstructionBundles computes the new content hash. Record before/after normalized block length, full bundle length, computed version and actual worst-case composition plus argument estimate. CARD-0884's broader transport/headroom redesign remains separate.

CARD-0822's generated file must consume the **same effective settings projection** for caps, modes, sources, host limits and defaults. Its existing plan builds those directly from DelegationSettings and several services; amend its `OrchestratorInstructionsSnapshotBuilder` contract to use this service's stable policy component. Do not put occupancy/asOf in the generated file's content hash: those would produce notices on every task transition. Its file tells orchestrators to GET the scoped live route before dispatch; its global file must not present a single project override as fleet policy. CARD-0822 owns file lifecycle, setting-change signals and delivery receipts. It is currently held/unimplemented at this baseline: the dependency is CARD-0505 -> CARD-0881 -> CARD-0822; there are no imaginary generated files to edit here. If that ordering changes, amend the exact footprint and checkpoint roster for its landed builder/renderer before Code, rather than silently shipping duplicate settings sources.

Rejected: updating only docs and leaving the delivered bundle stale; adding long JSON examples to argv; generating numbers from separate settings reads; repurposing this read-only card into CARD-0822's notification pipeline.
