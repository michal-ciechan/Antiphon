# CARD-0260 + CARD-0259 — Delegates must inherit the caller's LLM project/key routing

**Date:** 2026-08-30 · **Status:** plan (Plan pass; nothing built). Ground truth verified against
master `bfb8027`. Both cards were filed by PM-Orchestrator-Grok (a sibling Antiphon-hosted project,
`D:\src\project\predictionMarkets`) with production evidence; this plan re-verified every code
claim against the current tree rather than trusting the cards' line references. One root cause,
one design, both cards.

## Verdict up front

1. **The diagnosis is confirmed.** The merge order in `AgentTuiLaunchResolver.ResolveCoreAsync`
   (`server/Application/Services/AgentTuiLaunchResolver.cs:352-378`) and its profile-less twin
   `AgentRegistry.Resolve` (`server/Application/Services/AgentRegistry.cs:127-155`) is exactly the
   documented profile → project default → agent `LaunchEnvJson` → task override → `ExtraEnv`.
   **No step anywhere copies the CALLER's LLM routing env onto a child** — not in
   `AgentTaskService.CreateAsync`, not in `AgentTaskDispatcher.BuildLaunchSpec`/`Async`, not in
   `delegate.ps1`. A parent whose own `Agent.LaunchEnvJson` carries `X_LLM_PROJECT` launches pool
   children whose agent rows are born `LaunchEnvJson = "{}"` (`AgentTaskDispatcher.cs:2435`), so
   the child gets nothing unless the create body passed `launchEnvOverride` — which none of the
   observed creates did.
2. **Half the machinery already exists and the cards under-credit it.** A child task inherits its
   parent's `ProjectId` at create (`AgentTaskService.cs:270-272`, CARD-0115 S1: "its commissioning
   project — not its filesystem path — decides the eventual API-key scope"); both dispatcher launch
   paths fetch `Project.DefaultLaunchEnvJson` for that project (`AgentTaskDispatcher.cs:2244-2250`
   and the funnel's `AttachProjectContextAsync`); and warm-pool reuse is already gated on
   `PoolProjectId == claimed.ProjectId` (`AgentTaskDispatcher.cs:2632`) precisely because "a warm
   process retains its launch environment". The `LaunchEnvOverride` DTO doc already says the
   intended subtree mechanism out loud: *"Does not cascade to child tasks; blanket a subtree with
   a project default"* (`AgentTaskDtos.cs:66`). **Seeding the PredictionMarkets project's
   `DefaultLaunchEnv` with `X_LLM_PROJECT` + the proxy base URLs would have prevented the observed
   miss with zero code changes.** That is the operator action (§6) — but it is opt-in per project
   and silently absent, which is why the code changes in §3-§5 are still owed.
3. **Build three things:** (S1) a create-time **inherited-env snapshot** on the task row, sourced
   from the caller's Antiphon-visible env layers, filtered to a configured LLM-routing name list,
   merged below the agent's own env and the explicit override; (S2) a create-time **422
   `llm_project_required`** when the env that would reach the child points at a local key-proxy
   with no project marker; (S3) `delegate.ps1` forwarding `X_LLM_PROJECT`/`X_LLM_KEY` from the
   caller's *process* env through a new request field (the server cannot see a process env).
4. **CARD-0256 is NOT fixed as a side effect** (§7). Its own Plan pass should proceed on the
   dead-session labeling and empty-transcript fail-fast regardless of this card.

## 1. Diagnosis verification — corrections to the cards' facts

- **`X_LLM_PROJECT` and `X_LLM_KEY` appear NOWHERE in this codebase** (grep, whole tree). They are
  a Mikey.LlmKeyProxy convention that reaches the CLI through env passthrough only. This is fine —
  the design below treats the inherit list as opaque names — but it means Antiphon must not
  hardcode semantics for them beyond a configurable "project marker" name for the §4 gate.
- **`GROK_BASE_URL` and `OPENAI_BASE_URL` are also not Antiphon names.** The real, code-verified
  variables are: Claude `ANTHROPIC_BASE_URL` + `ANTHROPIC_API_KEY` (`RealCliStubEnv.ForClaude`,
  agent-kinds §4); Grok `GROK_CLI_CHAT_PROXY_BASE_URL` (the chat redirect — `GROK_XAI_API_BASE_URL`
  alone is the documented false safety, agent-credentials §6); Codex `OPENAI_API_KEY` **plus five
  `-c` launch arguments, not a base-URL env var** (agent-kinds §7). `GROK_BASE_URL` may still be
  read by the wrapper on the PM machine; it stays in the default inherit list as an opaque name,
  but the plan must not pretend Antiphon knows what it does. **Consequence for Codex: env
  inheritance cannot re-route a Codex child through a proxy** — that needs a TUI profile carrying
  the `-c` args, and is explicitly out of scope here (§8).
- **CARD-0260's "two stacked misses" framing is right**, and the second one bounds what
  inheritance alone can deliver: a Grok parent whose Antiphon-visible layers carry only
  `X_LLM_PROJECT` (the observed state — `launchEnv = { "X_LLM_PROJECT": "PredictionMarkets" }`,
  no URLs) has **no `ANTHROPIC_BASE_URL` for a Claude child to inherit**. Copying the caller's env
  faithfully still leaves that Claude child wrapper-managed. A cross-provider auto-mapping ("if
  any inherited URL points at the proxy, synthesize the child kind's own URL") was considered and
  **rejected**: it bakes proxy topology into Antiphon and can silently re-bill a path nobody chose.
  The full per-provider set belongs in `Project.DefaultLaunchEnv` (one operator write per project,
  §6); inheritance is the caller-follows fallback; the §4 gate + warning make the remaining gap
  loud instead of silent.
- **CARD-0020 precedent, read as asked:** its S1 fixed the same class (a token-less caller
  silently inheriting the server process's cwd) by making identity flow from the caller's token at
  create time, and by refusing loudly instead of proceeding wrong. Same shape here: inherit from
  the authenticated caller at create; refuse (422) the one state that is never right.

## 2. The caller's env, as the server can actually see it

`AgentTaskService.AuthenticateAsync` yields two caller shapes (`AgentTaskService.cs:70-92`):

| Caller shape | How to reach its Antiphon-visible env layers |
|---|---|
| **Task token** (a sub-orchestrator running as a task) | `caller.Task.LaunchEnvOverrideJson` over (`caller.Task.AgentId` → `Agent.LaunchEnvJson`) |
| **Session token** (a standing agent, e.g. PM-Orchestrator-Grok) | `Agents.PersistentSessionId == sessionId.ToString("D")` → `Agent.LaunchEnvJson` (the exact join `DeriveCallerProjectAsync` already uses, `AgentTaskService.cs:404-410`) |

Deliberately **excluded** from the snapshot:

- **The caller's project-default layer** — the child re-derives it live at dispatch from
  `task.ProjectId`, which equals the parent's project by the existing CARD-0115 chain. Snapshotting
  it would freeze a value the project owner can still edit.
- **The caller's TUI-profile layer** — a profile is keyed to a kind; inheriting a Claude profile's
  env onto a Grok child is exactly the cross-kind mistake this card exists to stop. Accepted
  limitation, stated in §4.
- **The caller's live process env** — invisible to the server. That is what S3 (`delegate.ps1`)
  is for.

## 3. S1 — Inherited-env snapshot at create

**New column** `AgentTask.InheritedLaunchEnvJson` (default `"{}"`, same shape as
`LaunchEnvOverrideJson`; migration `AddAgentTaskInheritedLaunchEnv`). Snapshot at create, not
recompute at dispatch: "everything that decides WHAT a delegate will be happens at creation"
(`AgentTaskService` header comment), the §4 gate needs the same computation at create anyway, and
a snapshot covers every dispatch path (tick, retry, relaunch) without each learning the rule.

**Computation** (new `ComputeInheritedLlmEnvAsync` in `AgentTaskService`): merge the caller's
layers in caller order (agent `LaunchEnvJson`, then task `LaunchEnvOverrideJson` for a task-token
caller), then **filter to `Delegation:LlmEnvInheritance:Names`**. Values are copied raw —
including an unresolved `{{key:NAME}}`, which the launch resolver already resolves over the fully
merged env, so a placeholder inherits correctly. Never log a value; names only (the
`ApiKeyEnvResolver` rule). `ANTIPHON_*` can never appear (the list is name-allowlisted, and
`AgentLaunchEnv.ValidateOverride` already refuses it at every source).

**Settings** (new block on `DelegationSettings`):

```jsonc
"Delegation": {
  "LlmEnvInheritance": {
    "Enabled": true,
    // Opaque passthrough names. X_LLM_* are Mikey.LlmKeyProxy conventions, unknown to this code.
    "Names": [
      "X_LLM_PROJECT", "X_LLM_KEY",
      "ANTHROPIC_BASE_URL", "ANTHROPIC_API_KEY", "ANTHROPIC_CUSTOM_HEADERS",
      "GROK_BASE_URL", "GROK_CLI_CHAT_PROXY_BASE_URL", "GROK_XAI_API_BASE_URL",
      "OPENAI_BASE_URL", "OPENAI_API_KEY"
    ],
    "ProjectMarkerName": "X_LLM_PROJECT",
    "ProxyUrlNames": [ "ANTHROPIC_BASE_URL", "GROK_BASE_URL", "GROK_CLI_CHAT_PROXY_BASE_URL", "GROK_XAI_API_BASE_URL", "OPENAI_BASE_URL" ],
    "ProxyHostMarkers": [ "localhost", "127.0.0.1" ],
    "RequireProjectAtProxy": true
  }
}
```

**Merge position: after `ProjectDefaultEnv`, before `AgentEnv`.** New optional
`AgentLaunchOptions.InheritedEnv` (`AgentLaunchSpec.cs:9-45`), applied in **both** resolvers
(`AgentRegistry.Resolve` after the `ProjectDefaultEnv` loop at `AgentRegistry.cs:133-137`;
`AgentTuiLaunchResolver.ResolveCoreAsync` after `AgentTuiLaunchResolver.cs:359-363`). Rationale,
matching the existing order's own reasoning: a project default is a blanket fact; the caller's
actual routing is more specific than the blanket; but a value somebody wrote about *this
child agent* (`Agent.LaunchEnvJson` — a deliberately configured standing agent), an explicit
`-EnvOverride`, and `ANTIPHON_*` `ExtraEnv` all describe the child more specifically still and
must keep winning. **Explicit override beats inheritance by construction.**

Resulting order: profile → project default → **inherited (new)** → agent's own launch env →
task override → `ExtraEnv` → kind defaults fill gaps → `{{key:}}` resolution.

**Dispatcher plumbing:** `BuildLaunchSpec` and `BuildLaunchSpecAsync`
(`AgentTaskDispatcher.cs:2192-2290`) pass
`InheritedEnv: AgentLaunchEnv.Parse(task.InheritedLaunchEnvJson)`.

**Warm pool.** A reused process cannot change env (the rule that already declines reuse on a
non-empty override, `AgentTaskDispatcher.cs:2551-2561`) — but declining reuse for *every*
inherited-env task would kill the pool for exactly the projects this card serves. Instead, make
the pool row honest about what its process was launched with:

- **Fresh spawn** (`ResolveAgentAsync`, `AgentTaskDispatcher.cs:2435-2453`) and the pinned-pool
  relaunch path stamp `agent.LaunchEnvJson = task.InheritedLaunchEnvJson`, at the same point the
  spawn already stamps `PoolProjectId` (`:1874`) and restamps `Kind` (`:2424`). A pool row is born
  `{}` today and nothing else ever writes it, so this records fact, not policy. (Note the merge
  consequence: the stamp rides the *agent-env* layer on that row's relaunches, which outranks
  inherited — same values, so idempotent.)
- **Reuse check** (both the pinned arm and the pool-shopping arm of `TryReuseWarmAgentAsync`):
  reuse only when the inherit-list projection of `agent.LaunchEnvJson` equals the projection of
  `task.InheritedLaunchEnvJson` (both names and values, Ordinal). Mismatch → `SpawnFresh` with the
  same style of log line as the kind/scope mismatches. `PoolProjectId` equality already gets most
  of the way; this closes the caller-specific remainder (two callers in one project with different
  markers).
- **Standing agents** (`PlaceOnStandingAgentAsync`) are never stamped and never refused: the
  operator configured that agent's env deliberately. When the projections differ, add a Warning
  task event naming the *variable names* that differ (never values).
- **Follow-ups / `-OnAgent`**: no process launch, env frozen — skip both snapshot and gate, same
  as the existing override+follow-up 422 (`AgentTaskService.cs:108-114`) reasoning.

## 4. S2 — The refusal, and its weaker sibling

Both run in `CreateAsync` after the snapshot, over a **preview env** = child project default
(`ApiKeyEnvResolver.GetProjectDefaultEnvAsync(projectId)` — the resolver instance is available via
DI; `AgentTaskService` gains it as an optional dependency in the established pattern) → inherited
snapshot → pinned standing agent's `LaunchEnvJson` (when `request.AgentId` names one) → validated
`launchEnvOverride`.

- **Arm 1 — refuse, 422 `llm_project_required`** (gated on `RequireProjectAtProxy`, default true):
  any `ProxyUrlNames` variable in the preview has a value whose URI host matches a
  `ProxyHostMarkers` entry, AND `ProjectMarkerName` is absent or blank in the preview. A proxied
  child with no project is *never* right — it sits Pending/unbound on the proxy or binds a wrong
  key (CARD-0259's measured `/api/hello` corpses). The 422 detail names the offending variable and
  both escapes: pass `-EnvOverride @{ X_LLM_PROJECT = '…' }`, or seed the project's default env.
  No ignore flag — unlike CARD-0136's quota gate, the fix is one env var, not a judgment call.
- **Arm 2 — warn, never refuse**: `ProjectMarkerName` present in the preview, but the resolved
  `agentKind`'s own routing route is absent — ClaudeCode without `ANTHROPIC_BASE_URL`, Grok
  without `GROK_CLI_CHAT_PROXY_BASE_URL`/`GROK_BASE_URL`, Codex always (env cannot route it). This
  is CARD-0260's measured miss shape (project marker inherited, child still wrapper-managed) — but
  wrapper-managed is the documented default and legitimate on most boards, so it is a Warning
  task event + creation-response `warning` ("child will not route through the key proxy; its
  turns bill the wrapper credentials"), surfaced by `delegate.ps1`'s existing `WARNING:` echo.

**Stated limitation:** the preview cannot see the TUI-profile layer (profile resolution is
dispatch-time and kind-dependent). A profile that itself points at the proxy without a project
slips arm 1. Acceptable: profiles are operator-validated surfaces, and the proxy session will
still show Pending; do not contort create-time validation to chase it.

**Not extended to `AgentControlService.StartAsync` / card spawns** in this card: those launches
have no delegating caller to inherit from, and refusing an operator's explicit start of their own
agent is a different policy question. If wanted later, arm 1 drops into
`StartInteractiveSessionAsync`'s resolved spec as a post-resolution check — noted, not built.

## 5. S3 — `delegate.ps1`'s own role

Server-side inheritance covers every token-bearing caller — but only from the caller's *stored*
layers. The one env the server can never see is the caller's live process env: a human (or an
agent whose wrapper exported routing after launch) running `delegate.ps1` in a shell where
`$env:X_LLM_PROJECT` is set. So: **both sides, different jobs** —

- **New request field** `CreateAgentTaskRequest.InheritedLlmEnv` (dictionary, optional). The
  server intersects it with `LlmEnvInheritance:Names` (unknown names are dropped with a Warning
  event naming them — not a 422; same "bookkeeping must not refuse a launch" rule as `-Scope`),
  refuses `ANTIPHON_*` by the existing validator, and uses it as the snapshot **in preference to**
  the server-side reconstruction (the live process env is the truer fact). It lands in the same
  `InheritedLaunchEnvJson` — same merge position, same pool semantics, and crucially it does
  **not** trip the override-excludes-warm-reuse rule the way folding it into `launchEnvOverride`
  would.
- **`delegate.ps1`**: when `-EnvOverride` did not set them, send `inheritedLlmEnv` from
  `$env:X_LLM_PROJECT` / `$env:X_LLM_KEY` (just those two names, hardcoded ASCII — the URL set
  stays a server-side concern; forwarding a Claude orchestrator's own `ANTHROPIC_*` process env at
  a Grok child from the client is guesswork the server's layer model already does better). New
  switch `-NoInheritEnv` opts out. Document in the antiphon-delegate skill and
  `docs/agent-credentials.md` (§2 merge table gains the row; §6 gains the "false safety" note that
  `X_LLM_PROJECT` without a base URL routes nothing).

## 6. Operator actions (complementary, and sufficient for the live miss on their own)

- **Seed the PredictionMarkets project's `DefaultLaunchEnv`** with `X_LLM_PROJECT=PredictionMarkets`
  plus the proxy base URLs for every kind that board dispatches (`ANTHROPIC_BASE_URL`,
  `GROK_BASE_URL`/`GROK_CLI_CHAT_PROXY_BASE_URL`, dummy `ANTHROPIC_API_KEY` per the proxy's
  convention). This is **configuration on this Antiphon instance** (project settings UI /
  `Project.DefaultLaunchEnvJson`), not a build change; it is in scope for CARD-0260's close-out
  checklist but is an ops step, and it already works end-to-end today through
  `task.ProjectId` → `GetProjectDefaultEnvAsync` → both resolvers → `PoolProjectId`-scoped reuse.
- **Proxy-side `cwdPatterns`** (Mikey.LlmKeyProxy config) including `C:\Antiphon\worktrees\*`:
  different codebase, out of this repo's hands, and explicitly *not sufficient alone* (both cards
  agree; worktree cwds drift).
- **Workspace docs line** (CARD-0259 item 4): the generated orchestrator workspace docs state
  "delegates inherit your LLM project/key; explicit `-EnvOverride` wins" — one sentence in the
  CARD-0251/0247 workspace template, coordinated there.

## 7. CARD-0256 — expected effect, honestly

**Not fixed by this card; its Plan pass should proceed.** The two Grok E-01 workers died Stopped
with zero transcript entries in ~3.5 min. Nothing proves missing `X_LLM_PROJECT` killed them: the
parent's Antiphon-visible env carried **no Grok proxy URL to inherit** (only the marker), so those
children's Grok CLI reached whatever the machine-global wrapper config says, and a proxy refusal
has never been shown to kill the TUI pre-prompt. What this card *does* change for that shape:
after S1+§6, a Grok child carries the project marker (and the proxy URL once the project default
is seeded), so **if** the wrapper routes via the proxy, the unbound-hello failure mode closes; and
arm 1 turns "would sit Pending on the proxy" into a named 422 at create. CARD-0256's actual
deliverables — stop labeling empty Stopped sessions "an operator ended it", fail-fast with a named
reason on zero-transcript death, the AllowedRoots retry gap — are untouched here and still owed.

## 8. Out of scope

- Codex proxy routing (needs profile `-c` args, not env — CARD-0167 territory).
- Cross-provider URL synthesis (rejected, §1).
- Refusal on `StartAsync`/card spawns (§4).
- Any Mikey.LlmKeyProxy change.
- Real API keys in args (already refused by `ApiKeyPlaceholder.EnsureAbsent`).

## 9. Test plan (reconciling both cards' pins into real seams)

All in `Antiphon.Tests` unless noted; run via `dotnet run --project tests/Antiphon.Tests
--property:OutputPath=bin-card0260/ --treenode-filter ...`.

1. **Inheritance end-to-end at the spec level** (extend
   `ApiKeys/LaunchEnvLayersIntegrationTests`): a session-token caller whose agent's
   `LaunchEnvJson` = `{X_LLM_PROJECT: PredictionMarkets, GROK_BASE_URL: http://localhost:10746/v1}`
   creates (a) a Grok Worker and (b) a ClaudeCode Plan task → each task row's
   `InheritedLaunchEnvJson` carries both names; dispatching through `BuildLaunchSpec` **and**
   `BuildLaunchSpecAsync` yields `spec.Env` containing them *before any process starts* — the
   cards' "before the child's first token" pin, expressed at the only seam a unit test can hold.
2. **Explicit override wins**: same parent, child created with
   `launchEnvOverride = {X_LLM_PROJECT: Other}` → `spec.Env["X_LLM_PROJECT"] == "Other"`.
3. **Agent's own env wins over inherited; inherited wins over project default** (order pins in
   both resolvers, mirroring the existing layer tests).
4. **Arm 1 422**: parent env has `ANTHROPIC_BASE_URL=http://localhost:10746` and no project
   marker anywhere in the preview → create refused `llm_project_required`; adding
   `-EnvOverride @{X_LLM_PROJECT=...}` or a project default clears it. Non-local URL → no refusal.
5. **Arm 2 warning**: marker inherited, ClaudeCode child, no `ANTHROPIC_BASE_URL` in preview →
   created with warning; response `warning` names the variable.
6. **Warm pool**: fresh spawn stamps the pool agent's `LaunchEnvJson`; a second task with the same
   snapshot reuses it; a task with a different `X_LLM_PROJECT` declines reuse (SpawnFresh) and
   restamps; a task-with-snapshot never reuses a `{}`-stamped warm agent.
7. **Follow-up**: `-OnAgent` task computes no snapshot and passes no gate.
8. **`delegate.ps1`** (extend `Application/DelegateScriptKindTests`, which already asserts body
   shape): process env `X_LLM_PROJECT` set → body carries `inheritedLlmEnv`; `-NoInheritEnv`
   omits it; `-EnvOverride` setting the same name leaves `inheritedLlmEnv` without it.
9. **Server-side field validation**: `inheritedLlmEnv` with a name outside the list → dropped +
   Warning event; `ANTIPHON_*` → 422.

## 10. Files to change

| File | Change |
|---|---|
| `server/Domain/Entities/AgentTask.cs` + migration | `InheritedLaunchEnvJson` (default `"{}"`) |
| `server/Application/Settings/DelegationSettings.cs` | `LlmEnvInheritance` block (§3) |
| `server/Application/Dtos/AgentTaskDtos.cs` | `CreateAgentTaskRequest.InheritedLlmEnv` |
| `server/Application/Dtos/AgentLaunchSpec.cs` | `AgentLaunchOptions.InheritedEnv` |
| `server/Application/Services/AgentTaskService.cs` | `ComputeInheritedLlmEnvAsync`, snapshot in `CreateAsync`, arm 1 + arm 2 |
| `server/Application/Services/AgentRegistry.cs` | merge slot after project default |
| `server/Application/Services/AgentTuiLaunchResolver.cs` | same slot in `ResolveCoreAsync` |
| `server/Application/Services/AgentTaskDispatcher.cs` | pass `InheritedEnv` (both build paths); pool stamp in `ResolveAgentAsync`; reuse compare in `TryReuseWarmAgentAsync`; standing-agent Warning |
| `scripts/delegate.ps1` | auto-forward `X_LLM_PROJECT`/`X_LLM_KEY`, `-NoInheritEnv` |
| `docs/agent-credentials.md` | merge table row, §6 note |
| skill `antiphon-delegate` / orchestrator docs | one paragraph on inheritance + override-wins |
