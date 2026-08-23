# CARD-0106 — launch-time env overrides and project-level default env: plan

**Date:** 2026-08-23 · **Card:** CARD-0106 (`820ee76f-1559-46f6-9641-ab843975d2a5`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `4909bbf` (feat/card-task-83059a03, even with master at the time of writing).
Every line number below was re-read out of the code on that commit.

**Established fact, not re-derived here:** the Investigate stage (task `fdeccacc`, recorded on the
card 2026-08-23). CARD-0106's own S1–S3 are SHIPPED and working — the `ApiKey` store,
`{{key:NAME}}` resolution over the fully-merged env, `Agent.LaunchEnvJson` wired into both launch
paths, and the full UI. None of that is re-designed here. The per-agent BYO-endpoint use case (a
proxy URL + key for one agent) works today with zero new code. This plan designs **exactly the two
confirmed gaps**: (1) no launch-time env override on the HTTP surface, and (2) no project-level
default env.

**Related:** CARD-0114 (TuiSecret convergence — reuses, does not change, the mechanism here),
CARD-0115 (`AgentTask.ProjectId` / `Agent.PoolProjectId` — the provenance rail project defaults
ride to pool delegates), CARD-0140 (profile-aware delegate launches — the second finalization path
every layer must exist in), CARD-0006 (why ANTIPHON_* must always win).

---

## Verdict up front

1. **Overlay, not replace.** The launch-time override is a NEW merge layer
   (`AgentLaunchOptions.LaunchEnvOverride`), applied after the agent's `LaunchEnvJson` and before
   `ExtraEnv`, in both places `Env` is finalized. The existing `AgentEnv` hook's `??`-replace
   semantics (`AgentTuiLaunchResolver.cs:42,307`) are deliberately left untouched — they are an
   internal contract ("an explicit caller can say *no agent env at all* and have it stick"), not a
   surface anything HTTP-facing uses, and overloading them would force every call site to re-derive
   and pre-merge the stored env, duplicating the funnel's null-derive logic at five sites.

2. **ANTIPHON_\* is protected twice, independently.** First by merge order — the override layer sits
   BEFORE `ExtraEnv` in both resolvers, so the orchestration block wins mechanically, exactly as
   `AgentLaunchEnvTests.the_agent_env_beats_the_definition_env_and_loses_to_the_orchestration_block`
   already pins for the agent layer. Second by write-time refusal — every NEW write surface (the two
   override DTO fields, the card-spawn field, the project default) rejects any name starting
   `ANTIPHON_` with a 422 naming the variable, so an operator finds out their override would be
   inert instead of silently losing. The existing `PATCH /agents` `LaunchEnv` surface keeps its
   shipped behavior (ANTIPHON_* accepted-but-inert): refusing there now could 422 an already-stored
   config on its next unrelated save.

3. **Project defaults are a column, not a child table**: `Project.DefaultLaunchEnvJson`, mirroring
   `Agent.LaunchEnvJson` (`Agent.cs:79`) byte-for-byte in shape so `AgentLaunchEnv`
   Parse/Validate/Serialize (`AgentLaunchEnv.cs:24,45,58`) is reused whole. `ApiKey` is a child
   table because each key is an individually-named, individually-encrypted secret with its own CRUD
   lifecycle; a default env is a small plain dict whose secret values already travel through
   `{{key:NAME}}` indirection — nothing in it needs a row identity or encryption of its own.

4. **The project a launch's defaults come from IS the project its API keys resolve against.** One
   identity, decided once, used twice. Every finalization site already answers "which project is
   this launch under" for key scoping (`options.ApiKeyProjectId ?? board→project`;
   `task.ProjectId` first on the delegate path, `AgentTaskDispatcher.cs:1932`;
   `card.Board.ProjectId` on card spawns, `CardService.cs:610`). Project defaults piggyback on that
   exact answer — a launch whose keys resolve project-scoped also inherits that project's default
   env, and a launch with no trustworthy project identity gets neither.

5. **Pool delegates DO inherit project defaults** — via `AgentTask.ProjectId` (CARD-0115,
   `AgentTask.cs:38-45`), which is set at creation from caller provenance and is already what the
   profile-path delegate launch resolves keys against. This is exactly the case project defaults
   are FOR: a delegate spun up under a project's board gets that project's proxy/key setup without
   any per-agent config existing. A task with `ProjectId == null` (no trustworthy provenance) gets
   no defaults, consistent with its global-only key resolution — path-derived guessing stays
   rejected.

**Precedence, final:** launch-time override > agent `LaunchEnvJson` > project default >
profile/definition env — with two unmoved brackets around it: kind gap-fills
(`DISABLE_AUTOUPDATER` etc.) still only fill absent names after all of it, and the ANTIPHON_*
`ExtraEnv` block still lands last and wins unconditionally. `{{key:NAME}}` resolution then runs
over the fully-merged result, and the `BuildRuntimeLaunchSpec` tripwire still refuses any
surviving placeholder.

---

## What the code does today, re-read on `4909bbf`

### The two finalization paths and their merge order

`AgentLaunchOptions` (`server/Application/Dtos/AgentLaunchSpec.cs:9-27`) carries `ExtraArgs`,
`ExtraEnv` (the ANTIPHON_* block), `AgentEnv` (CARD-0106 S2, null = derive from the agent), and
`ApiKeyProjectId` (null = derive from the agent's board).

**Path A — registry (no managed profile):** `AgentRegistry.Resolve` (`AgentRegistry.cs:102-160`)
merges `def.Env` (:116) → `options.AgentEnv` (:121-125) → `options.ExtraEnv` (:127-131), then
gap-fills kind defaults (:137-160, `ContainsKey`-guarded). It is sync and DB-free; API keys are
resolved by the CALLER after it returns — `ResolveLegacyAsync`
(`AgentTuiLaunchResolver.cs:99-116`), the dispatcher's profile-null arm
(`AgentTaskDispatcher.cs:1649-1658`), and `CardService`'s explicit-definition arm
(`CardService.cs:577-594`) each call `ApiKeyEnvResolver.ResolveSpecAsync` on the finished spec.

**Path B — managed profile:** `AgentTuiLaunchResolver.ResolveCoreAsync` merges profile non-secret
env + decrypted `AgentTuiSecret`s (:280-299) → `options.AgentEnv ?? ParseForAgent(agent)` (:307) →
`options.ExtraEnv` (:310-314), applies kind defaults (:332-333), computes
`projectId = options.ApiKeyProjectId ?? board→project` (:360-361), refuses placeholders in args
(:362-363), and resolves `{{key:NAME}}` over the merged env (:364-370).

**The funnel:** `AgentLaunchResolution.ResolveForAgentAsync` (`AgentTuiLaunchResolver.cs:29-68`)
attaches `AgentEnv = options.AgentEnv ?? AgentLaunchEnv.ParseForAgent(agent)` (:40-43) so its five
call sites can't forget, then routes to path B or falls back to A.

### The launch entry points

- **Interactive start:** `AgentControlService.StartAsync` (`AgentControlService.cs:85-145`) takes
  `StartAgentRequest` (`AgentDtos.cs:260-268` — `RemoteControl`, `Fresh`,
  `IgnoreSubscriptionQuota`, nothing env-shaped). Cardless: `StartInteractiveSessionAsync` builds
  the ANTIPHON_* `extraEnv` (:172-177) and calls the funnel (:275-285) — the spec is resolved
  in-line and handed to the launch queue, so nothing needs persisting for an override to reach it.
  With a queued card: it detours through `CardService.SpawnAsync` with a `SpawnCardRequest`
  (:125-128; DTO at `BoardDtos.cs:228-236`, also env-less).
- **Task dispatch:** `CreateAgentTaskRequest` (`AgentTaskDtos.cs:10-58`) has no env field. The task
  row persists everything dispatch needs; launch happens later from
  `BuildLaunchSpec`/`BuildLaunchSpecAsync` (`AgentTaskDispatcher.cs:1877-1958`), with
  `AgentEnv: AgentLaunchEnv.ParseForAgent(agent)` (:1905, :1945) and
  `ExtraEnv: BuildEnv(task, agent, session)` (:1906, :1946; `BuildEnv` at :2059-2075 — `ANTIPHON_API`,
  `ANTIPHON_SESSION_ID`, `ANTIPHON_AGENT_ID`, `ANTIPHON_TASK_ID`, `ANTIPHON_TASK_TOKEN`).
- **Warm reuse:** a Shared-workspace task can be claimed by an already-running warm delegate
  (`TryReuseWarmAgentAsync`, :2217, called at :1508) — **no process launch happens**, so no env of
  any kind can reach it. Same for a `FollowUpOnTask` pin delivered between turns.

### The pinned expectations

`tests/Antiphon.Tests/ApiKeys/AgentLaunchEnvTests.cs` pins: agent env beats definition env and
loses to `ExtraEnv` (:27-56); null and empty `AgentEnv` are distinct but both harmless (:71-82);
write-time validation (placeholder-in-name refused, malformed placeholder refused, oversize value
refused without echo). Nothing in this plan changes any of those tests — both new layers default to
null/empty, so every existing assertion holds unmodified (verification item d).

### `Project` today

`Project.cs:7-27` — no env concept; `ApiKeys` child collection is the only launch-adjacent thing.
DTOs are plain full-replace records (`CreateProjectRequest.cs`, `UpdateProjectRequest.cs`,
`ProjectDto.cs`), applied in `ProjectService.UpdateAsync` (`ProjectService.cs:81`), exposed at
`ProjectEndpoints.cs:41`. UI: `ProjectConfig.tsx` edit modal already embeds
`<ApiKeysSection projectId={...}/>` (:450).

---

## Design

### Gap 1 — launch-time override

#### D1. New merge layer: `AgentLaunchOptions.LaunchEnvOverride`

```csharp
// AgentLaunchSpec.cs — AgentLaunchOptions gains:
IReadOnlyDictionary<string, string>? LaunchEnvOverride = null,
```

Merged in **both** finalization paths immediately after the agent-env layer:

- `AgentRegistry.Resolve`: after the `options.AgentEnv` loop (:121-125), before `options.ExtraEnv`
  (:127).
- `ResolveCoreAsync`: after the `options.AgentEnv ?? ParseForAgent(agent)` loop (:307-308), before
  `options.ExtraEnv` (:310).

Because it sits before `ExtraEnv`, ANTIPHON_* wins mechanically even if a reserved name somehow
reaches the layer (defense in depth behind D3's write-time refusal). Because it sits before the
kind gap-fills, an override CAN deliberately set `DISABLE_AUTOUPDATER=0` etc. — same freedom the
agent layer already has. Values may carry `{{key:NAME}}`: resolution runs over the merged env after
all layers, so this is free and consistent (D9).

No funnel-level null-derive is needed (there is nothing to derive an override *from*); the funnel
passes it through untouched.

#### D2. Write-time refusal of reserved names

New `AgentLaunchEnv.ValidateOverride(env, field)` = `Validate(env, field)` plus: any name matching
`ANTIPHON_` prefix (OrdinalIgnoreCase) → 422
`"'{name}' is Antiphon's own orchestration plumbing and cannot be overridden."` Reject, don't
strip: a stripped key is a silent no-op the operator discovers only when their launch behaves as if
they'd typed nothing. Applied to every surface in D3/D6; NOT retrofitted onto the existing agent
`PATCH` (`AgentService.cs:357`) — see verdict 2.

#### D3. DTO surfaces and wiring

- **`StartAgentRequest`** (`AgentDtos.cs:260`) gains
  `IReadOnlyDictionary<string, string>? LaunchEnvOverride = null` — "this launch only", the same
  per-start-override contract `RemoteControl` already documents on that record. Wiring:
  `StartAsync` validates via `ValidateOverride`, threads it to `StartInteractiveSessionAsync` →
  `AgentLaunchOptions.LaunchEnvOverride`; on the card branch it threads through `SpawnCardRequest`
  (below). **Ephemeral by design:** the AlwaysOn supervisor's restart and the resume-recovery
  relaunch rebuild the spec from persisted config, so an interactive override does not survive a
  restart — a durable change is what `PATCH launchEnv` is for. Stated in the DTO doc-comment.
- **`SpawnCardRequest`** (`BoardDtos.cs:228`) gains the same field; `CardService.SpawnAsync` passes
  it into all three of its option constructions (:577, :602, :623). This keeps "start agent with
  queued card" and "start agent cardless" behaviorally identical, and gives the board-spawn UI the
  hook for free.
- **`CreateAgentTaskRequest`** (`AgentTaskDtos.cs:10`) gains the same field. Dispatch is
  asynchronous, so it must persist: new column **`AgentTask.LaunchEnvOverrideJson`**
  (`string`, default `"{}"`, migration `AddAgentTaskLaunchEnvOverride`) written in
  `AgentTaskService.CreateAsync` after `ValidateOverride`, serialized via `AgentLaunchEnv.Serialize`.
  `BuildLaunchSpec` and `BuildLaunchSpecAsync` pass
  `LaunchEnvOverride: AgentLaunchEnv.Parse(task.LaunchEnvOverrideJson)` into their options
  (:1895-1906, :1940-1947). Persisting on the task row means a task-session relaunch after a crash
  re-applies the override — correct, because the override was *for that task*.
- **Naming:** `LaunchEnvOverride` (not `EnvOverride`) — it namespaces next to the shipped
  `LaunchEnv`/`LaunchEnvJson` family so the relationship is legible, and `Launch` says *when* it
  applies.

#### D4. Interactions that would silently drop an override

- **Warm reuse:** a task whose `LaunchEnvOverrideJson` parses non-empty is **excluded from warm
  reuse** — `TryReuseWarmAgentAsync` (:2217) declines before candidate matching (fall through to a
  fresh spawn, which is the only thing that can honor a process-level env). A reused process
  cannot change its environment; reusing anyway would mark the task running with the operator's
  override silently ignored — the exact "wrong/no proxy reaching a launched agent" bug this card
  exists to prevent.
- **`FollowUpOnTask` + override:** refused 422 at create. A follow-up's entire point is continuing
  an EXISTING process's context; there is no launch for the override to apply to, and "sometimes it
  applies, if the agent happened to die and relaunch" is a coin-flip contract. The error tells the
  caller to either drop `-OnAgent` or PATCH the agent's `launchEnv`.
- **No cascade to child tasks:** a sub-orchestrator's delegates are separate creates; the override
  does not auto-inherit (only `ProjectId` provenance does). Blanketing a subtree is what project
  defaults (gap 2) are for. Stated in the DTO doc-comment.

#### D5. `delegate.ps1`

New `Create`-set parameter `[hashtable]$EnvOverride` (not `$Env` — shadowing the `Env:` drive name
in a script is asking for confusion), sent as
`if ($EnvOverride -and $EnvOverride.Count -gt 0) { $body['launchEnvOverride'] = $EnvOverride }`
next to :196. Comment documents the two rules a caller feels: ANTIPHON_* refused, and the task
won't reuse a warm delegate. Usage:
`delegate.ps1 Code -Goal ... -EnvOverride @{ ANTHROPIC_BASE_URL='http://proxy:8080'; ANTHROPIC_API_KEY='{{key:proxy-key}}' }`.
ASCII-only, per the script's own header rule.

### Gap 2 — project-level default env

#### D6. Schema and API

- **`Project.DefaultLaunchEnvJson`** (`string`, NOT NULL, default `"{}"`), migration
  `AddProjectDefaultLaunchEnv` — every existing row backfills to `"{}"` via the column default;
  `AppDbContext` config mirrors `Agent.LaunchEnvJson`'s (`AppDbContext.cs:782`). Unreadable JSON
  reads as empty via `AgentLaunchEnv.Parse` — same "never kill a launch over a corrupt optional
  column" stance, same reasoning (`AgentLaunchEnv.cs:10-14`).
- **DTOs:** `ProjectDto` gains `IReadOnlyDictionary<string, string> DefaultLaunchEnv` (parsed).
  `UpdateProjectRequest` and `CreateProjectRequest` gain
  `IReadOnlyDictionary<string, string>? DefaultLaunchEnv = null` — **null = leave unchanged, empty
  = explicit clear**, the `UpdateAgentRequest.LaunchEnv` contract (`AgentDtos.cs:245-248`), even
  though the rest of `UpdateProjectRequest` is full-replace: an older UI build PUTting a project
  must not wipe a default env somebody configured. Applied in `ProjectService.UpdateAsync`
  (`ProjectService.cs:81`) via `ValidateOverride` (reserved names refused here too — a project
  default clobbering `ANTIPHON_SESSION_ID` for every agent under the project is the worst version
  of the CARD-0006 shape) then `Serialize`.

#### D7. Resolution: where the layer is fetched and merged

New `AgentLaunchOptions.ProjectDefaultEnv` (`IReadOnlyDictionary<string, string>?`), merged in both
paths **immediately before** the agent-env layer:

- `AgentRegistry.Resolve`: between `def.Env` (:116) and `options.AgentEnv` (:121).
- `ResolveCoreAsync`: between the profile env/secrets block (:280-299) and `options.AgentEnv`
  (:307). The profile loses to the project default deliberately: a profile is a shared
  program-template ("how to run Grok"), the project default is a statement about the environment
  this project's agents run in — the more specific intent for a *credential/endpoint* fact.

Fetching rides the existing project-identity answer (verdict 4). New method on `ApiKeyEnvResolver`
— it already owns `ResolveProjectIdAsync` (:52-62) and is plumbed to every site that needs this:

```csharp
public async Task<IReadOnlyDictionary<string, string>> GetProjectDefaultEnvAsync(
    Guid? projectId, CancellationToken ct)  // null → AgentLaunchEnv.Empty
```

Populated at the same four places the projectId decision already lives, hoisted so it is made ONCE
per launch:

1. **The funnel** (`AgentLaunchResolution.ResolveForAgentAsync` / `ResolveDefaultAsync`): compute
   `projectId = options.ApiKeyProjectId ?? await resolver.ResolveProjectIdAsync(agent?.BoardId)`,
   then `options = options with { ApiKeyProjectId = projectId, ProjectDefaultEnv = await
   resolver.GetProjectDefaultEnvAsync(projectId) }`. Both downstream resolvers' existing
   `options.ApiKeyProjectId ?? …` fallbacks (:109-110, :360-361) still stand for any caller that
   bypasses the funnel, and now read the hoisted value when it went through it — one decision, no
   second derivation that could disagree. With no `ApiKeyEnvResolver` (older test harnesses), no
   defaults and no keys: degrades exactly as today.
2. **Dispatcher, profile-null arm** (`BuildLaunchSpecAsync`, :1923-1924): before delegating to sync
   `BuildLaunchSpec`, compute `projectId = task.ProjectId ?? board→project` (the same expression
   :1932-1934 already uses) and pass the fetched defaults into `BuildLaunchSpec` as a new optional
   parameter → `options.ProjectDefaultEnv`. The sync `BuildLaunchSpec` signature stays
   default-compatible for the four suites that call it directly.
3. **Dispatcher, profile arm** (:1936-1949): already sets `ApiKeyProjectId` — the funnel hoist (1)
   covers it with zero new code at this site.
4. **CardService explicit-definition arm** (:577-594): the one finalization site outside the
   funnel — fetch with `card.Board.ProjectId` and pass in the options, mirroring its existing
   post-resolve `ResolveSpecAsync` call.

**Interactive starts get project defaults through the agent's board** (`Agent.BoardId →
Board.ProjectId`), the same mapping keys use — an agent on no board gets global keys and no project
defaults. `Agent.PoolProjectId` (`Agent.cs:129`) stays what CARD-0115 made it: a pool-matching
stamp, not a resolution input — the TASK's ProjectId is the provenance rail (verdict 5), and a warm
pool row's stamp already mirrors the task it last ran (`AgentTaskDispatcher.cs:1586`).

#### D8. Placeholders in project defaults (confirming card question 2c)

Yes, unchanged: `{{key:NAME}}` in a project-default VALUE resolves at the same final resolution
point (`ResolveCoreAsync` :348-371; `ResolveSpecAsync` on the registry paths), against the same
`projectId` the default came from — so a project default can reference that project's own scoped
key, which is the expected idiom (`ANTHROPIC_API_KEY={{key:proxy-key}}` where `proxy-key` is
project-scoped). Names may not carry placeholders (existing `Validate` rule, reused). No assumption
in `ApiKeyEnvResolver.ResolveWithSecretValuesAsync` (:113-236) cares which layer contributed a
value — it iterates the merged dict. Verified: nothing breaks.

#### D9. UI

`ProjectConfig.tsx` edit modal gains a "Default launch environment (KEY=value per line)" `Textarea`
above the `ApiKeysSection` embed (:450), reusing `envToText`/`parseEnvironmentText`
(`client/src/shared/environmentText.ts:7,17`) and the exact label/description pattern of
`AgentSettingsModal.tsx:303-310` — description text: "inherited by every agent and pool delegate
under this project unless its own launch env sets the same variable; values may reference stored
API keys as {{key:NAME}}". `client/src/api` project types gain `defaultLaunchEnv`. Send the parsed
dict always (empty = clear), matching how the modal handles save; null is reserved for older
callers. No launch-time-override UI is planned — that surface is `delegate.ps1`/API-first by the
card's own motivation; the board spawn dialog can adopt `SpawnCardRequest.LaunchEnvOverride` later
without server work.

---

## What is deliberately NOT built

- **No replace-mode override.** Nobody asked for "wipe the agent's env for this launch"; the
  `AgentEnv` explicit-empty hook already exists internally if a future card needs to expose it.
- **No per-launch-override persistence for interactive starts** (session-row column). Restart
  semantics are documented instead; the durable path exists (`PATCH launchEnv`).
- **No ANTIPHON_* refusal retrofit on the shipped agent PATCH surface** (verdict 2).
- **No project-default child table, no per-entry encryption** — secrets stay in `ApiKey` rows,
  referenced by placeholder (verdict 3).
- **No path-derived project identity** — unchanged from S2's rejection; a task/agent without
  provenance gets no defaults.
- **No security boundary.** Per the card's scope note this is a trusted-environment convenience;
  the reserved-name refusal is correctness (protecting the plumbing from accidents), not access
  control.

---

## Verification / test design

All server tests live beside the shipped suite in `tests/Antiphon.Tests/ApiKeys/` unless noted;
run via `dotnet run --project tests/Antiphon.Tests --treenode-filter` per repo convention (alternate
`OutputPath` while daemons run).

**(a) Override reaches the process; ANTIPHON_* cannot be clobbered.**
- `AgentLaunchEnvTests` (extend): `the_launch_override_beats_the_agent_env_and_loses_to_the_orchestration_block`
  — `AgentEnv` and `LaunchEnvOverride` contest a key (override wins), override sets
  `ANTIPHON_SESSION_ID` maliciously and `ExtraEnv` still wins. Mirror through `ResolveCoreAsync`
  in the profile-path suite so BOTH finalizers are pinned, not just the registry.
- `ValidateOverride`: `ANTIPHON_TASK_TOKEN` (and lowercase `antiphon_x`) refused 422 naming the
  variable; ordinary names pass; `{{key:NAME}}` values pass.
- Dispatcher-level (extend `ApiKeyLaunchPathTests` harness): task created with
  `LaunchEnvOverride` → `BuildLaunchSpec(Async)` env carries the override value AND the intact
  `ANTIPHON_SESSION_ID`/`ANTIPHON_TASK_TOKEN` from `BuildEnv`; round-trips
  `AgentTask.LaunchEnvOverrideJson`.
- API-level: `CreateAgentTaskRequest`/`StartAgentRequest` with an ANTIPHON_* override name → 422
  (pins that validation runs at the boundary, not only in the helper).

**(b) Agent's own `LaunchEnvJson` beats a project default.**
- `AgentLaunchEnvTests` (extend): `ProjectDefaultEnv` and `AgentEnv` contest a key — agent wins;
  project default beats definition env; a project-default-only key is present. Same trio through
  `ResolveCoreAsync` (project default vs profile env: project wins).
- Full chain, one test: definition < project default < agent < override < ExtraEnv, five layers
  contesting one key each pairwise-adjacent — the single test that fails loudest if a future edit
  reorders a loop.

**(c) A project default reaches a POOL DELEGATE.**
- Integration (dispatcher harness, DB-backed): project with `DefaultLaunchEnvJson` →
  board under it → task created with that provenance (`ProjectId` set, no pinned agent, fresh pool
  spawn) → `BuildLaunchSpecAsync` env carries the default; sibling test with a
  `{{key:NAME}}`-valued default referencing a PROJECT-scoped key resolves to that project's value
  (pins D8 end-to-end on the delegate path). Negative: `task.ProjectId == null` → no defaults.
- Interactive path: agent on a board under the project → funnel-resolved spec carries the default.

**(d) Existing pinned expectations pass unmodified.** Every current `AgentLaunchEnvTests` case
constructs options without the two new fields (null defaults) — zero behavioral delta by
construction. The build slice must run the full `ApiKeys` + dispatcher suites and report them
green WITHOUT edits to any existing test file; an edit there is a red flag to justify, not a fixup.

**(e) The drop-guards.**
- Warm reuse declined for an override-carrying task (`TryReuseWarmAgentAsync` returns the decline
  outcome; the task then spawns fresh) — and NOT declined for `"{}"`.
- `FollowUpOnTask` + `LaunchEnvOverride` → 422 at create.

**(f) Project API + UI.**
- `ProjectService`: update with `DefaultLaunchEnv` null leaves stored value; empty dict clears;
  ANTIPHON_* refused; `ProjectDto` round-trips.
- Client (`pwsh -File scripts/test-client.ps1`): `ProjectConfig` default-env textarea renders
  stored env, save sends parsed dict — pattern of `AgentLaunchEnv.test.tsx` /
  `ApiKeysSection.test.tsx`.

---

## Slices

- **S1 — the two layers, mechanically.** `AgentLaunchOptions.LaunchEnvOverride` +
  `ProjectDefaultEnv`; merge insertion in `AgentRegistry.Resolve` and `ResolveCoreAsync`;
  `AgentLaunchEnv.ValidateOverride`. Tests (a-unit), (b), (d). No HTTP surface yet — pure,
  fast-feedback core.
- **S2 — launch-time override surface (gap 1 feature-complete).** DTO fields on
  `StartAgentRequest`/`SpawnCardRequest`/`CreateAgentTaskRequest`; `AgentTask.LaunchEnvOverrideJson`
  migration; wiring in `AgentControlService`/`CardService`/`AgentTaskService`/dispatcher; reuse
  decline + follow-up 422; `delegate.ps1 -EnvOverride`. Tests (a-integration/API), (e).
- **S3 — project defaults (gap 2 feature-complete via API).** `Project.DefaultLaunchEnvJson`
  migration; `ApiKeyEnvResolver.GetProjectDefaultEnvAsync`; funnel hoist + the two non-funnel fetch
  sites; project DTO/service changes. Tests (c), (f-server).
- **S4 — UI + housekeeping.** `ProjectConfig` textarea, client api types, vitest; card updated with
  shipped-state note (the omission that made this card need an investigation stage).

S1→S2 and S1→S3 are ordered; S2 and S3 are independent of each other. S4 last.
