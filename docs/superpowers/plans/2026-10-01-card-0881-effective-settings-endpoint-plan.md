# CARD-0881: one effective dispatch-settings read

Date: 2026-10-01. Stage: **TestDesign complete; next Code, after the ordered prerequisites below.** Verification is frozen at start ref **`2b4d7313324b6eaeadf6e45fb15808bacfad6263`** on `feat/card-task-271c88b9`. The original Plan baseline was `fccef27eb03f2522f2a40574dc1c764335d4a490` on `feat/card-task-456be05e`; its live observations below remain historical. TestDesign changes only this Markdown file. No rebase, merge, reset or runtime change is part of this task.

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

To make V-11 exercise the actual binding path without booting Program, place the pure capture operation in an internal static `Program.CaptureDispatchConcurrencySeedOrigins` member in the existing partial Program class. Startup and the isolated host call that same member with a real in-memory ConfigurationBuilder. This keeps IConfiguration at the composition root and needs no extra production file. The helper returns only the typed origin record; it starts no host, initializes no settings and touches no static runtime state. V-11 must not manufacture origin records that merely agree with its own expectations.

Reuse the runtime resolver's `default/global/project` precedence; adapt its source labels to the wire vocabulary above. Add a noninitializing RunnerDefaultSettingsService read for the aggregate; missing initialized settings return 503 `dispatch_settings_uninitialized` through HttpException. Startup remains the initialization owner. Imported placement metadata can be unknown if the old store did not preserve presence; do not classify Migration as Human. Preserve existing kind source values by reusing ProjectAsync's mapping.

Rejected: a parallel config store; returning raw IConfiguration; calling EnsureInitializedAsync from GET; calling every seed `codeDefault`; inferring origin from a value equal to its shipped default.

### D-5: mandatory final policy slice and CARD-0822

S4 is the last slice and cannot be dropped. Replace the multi-GET wording in `server/Bundles/orchestrator.md`, `AGENTS.md`, `docs/orchestration-loop.md` section 1, `.claude/skills/antiphon-orchestrator/SKILL.md`; update `docs/ops-http.md`, `docs/antiphon-api.md` and the pins in StandingPipelinePolicyDocumentationTests. Discover mirrors with `rg -n 'three-route|CARD-0881|effective concurrency limits|GET /api/runner-defaults' AGENTS.md docs server/Bundles .claude` and edit policy mirrors, not historical plans/investigations or generated docs/cards. CLAUDE.md is only an import pointer, not another prose copy.

Policy must say **read the effective-settings endpoint and use its limits**, with the explicit scope, min-bound operator defaults, server2 preference, desktop shielding, Code depth including ready, per-task Worktree and same-source-area deferral retained. Preserve the absolute-axis override exception only when `population=open`, `canOverride=true` and no same-stage occupant; queue or role saturation defers. If the 409 occupant list is truncated, confirm zero same-stage open tasks from the scoped read; absence from a capped list is not absence of an occupant. No independent GET /api/hosts prerequisite remains. Host budget PUT stays documented as an operator action.

The same final slice records the **operator's 2026-10-01 Final Review verdict rule** in the bundle, AGENTS.md and the two policy mirrors: end `found` only for a regression of existing functionality or a new reachable security/privacy exposure (including a reachable fail-open). Test-strength gaps, plan-label mismatches and documentation errors are disclosed and filed as Backlog follow-up cards; the implementation card lands. A test-strength disclosure is not permission to invent a passed test, erase an unfinished checkpoint, or claim Full evidence for an incomplete round. Keep scope-completion facts and verdict classification separate. This explicit operator instruction supersedes older generic wording that treats every documentation/test gap as a blocking Final Review defect. CARD-0881 does not edit the separately owned stage-review bundle.

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

Immediately before `Model-tier names are`, insert this exact paragraph and one blank line:

```text
Final Review ends `found` only for a regression of existing functionality or a new reachable
security/privacy exposure. Disclose test-strength gaps, plan-label mismatches and documentation
errors as Backlog follow-up cards; the card lands.
```

The frozen character budget, measured in **UTF-16 units** after LF normalization and Trim, is:

| Edit | Removed | Added | Net |
|---|---:|---:|---:|
| Standing prefix, including its terminating LF | 922 | 730 | -192 |
| Code-count sentence | 63 | 45 | -18 |
| Absolute override condition | 85 | 94 | +9 |
| Platform prefix, including the join space | 363 | 245 | -118 |
| Final Review paragraph, including two terminating LFs | 0 | 241 | +241 |
| **Total** | **1,433** | **1,355** | **-78** |

Current embedded bundle **14,146**, version **`bac5c61d`**; exact candidate **14,068**, computed version **`c0137d5d`**. Versions are observations, never constants to edit. The real composition measurement uses the same role/kind loop, `board-api`, `style-explanatory`, Telegram preset and six other arguments as `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget`:

| Quantity | Start ref | Exact candidate |
|---|---:|---:|
| Maximum composed text | 29,635 | 29,557 |
| Other arguments plus append flag/quoting estimate | 124 | 124 |
| Total against unchanged 30,000 guard | 29,759 | 29,681 |
| Remaining guarded headroom | 241 | 319 |

Thus the brief's roughly 365 characters is the **text-only** margin (30,000 - 29,635); actual argument accounting leaves 241. Do not spend the difference twice. TestDesign made one justified isolated compile/measurement outside tracked source to resolve this budget: a net9 console linked the **unchanged** `InstructionBundles.cs`, `InstructionBundleComposer.cs` and `ChannelPreamble.cs`, embedded the real `server/Bundles/*.md` resources with their actual logical names (excluding README), and copied only the required enum/predicate/constant declarations verbatim. It ran the real catalog loader, `ForDelegate`, `Compose`, `Render` and `EnsureWithinCommandLineBudget`. The candidate replaced just the real rendered orchestrator block using `InstructionBundle.Render`; every other block remained identical. Both 28-composition loops satisfied the guard. This is a budget probe, **not** a TUnit pass or a full server build.

Probe commands were `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c881-bundle-budget-probe -- dotnet build /tmp/c881-budget-M45ccF/Probe.csproj --property:OutputPath=bin-budget/ --property:UseAppHost=false --nologo -nodeReuse:false` (one build, 0 warnings/errors) and the same wrapper with label `c881-bundle-budget-measurement` running `dotnet /tmp/c881-budget-M45ccF/bin-budget/Antiphon.Server.dll /tmp/c881-budget-M45ccF/candidate.txt` (exit 0); both obtained/released a host slot. Scratch paths are historical evidence, not Code dependencies. Reproduce the candidate from the five exact replacements above. The final production-resource proof is CP-4: V-38 checks normalized embedded text against source and the frozen ceiling/hash; the existing InstructionBundleTests method runs the full actual composition guard, including arguments. No extra TUnit run is authorized by this measurement.

CARD-0505 and other predecessors may change surrounding content. Before S4 Code records its actual base bundle/block/composition lengths, applies this replacement to that base, and commits a refreshed budget here if necessary. Require **strictly shorter than that immediate base AND below 14,146**, plus the unchanged 30,000 full guard. Never discard unrelated instructions or weaken the guard to hit a historical number.

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

**Chosen Code/landing queue:** CARD-0826 -> CARD-0788 -> CARD-0883 -> CARD-0885 -> CARD-0886 -> CARD-0505 -> **CARD-0881** -> CARD-0822. This is a deterministic serialization choice for the caller, not a claim that every edge is a functional dependency. CARD-0826 is already in flight per the brief; both 0505 and 0881 stay behind it. The hard feature edge is 0505 -> 0881 -> 0822. Plans/TestDesign can finish now. CARD-0835's source-clean checkpoint/Review changes are already in this start ref; its old pending-land restriction is satisfied.

| Other card | Exact intersection with CARD-0881 Code / transitive collision | Required order |
|---|---|---|
| CARD-0826 | Direct: `server/Program.cs`, `docs/ops-http.md`. Its `server/Application/Services/AgentTaskLandService.cs`, `server/Infrastructure/Data/AppDbContext.cs`, `server/Migrations/AppDbContextModelSnapshot.cs` and generated AddHostCleanup migration/Designer overlap upstream land/schema owners, especially 0505's model. Its `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, `scripts/deploy-server2.ps1`, `server/Bundles/stage-code.md`, `server/Bundles/stage-review.md`, `tools/Antiphon.Checkpoints/Cleanup/*` and `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` are **not** in this Code footprint. | Finish/land 0826 first; no parallel 0881 or 0505 Code while it owns Program/EF. Preserve its maintenance registrations and final stage bundle wording. |
| CARD-0788 | Direct: `docs/ops-http.md`, `docs/orchestration-loop.md`. Its land/refusal services are read-only here: `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandSourceResolver.cs`, `AgentTaskReplyService.cs` under `server/Application/Services/`; these intersect 0883/0886's land area. | After 0826, before 0883 and 0881. Do not treat edits to different sections of the same document as independent ownership. |
| CARD-0883 | Direct: `docs/orchestration-loop.md`. Its `server/Infrastructure/Data/AppDbContext.cs`, `server/Migrations/AppDbContextModelSnapshot.cs`, generated AddLandRequestWriterProvenance migration/Designer, and land/reply services above collide with 0826/0788/0505 and the 0886 harness area, not this read implementation. | After 0788; before 0886/0505/0881. Preserve its recovery guidance in the shared owner doc. |
| CARD-0885 | Direct: `docs/orchestration-loop.md`. Shared read dependencies: `scripts/run-checkpoint.ps1`, `scripts/validate-checkpoint-receipt.ps1`, `tools/Antiphon.Checkpoints/Program.cs`, `CheckpointApp.cs`, `Execution/{BuildStep,RowRunner,RunScheduler}.cs`, `Report/{ReportModel,ReportValidator,CheckpointLine,ReportWriter,ReportMerger}.cs`, `Directory.Build.targets`, `tests/Antiphon.Tests/Antiphon.Tests.csproj` and `server/Bundles/stage-code.md`. 0881 does not edit those dependencies. | After 0826's checkpoint extraction (chosen queue also puts 0788/0883 first); before 0886's fixed-revision measurements and before 0881's final doc slice. Re-read the landed CLI/receipt contract; do not use its repeat facility to inflate this roster. |
| CARD-0886 | **No exact edited-file intersection** with 0881. Its `tests/Antiphon.Tests/TestHelpers/{LandingProtocolHarness,LandingSafetyHarness,LandingGitFixture}.cs`, `Application/CheckpointSourceApprovalTests.cs`, `Scripts/RunCheckpointSourceScriptTests.cs`, `Checkpoints/{CheckpointSourceStateTests,CheckpointSourceExecutionTests}.cs` are not selected or edited here. It consumes the land/checkpoint contracts changed by 0826/0788/0883/0885. | Chosen queue lands it after those four, before 0505/0881, to freeze the infrastructure revision. No invented API dependency or additional 0886 tests in this card. |
| CARD-0505 | Direct: `server/Application/Dtos/{AgentTaskPipelineDtos,DispatchConcurrencyDtos}.cs`; `server/Application/Services/{AgentTaskPipelineStatusService,DispatchConcurrencySettingsService}.cs`; `server/Api/Endpoints/AgentTaskEndpoints.cs`; `server/Program.cs`; `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`; `server/Bundles/orchestrator.md`; `docs/{ops-http,antiphon-api,orchestration-loop}.md`. Its frozen TestDesign also uses/updates the standing-policy pins in `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`. 0505's gate/dispatcher/EF files remain outside 0881. | **Hard prior land.** Its store, resolver, scoped pipeline and fake dispatch host do not exist at this start ref. Recount after land and implement the seed-origin extension through its existing writer only. No standalone substitute policy. |
| CARD-0822 | Direct: `server/Program.cs`; `server/Application/Services/{HostBudgetService,RunnerDefaultSettingsService}.cs`; `server/Bundles/orchestrator.md`; `AGENTS.md`; `docs/{orchestration-loop,ops-http}.md`; `.claude/skills/antiphon-orchestrator/SKILL.md`; `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`. Its declared wording updates also intersect `tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs` and `InstructionBundleTests.cs` (the latter is read/run only here). | **After 0881.** Amend its snapshot builder to consume the same stable policy projection; omit occupancy/asOf from generated-file hashes. No generated instructions code is added by 0881. |
| CARD-0880 / CARD-0884 | 0880's interim policy is already landed. 0884 concerns the shared composition guard; no transport/budget source is edited here. | Replace 0880's multi-route wording last; retain the full 30,000 guard. Serialize any newly admitted bundle redesign and recalculate the budget. |

The implementation footprint is exactly the 30 source paths in the scope line above, plus **this plan** for source-census/budget/receipt amendments. Three listed existing regression files are constructor-wiring-only allowances; omit their edit when unnecessary. The footprint does **not** implicitly include all files used by a fixture. Any additional path requires a committed, concrete amendment and collision recheck before editing. Last slice also writes the review-verdict paragraph to the bundle, AGENTS.md, orchestration owner and skill mirror, and adds its named test in the already listed standing-policy test file.

Before actual Code dispatch, refresh the pipeline, runner catalogue, defaults and hosts reads that exist today, plus project/board-scoped task occupancy and exact in-flight scopes. Follow the new single route only after it is implemented/active. This TestDesign makes no occupancy or activation claim and dispatches no agents. A sibling's card column is not proof of a landed implementation; a reversed queue requires the caller to record the changed shared-file order first.

## Verification design

### Frozen source and review contract

TestDesign read `card.ps1 get CARD-0881 -Board Antiphon` in full: **One effective-settings endpoint for orchestrators (concurrency limits, runner capacity, occupancy)**, platform Any. Source census and the exact budget are from **`2b4d7313324b6eaeadf6e45fb15808bacfad6263`**. There are **182 existing executions plus 40 proposed executions = 222**, with 0 planned skips on either OS. No TUnit tests ran in TestDesign. The only compile was D-6's small, slot-leased real-composer budget probe; 0 errors/warnings, measurement exit 0. No server build, database fixture, provider or live checkout was exercised.

CARD-0505's new store/host is absent at this ref. Its names below are the committed dependency contract, not source claimed to exist. Recount on the eventual Code base after the chosen landing queue; if prerequisite changes alter methods, argument expansion, constructors or bundle text, commit the exact delta here before implementation/checkpoints. Preserve every existing assertion. The fixed 222 is the start-ref design, not authority to misreport a later TRX.

Apply the operator's Final Review rule from D-5. A reachable regression/security or privacy exposure yields `found`; test-strength/plan-label/doc-only errors are named disclosures with Backlog follow-ups and the card lands. Ordinary scope and source-clean evidence must still be reported honestly. This section does not waive an unexecuted row or manufacture acceptance evidence.

### Fixtures, boundaries and test-first order

Use TUnit through `dotnet run --project tests/Antiphon.Tests`, driven by the **checkpoint tool**, as specified below. No real provider or production runner launches. `TestDbFixture.CreateIsolatedSchemaAsync` owns a **cloned database**, despite the schema name, from the documented shared-Postgres lifecycle. Each scenario owns its clone; initialize settings explicitly in setup, not via the aggregate GET. Compose CARD-0505's fake dispatch/admission host and `DelegationTestServices` registrations in the one new `DispatchEffectiveSettingsTestHost.cs`; use actual minimal endpoint mappings, HttpException middleware and EF services. Do not boot real Program. Pure arithmetic/source tests may use immutable fixture records, but scope, database populations, HTTP JSON and 409 comparisons must cross the real service/EF/endpoint boundary. A stub response or an expected value derived by the same projection is insufficient. Existing regression fixtures keep their real throwaway Git repositories if needed; no fixture operates a live checkout, real provider or production runner. No new Git behavior is introduced.

Use `FakeTimeProvider` at a fixed UTC instant **only for projection/freshness services**. Advance explicitly across the configured stale boundary; no sleep-based assertions or real-time margins below two seconds. Do not give a frozen clock to the entire runtime/message queue (testing owner, Gotcha 73): reuse the existing real/offset/auto-advancing queue clock for V-30's dispatch fixture. Gate policy/count reads using an async EF command interceptor, not arbitrary delays. For V-22/34, fully materialize the first policy SELECT, pause before the count SELECT, commit an atomic policy-and-task update on a second context, then release the reader. RepeatableRead must return the old pair; ReadCommitted must return the deterministic mixed pair. Avoid row locks and concurrent queries on one DbContext. Release gates, cancel owned work and await writer/reader completion in finally even after assertion failure.

For V-33 use a recording pending-reader interceptor whose cancellation registration completes an awaited acknowledgement; cancel only after its entered signal. Assert token identity/cancellation and no successful JSON response. A generous real-time watchdog (at least 10 seconds) is disposal/infrastructure protection, never the expected detecting assertion. The no-write checks use fresh contexts before/after plus recording SaveChanges/event/launch/stop/input sinks; GET must neither initialize nor prune expired pins. All expected counts/sources/statuses are literal independently seeded expectations.

This API creates no asynchronous delivery obligation. Its producer is the HTTP request, destination the requesting orchestrator, persistence the existing task/settings/host tables, and observable receipt the JSON response with scope/revisions/asOf. Reads neither persist a new snapshot nor enqueue a notice. CARD-0822's later file/notice producer and transcript receipt remain that card's responsibility.

For each slice: implement and commit the named assertions/scaffold; run that slice's exact CP row as a declared preparatory red and record expected assertion failures; implement and commit/push the production change. S1-S3 form one final runtime verification group: run CP-1, CP-2 and CP-3 green only after all three are committed, so the S2 shared-host extraction cannot leave S1 with stale evidence. S4 then runs CP-4 green, including the new Final Review rule and the real embedded bundle budget. Preparatory red invocations use the same exact row/filter before its final After group, are explicitly authorized here, and are reported as red-round reruns. If absent symbols initially prevent compilation, first add minimal callable scaffolding returning incomplete values, then require the named assertion to fail; a compile error does not count as a useful red. S2/S3 use the actual resolved CARD-0505 provider, not a replacement test-only rules engine. S4 red-first edits the pins before policy text. Commit before every invocation; never edit while a driver owns the worktree. Record every red/fix rerun. Deliberate production mutations are post-land Mutation work, not ordinary Code.

### Required named tests and field coverage

The following are **frozen proposed individual `[Test]` methods**, each one execution and no Arguments/Repeat/OS skip. They are in red-first authoring order: S1 V-01..10, S2 V-11..22, S3 V-23..36, S4 V-37..40. Counts are a design roster, not tests claimed to exist or pass. Code must reconcile dependency wiring after CARD-0505 and recount the implemented source and fresh TRX; a changed roster requires a committed table amendment. Use each V-n and the PC-n below as literal assertion labels/custom messages; an early setup failure never substitutes for the named witness.

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
| V-11 | DispatchEffectiveSettingsProjectionTests.Code_default_and_explicit_config_have_distinct_sources | Real ConfigurationBuilder and the shared Program capture member feed the real initializer/resolver: two equal numeric values, one absent config leaf and one explicit, produce codeDefault vs configuration; include explicit-null role leaf, key and persisted seed revision. Reconstruct services with different config against the same clone to model restart and retain seed origin/value. |
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
| V-38 | StandingPipelinePolicyDocumentationTests.Policy_bundle_is_shorter_than_the_frozen_baseline | First assert LF-normalized trimmed embedded bundle below 14,146 chars and strictly below the recorded immediate Code-base size; for the exact frozen candidate it is 14,068. Then assert embedded text equals normalized source and its runtime version equals the SHA-256 prefix. Report actual changed blocks and full composition/argument counts. Existing InstructionBundleTests runs the full role/kind guard; do not replace it with length-only evidence. |
| V-39 | StandingPipelinePolicyDocumentationTests.Policy_uses_scoped_read_and_preserves_override_conditions | Standing and Platform policy copies require effective endpoint; no three-route prerequisite; min-four/min-six fallback, prefer server2, shield desktop, ready depth, same-area deferral and open/absolute/canOverride/no-same-stage exception retained. |
| V-40 | StandingPipelinePolicyDocumentationTests.Final_review_found_is_limited_to_regressions_and_reachable_exposures | Read the actual bundle, AGENTS.md, orchestration owner and skill mirror. For each, assert `found` is limited to existing-functionality regression or new reachable security/privacy exposure; all three disclosure categories (test-strength gaps, plan-label mismatches, documentation errors), Backlog follow-up and landing remain explicit. Inspect the policy section, not a phrase in a historical plan. Single method, four internal assertions groups, one execution. |

R-1: AgentTaskConcurrencyLimitTests preserves existing default, create and override semantics (25 executions). R-2: all partial AgentTaskPipelineStatusTests preserve ready/backlog/queue-reason and existing host semantics (58). R-3: HostBudgetServiceTests preserves budget range, zero/clear and audits (8). R-4: HostEndpointTests preserves HTTP budget contract (4). R-5: RunnerDefaultSettingsTests (6) and RunnerDefaultsWireTests (5) preserve revisioned defaults and their wire contract. R-6: existing StandingPipelinePolicyDocumentationTests (7), InstructionBundleTests (60), TaskPlatformGuidanceTests (5) and RunnerDefaultGuidanceTests (4) preserve delivery, launch budget/hash and platform intent. Update only obsolete aggregate-read pins; do not remove guard assertions.

### Source census and OS counts

Recounted from current source at `2b4d7313324b6eaeadf6e45fb15808bacfad6263`, following **top-level** class declarations across partial files and expanding Arguments, rather than counting filenames or assertions. In particular, nested `C557Seed` does not end the surrounding partial test class:

| Class | Source methods -> executed cases (Windows / Linux) | Census detail |
|---|---|---|
| AgentTaskConcurrencyLimitTests | 24 -> 25 / 25 | One two-argument validation method; no OS skips. |
| AgentTaskPipelineStatusTests | 49 -> 58 / 58 | `AgentTaskPipelineStatusTests.cs`: 26 -> 35; `AgentTaskPipelineStatusC557Tests.cs`: 19 -> 19; `HostBudgetPipelineTests.cs`: 4 -> 4. The separately declared AgentTaskPipelineEndpointTests (4 methods) is **not** selected by the class filter. |
| HostBudgetServiceTests | 7 -> 8 / 8 | One two-argument method; no OS skips. |
| HostEndpointTests | 4 -> 4 / 4 | No parameter expansion/skips. |
| RunnerDefaultSettingsTests | 6 -> 6 / 6 | In RunnerDefaultTests.cs; no parameter expansion/skips. |
| RunnerDefaultsWireTests | 5 -> 5 / 5 | Same physical file. OutboundConversionPublicCreateTests (1), RunnerDefaultPlacementTests (9) and RunnerDefaultMigrationTests (5) are separate classes, not selected; there is no class named RunnerDefaultTests. |
| InstructionBundleTests | 41 -> 60 / 60 | Arguments expansions 8, 6, 3, 6 replace four single executions. No OS skips. |
| StandingPipelinePolicyDocumentationTests | 4 -> 7 / 7 currently; proposed 8 -> 11 / 11 | Four policy-copy arguments plus three ordinary existing methods; add four named V-37..40 methods. |
| TaskPlatformGuidanceTests | 5 -> 5 / 5 | Same physical file also declares the next class. |
| RunnerDefaultGuidanceTests | 4 -> 4 / 4 | Lives in TaskPlatformGuidanceTests.cs; no separate file. |
| New PipelineAdmissionSnapshotTests | proposed 10 -> 10 / 10 | V-01..10, no parameter attributes. |
| New DispatchEffectiveSettingsProjectionTests | proposed 12 -> 12 / 12 | V-11..22, no parameter attributes. |
| New DispatchEffectiveSettingsWireTests | proposed 14 -> 14 / 14 | V-23..36, no parameter attributes. |

Source inventory recipe: `rg -n '\[Test\]|\[Arguments|class |Skip|OperatingSystem|DataSource|Repeat'` over those exact files, including the two other partial files, then sum `max(1, ArgumentsCount)` per method. A static Node scan of test attributes/methods grouped by top-level class was checked against the files and produced the exact roster below. New counts derive from V-01..40, not fictional source. No OS skip/data source/repeat attribute was found in this selected roster; expected skips are zero on either platform. Shared class fixtures and the `Enumerable.Repeat` inside the budget test are not extra executions. Recount after the ordered prerequisites; counts are not frozen authority over changed source.

### Exact existing regression roster

These are current-source methods, additional to the new V-methods above. A suffix `xN` is the separately expanded Arguments count; otherwise one execution. The row filter intentionally includes these unchanged methods after the new assertions are authored. All are in `Antiphon.Tests.Application`. File order is inventory order, not a request to order TUnit execution.

**CP-1 / S1**

- `AgentTaskConcurrencyLimitTests` (25 existing executions):
  Source `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs`: `max_open_tasks_defaults_to_six_and_does_not_change_the_desktop_cap`; `max_open_tasks_rejects_zero_and_negatives` x2; `max_open_tasks_accepts_a_positive_integer`; `fourth_create_at_three_open_returns_409_concurrency_limit`; `second_debug_returns_409_on_the_role_axis`; `mixed_plan_code_debug_plus_a_fourth_is_409_absolute`; `custom_has_no_per_role_gate_until_the_absolute_cap`; `three_custom_plus_a_fourth_is_409_absolute`; `blocked_does_not_count_as_open`; `specialist_at_the_cap_still_creates`; `live_follow_up_at_the_cap_still_creates`; `retired_agent_follow_up_at_the_cap_is_refused`; `ignore_concurrency_limit_at_the_cap_creates_and_writes_a_warning`; `two_concurrent_creates_when_max_open_is_one_yield_one_200_and_one_409`; `a_stuck_finding_is_named_on_the_occupant`; `compact_stuck_drops_the_branch_prefix`; `two_projects_each_near_their_own_cap_do_not_block_each_other`; `a_role_slot_taken_in_another_project_is_free_in_this_one`; `null_project_tasks_form_their_own_bucket`; `same_project_at_the_cap_is_refused_and_names_only_its_own_occupants`; `same_project_role_axis_still_refuses_and_lists_only_this_projects_role_occupant`; `override_at_the_project_cap_creates_and_the_warning_names_the_project`; `the_lock_still_serialises_creates_within_one_project`; `a_plan_and_a_code_together_are_under_the_absolute_cap`.

- `AgentTaskPipelineStatusTests` (58 existing executions):
  Source `tests/Antiphon.Tests/Application/HostBudgetPipelineTests.cs`: `C654_Pipeline_local_in_flight_excludes_runner_bound_tasks`; `C654_Pipeline_local_in_flight_excludes_retained_capacity_wait`; `C654_Pipeline_reports_local_and_runner_limits`; `C654_Queued_remote_task_names_its_full_host_budget`.
  Source `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC557Tests.cs`: `C557_backlog_status_guard`; `C557_archived_card_guard`; `C557_archived_board_guard`; `C557_terminal_column_guard`; `C557_owner_session_guard`; `C557_open_stage_guard`; `C557_succeeded_stage_guard`; `C557_retry_and_helper_controls`; `C557_post_land_companion_guard`; `C557_rank_and_importance_order`; `C557_position_due_created_order`; `C557_due_clock_boundaries`; `C557_board_and_card_ties`; `C557_uncapped_candidates`; `C557_count_and_limit`; `C557_ready_priority_order`; `C557_ready_tie_order`; `C557_read_is_advisory_and_nonmutating`; `C557_formal_investigate_is_not_fresh`.
  Source `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`: `C448_V33_PipelineUsesLandingEvidence` x6; `empty_fleet_returns_every_visible_stage_and_omits_check`; `shipped_limits_match_role_policy_and_custom_is_unbounded`; `a_configured_limit_and_absent_limit_are_reflected`; `working_and_dispatched_are_in_flight_and_blocked_is_separate`; `last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise`; `queued_work_is_awaiting_dispatch_unless_a_live_lease_holds_it`; `queued_work_is_concurrency_cap_when_in_flight_fills_the_cap`; `a_lease_hold_outranks_the_concurrency_cap`; `a_specialist_role_working_task_does_not_count_against_the_cap` x3; `a_ready_plan_appears_on_the_code_stage`; `an_investigate_settled_next_plan_is_ready_on_plan`; `a_plan_settled_next_test_design_is_ready_on_test_design_not_code`; `a_plan_settled_next_code_is_ready_on_code`; `next_land_decide_none_yield_no_ready_even_with_a_plan_doc` x3; `a_newer_open_task_in_the_target_role_consumes_readiness`; `a_ready_row_carries_the_card_code_pin_when_one_is_set`; `a_dated_pin_is_the_queued_reason_when_nothing_else_holds_the_task`; `later_or_open_code_and_a_canceled_never_dispatched_code_are_classified`; `non_success_latest_plan_wrong_deliverable_and_card_state_suppress_ready`; `collections_sort_by_created_then_id_and_ready_by_card_priority`; `rows_carry_agent_kind_model_level_and_workspace`; `a_sibling_land_in_flight_is_the_queued_reason_after_the_lease`; `a_queued_test_design_reports_sibling_land_while_its_card_plan_is_landing`; `a_shared_task_never_reports_a_sibling_land`; `verified_plan_deliverable_accepts_only_the_plans_folder`.

**CP-2 / S2**

- `HostBudgetServiceTests` (8 existing executions):
  Source `tests/Antiphon.Tests/Application/HostBudgetServiceTests.cs`: `Runner_limit_is_the_minimum_of_budget_and_declaration`; `Null_budget_uses_local_config_and_remote_declaration`; `Zero_budget_drains_without_changing_declaration`; `Out_of_range_budget_is_rejected` x2; `Missing_reason_is_rejected`; `Unknown_host_is_not_created`; `Each_write_revises_the_budget_and_audits_old_and_new_values`.

**CP-3 / S3**

- `HostEndpointTests` (4 existing executions):
  Source `tests/Antiphon.Tests/Application/HostEndpointTests.cs`: `C654_Unknown_host_is_404`; `C654_Budget_write_requires_a_reason`; `C654_Budget_round_trip_has_revision_and_effective_limit`; `C654_Hosts_and_pipeline_agree_on_the_local_limit`.

- `RunnerDefaultSettingsTests` (6 existing executions):
  Source `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs`: `Writes_round_trip_after_restart_with_immutable_history`; `Concurrent_edits_commit_one_revision`; `Invalid_targets_kinds_and_missing_fields_leave_state_unchanged`; `Aliases_clears_and_noop_have_distinct_semantics`; `Human_protection_and_caller_attribution_are_enforced`; `Commit_and_notification_retry_do_not_duplicate_history`.

- `RunnerDefaultsWireTests` (5 existing executions):
  Source `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs`: `Put_global_affects_next_create_without_restart`; `Per_kind_override_wins_over_global`; `Clearing_override_restores_inheritance`; `Changing_defaults_does_not_move_queued_task`; `Missing_required_fields_refuse_without_clearing`.

**CP-4 / S4**

- `StandingPipelinePolicyDocumentationTests` (7 existing executions):
  Source `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`: `the_policy_phrases_are_pinned_in_every_copy` x4; `the_doc_owns_the_section_and_the_old_wip_reading_is_gone`; `the_bundle_no_longer_states_the_old_override_rule`; `the_agents_index_points_at_the_owner`.

- `InstructionBundleTests` (60 existing executions):
  Source `tests/Antiphon.Tests/Application/InstructionBundleTests.cs`: `C807_ShippedReviewExampleParses`; `Channel_sources_do_not_instruct_universal_pdf_conversion`; `Phone_with_attachments_and_append_keeps_the_command_line_budget_guard`; `C499_V08_CodeBriefCarriesTheTaskScopedProgressClaimContract`; `C499_V08b_RepairBriefNamesOwnerSourceBaselineAndAssignedCheckout`; `C470_composed_roles_separate_vr_from_pc`; `the_orchestrator_preset_prompt_is_embedded_and_not_attachable`; `the_orchestrator_preset_enables_remote_control_and_the_full_feature_pipeline`; `the_catalog_holds_exactly_the_bundles_that_ship`; `every_bundle_has_text_and_an_eight_hex_digit_content_version`; `the_version_is_the_content_hash_so_two_bundles_never_share_one`; `no_bundle_carries_a_channel_preamble_placeholder`; `a_rendered_bundle_leads_with_its_versioned_header`; `every_bundle_summarises_itself_in_its_opening_sentence`; `an_unknown_key_throws_and_names_the_ones_that_exist`; `bundles_compose_in_declared_order_under_their_headers`; `a_key_reachable_twice_is_composed_once`; `the_style_block_lands_after_the_bundles_and_the_agents_own_append_lands_last`; `with_no_bundles_the_composition_is_the_agents_own_append_byte_for_byte`; `nothing_to_compose_is_empty_so_the_flag_is_omitted_entirely`; `the_check_interpreter_contract_forwards_to_its_bundle`; `the_diagnose_contract_forwards_to_its_bundle_with_the_pinned_hard_rules`; `the_output_distiller_contract_forwards_to_its_bundle_with_the_pinned_invariants`; `the_distill_reporting_contract_never_offers_blocked_and_keeps_the_handoff_anchor`; `the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact`; `delegate_basics_carries_the_standing_rules_and_none_of_the_days_state`; `the_worst_case_composition_measured_sits_far_under_the_budget`; `an_oversized_composition_throws_and_names_what_to_shrink`; `the_test_design_composition_is_past_the_batch_command_ceiling`; `the_other_arguments_count_towards_the_budget_not_just_the_append`; `a_helper_worker_role_carries_only_the_delegate_basics` x8; `a_stage_worker_carries_its_stage_bundle_then_the_basics` x6; `an_investigate_worker_carries_the_stage_bundle_and_a_docs_worker_does_not`; `a_sub_orchestrator_carries_its_own_contract_first_then_the_basics`; `a_specialist_task_carries_nothing` x3; `the_board_api_bundle_is_on_no_role_by_default`; `each_stage_bundle_is_ascii_and_under_the_size_cap` x6; `stage_bundle_invariants_are_pinned_by_substring`; `C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces`; `C467_V21_DeliveryInventoryAndReviewAreMandatory`; `a_realistic_code_worker_composition_stays_under_the_command_line_budget`.

- `TaskPlatformGuidanceTests` (5 existing executions):
  Source `tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs`: `Stage_bundles_leave_twenty_characters_below_the_size_cap`; `Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes`; `Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning`; `Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes`; `Review_guidance_preserves_every_instruction`.

- `RunnerDefaultGuidanceTests` (4 existing executions):
  Source `tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs`: `Bundles_read_runtime_defaults_without_pinning_location`; `Platform_pins_are_explicit_and_bundles_defer_to_orchestrator_contract`; `Reroute_diagnostic_names_codex`; `Legacy_default_is_import_only_in_documented_configuration`.

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
| PC-19 | In the aggregate's missing-runner-default-snapshot branch, replace the 503 refusal with the existing initializing RunnerDefaultSettingsService.GetAsync call; do not leave an earlier missing-row guard masking this mutation. | W.Uninitialized_settings_return_503_without_seeding: initialize concurrency normally, leave only placement missing, supply valid startup defaults/runner fixtures; first assert placement/history row count remains zero, then 503. Use recording sinks, so an earlier fixture exception cannot masquerade as this red. |
| PC-20 | Omit role queue bound from admissionFreeSlots. | W.Queue_409_axes_and_precedence_match_reported_sources: zero reported slots and same role/queued limit/source as 409. |
| PC-21 | Treat parallel-full as zero fresh admission in SeparateQueues. | W.Parallel_full_holds_accepted_work_without_create_409: response admission > 0, actual accepted row Queued and held parallel. |
| PC-22 | Make DispatchSettingsEndpoints serialize its result with JsonIgnoreCondition.WhenWritingNull. | W.Null_and_zero_survive_json_serialization: GetProperty finds explicit null limit/source/timestamps. |
| PC-23 | Restore multi-route standing/Platform prerequisite in bundle. | D.Policy_uses_scoped_read_and_preserves_override_conditions: policy read uses /api/settings/dispatch and has no old prerequisite. |
| PC-24 | Append 79 non-whitespace characters to the frozen 14,068-character candidate bundle (after a changed Code base, use 14,147 minus its actual normalized length). Rebuild the embedded resource; leave the 30,000 guard unchanged. | D.Policy_bundle_is_shorter_than_the_frozen_baseline: first labelled assertion normalized length < 14146 goes red at 14,147, before a hash assertion. At this baseline the resulting full argument estimate still fits, so overflow is not an accidental earlier witness. |
| PC-25 | In only server/Bundles/orchestrator.md replace ``Final Review ends `found` only for`` with ``Final Review ends `found` also for``; preserve the new scoped route and other instructions. | D.Final_review_found_is_limited_to_regressions_and_reachable_exposures: `PC-25/final-review-only` asserts the restrictive clause in that actual policy copy, and fails before any other copy is checked. |
| PC-26 | Add one unconditional SaveChangesAsync(ct) to the successful aggregate read path, without changing its response. | W.Read_changes_no_rows_events_pins_or_settings: `PC-26/no-save` checks the recording interceptor observed zero SaveChanges calls; return/save the read normally so the red is the assertion, not a deliberately thrown fixture error. |
| PC-27 | In the new endpoint only, treat absent scope selectors as unscoped instead of validation_failed. | W.Scope_validation_and_empty_scope_are_explicit: `PC-27/missing-scope-422` is the first scenario and asserts HTTP 422 plus its code against initialized valid settings. |
| PC-28 | Pass CancellationToken.None instead of the received token into the pending database read. | W.Request_cancellation_reaches_pending_reader: `PC-28/reader-token-cancellable` checks the recorded read token immediately after the interceptor entered signal, before cancel/await. None makes this assertion false deterministically; finally releases a separate gate and joins the read even if cancellation was dropped. Never use an acknowledgement timeout as red. |

P = PipelineAdmissionSnapshotTests; E = DispatchEffectiveSettingsProjectionTests; W = DispatchEffectiveSettingsWireTests; D = StandingPipelinePolicyDocumentationTests. PC-16 has two independent variants, so this inventory prescribes **29 mutations across 28 named controls**. For every row the detecting assertion carries its PC-n label, checks the independently seeded value stated here, and runs before less-specific assertions that might mask it. PC-02 and PC-03 mutate only the aggregate/pipeline read predicate, never the gate's enforcement, so the seeded counted control stays one. PC-18/19/24/28 explicitly prevent first-witness masking. All use controlled clocks/gates where timing matters. Ordinary Code executes no deliberate production mutations; post-land Mutation runs each named method baseline-green -> mutant assertion-red -> exact restoration/rebuild -> green, preserving custody receipts. Additional discovered controls are recorded, not a silent broad test run.

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

Use **CARD-0723's checkpoint tool**, one run per committed slice group, then wait until exit is not 75. This supersedes the Plan-stage direct-driver exception: the current brief authorizes no owner-guard bypass. The launcher internally uses TUnit via `dotnet run --project tests/Antiphon.Tests`, one exact filter per row. Preserve task/session ownership environment and its source-clean checks; do not unset tokens/owner IDs, print secrets, invoke dotnet test, or fall back to a raw driver on owner-unverified. Exit 4 is an unrun slot timeout; exit 7 is an ownership refusal to report, not an invitation to evade the guard.

The sole separately accounted launcher bootstrap, needed only if a compatible tool output does not already exist, is not an extra test selection. It obtains a build slot. All CP builds/rows lease internally; do not wrap the tool's run in another slot-owning driver.

```powershell
$c881Plan = 'docs/superpowers/plans/2026-10-01-card-0881-effective-settings-endpoint-plan.md'
$c881Sha = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c881-tool-build -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c881-tool/ --nologo
# After committed/pushed S1-S3: one run for all three runtime rows.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c881-tool/ -- run --plan $c881Plan --rows CP-1,CP-2,CP-3 --expected-source-sha $c881Sha --max-wait 50s
# If exit 75: use the printed run ID, repeatedly; never start another run to poll.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c881-tool/ -- wait <run-id> --max-wait 50s
# After committed/pushed S4: refresh c881Sha, then the same run command with --rows CP-4.
# A declared red-first round selects just its slice's CP row; retain its red receipt.
```

The statically inspected start-ref CLI supports these flags. Refresh it after 0885 lands. CP-1..3 run after S1-S3 to cover the shared extraction once; CP-4 closes mandatory S-last. Preparatory reds and necessary same-row repairs are explicitly counted reruns, with exact selected classes and committed SHA. There is no repeat battery, broad Unit lane or unfiltered full-suite acceptance. Final Review selects CP-1,CP-2,CP-3,CP-4 together at its exact reviewed source.

A literal filter contains `|`; the Markdown table spells it `\|` so the importer unescapes it exactly once. Each OR class operand is parenthesized and has its trailing `*`. Do not pass a literal backslash to TUnit. Every row has its own isolated `bin-c881-sN/` build, zero output reuse. Off Windows the driver supplies UseAppHost=false. Do not co-schedule Antiphon.Agents.Pty.Tests; it is outside this roster.

Keep the tool's unedited `CHECKPOINT CP-n` line: committed SHA, build result, filter, executed/passed/failed/skipped counts, fresh TRX, slot/wait, `dirty`, `source`, `sourceState`, `buildSource` and rerun count. Validate the structured `report.json` against the same source with `scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha <sha> -Rows CP-1,CP-2,CP-3` (CP-4 for the final group). A wrong/empty/extra roster, missing argument case, skipped case, dirty source or missing build stamp cannot be called green. Static counts in this plan are not execution evidence.

### Checkpoints

Closed ordinary list: one isolated build and one exact filter per row. Red-first and necessary repair reruns are the same row, reported explicitly; no extra unlisted build. Default placement is server2/Linux with the task platform **unpinned (Any)**. Portable expected Windows counts are shown to prevent hidden OS skip accounting, **not** to commission duplicate desktop runs. **No row requires Windows. Windows-only pending roster: none (0 rows, 0 commissioned executions).** Do not run portable rows again on Windows. If a genuine native requirement is discovered, commit a named, bounded Windows row with its own exact filter/count/budget, mark it **pending for a separate Windows task**, and leave the remaining rows unpinned; do not silently duplicate this whole selection.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c881-s1/` | scoped-admission | `/*/Antiphon.Tests.Application/(PipelineAdmissionSnapshotTests*)\|(AgentTaskPipelineStatusTests*)\|(AgentTaskConcurrencyLimitTests*)/*` | V-01..10, R-1..2 | Linux 93 / Windows 93 executed; 0 failed, 0 skipped; every named class | 93 | 8 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c881-s2/` | effective-projection | `/*/Antiphon.Tests.Application/(DispatchEffectiveSettingsProjectionTests*)\|(HostBudgetServiceTests*)/*` | V-11..22, R-3 | Linux 20 / Windows 20 executed; 0 failed, 0 skipped; every named class | 20 | 6 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c881-s3/` | aggregate-wire | `/*/Antiphon.Tests.Application/(DispatchEffectiveSettingsWireTests*)\|(HostEndpointTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultsWireTests*)/*` | V-23..36, R-4..5 | Linux 29 / Windows 29 executed; 0 failed, 0 skipped; every named class | 29 | 8 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c881-s4/` | final-policy | `/*/Antiphon.Tests.Application/(StandingPipelinePolicyDocumentationTests*)\|(InstructionBundleTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)/*` | V-37..40, R-6 | Linux 80 / Windows 80 executed; 0 failed, 0 skipped; every named class | 80 | 5 |

## Activation and rollback

After Review/land, activation is an operator-owned ordinary server deployment. This Plan performs none. Do not enable SeparateQueues, raise/lower concurrency, write host budgets, alter runner defaults or send test dispatches to prove a read feature. Confirm active `/api/version` SHA directly, then read the scoped new route and compare its counts/limits to the existing pipeline/host/catalogue/defaults on a quiet observation; record any intervening task/config changes. Missing endpoint on the running SHA means not activated; use the documented interim read procedure and defer when admission headroom is unknown.

Rebuild computes bundle version from the changed Markdown. Activation acceptance includes a newly composed orchestrator carrying that version (or the existing idle refresh path), with the effective-settings read instruction and a passing full argv budget check. Do not kill a working session just to refresh policy. CARD-0822 integration/notification activation belongs to its subsequent delivery and must use this projection.

Rollback is a reviewed revert of this card's read projection/route/policy changes together. Keep CARD-0505's settings/gates and persisted values/audit history; optional seed-origin metadata is additive and may remain. Revert S4's route instruction with the route so orchestrators do not call a removed API. No destructive schema rollback, settings reset or runner capacity write is needed.

### Cost

Estimates, not measured timings: ordinary Code green floor = **27 minutes** (8 + 6 + 8 + 5), four isolated builds included; allow 2–5 minutes for the explicitly accounted tool bootstrap when needed, and report slot waits separately. Roster = **222 executions** (93 + 20 + 29 + 80), all on Linux, zero planned skips. If run on Windows the portable roster is also 222/zero skips; **Windows commissioned count is zero**. Red-first rounds add up to another 27 minutes; necessary same-row fixes are separately reported. Final Review reruns the complete green list, estimated another 27 minutes. No repeated full suite or repeated launcher bootstrap is budgeted.

TestDesign completed under the assigned 45-minute box. Future prerequisite census/budget refresh estimate 15–30 minutes. Code authoring estimate 4–6 hours plus the 27-minute green floor and explicitly recorded red rounds; choose ExpectAbout from that sum, not the 222 test count. Mutation has **29 variants**, estimated 3 minutes each including its baseline/red/restored-green build/run cycle = 87 minutes, plus 15 minutes for custody/restoration/reporting = **102 minutes**. Ordinary Code green + Final Review + Mutation estimate = 156 minutes, excluding authoring and test-first reds. Actual build-slot waits are reported separately. The seed-origin amendment and shared read extraction are the main implementation costs; no measured speedup is claimed.
