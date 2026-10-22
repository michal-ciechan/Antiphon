# CARD-0167: API key + LLM proxy as a first-class per-agent-kind setting

Plan date: 2026-10-22. Target: **master only**. Stage: Plan, with the verification design folded in (the brief asked for a closed `### Checkpoints` table). Source inspected at `294fa3d1a81ff97abb285af3180d93ee4d4b25f6` (this branch tip, equal to the task base). No source was edited. No build or test was run. The verification harness this plan leans on is CARD-0168's `src/Antiphon.FakeLlmApi` and `RealCliStubEnv`.

Secrets custody is binding throughout: nothing in this plan stores, prints, logs or previews a key **value**. The setting holds a URL and a key **name**. The value stays where CARD-0106 already keeps it (the Data-Protection-encrypted `ApiKey` store, write-only over HTTP), reaches a child process only as an environment variable, and is never a launch argument.

## Outcome

Add one typed setting, a **provider endpoint**, that an operator sets per agent kind on a project (the trusted proxy this project's Claude, Grok or Codex agents always use) and optionally overrides on one agent. The server projects it into the launch environment and, for Codex, into the five `-c` launch arguments, through one resolver arm per kind in a new `ProviderEndpointProjection`. The key is always referenced as `{{key:NAME}}`, so the existing placeholder resolver, scoping, tripwire and failure messages apply unchanged. With no setting, every launch is byte-identical to today.

## Ground truth

| Card premise | What `294fa3d1a` does | Consequence |
|---|---|---|
| Grok base URL is unconfirmed (`GROK_XAI_API_BASE_URL`). | CARD-0168 corrected it: chat redirect is `GROK_CLI_CHAT_PROXY_BASE_URL`; `GROK_XAI_API_BASE_URL` redirects only `GET /api-key`. `RealCliStubEnv.ForGrok` sets both and throws without the chat var (`src/Antiphon.FakeLlmApi/RealCliStubEnv.cs`). `docs/agent-kinds.md` §5 documents both. | The Grok arm projects **both** variables, chat var mandatory. Card scope item 1 is closed by CARD-0168; this plan does not re-probe. |
| Codex needs three `-c` args. | Five: `model_providers.<id>.{name,base_url,env_key,wire_api}` + `model_provider=<id>`; base URL must end in `/v1` (`RealCliStubEnv.ForCodex`, `docs/agent-kinds.md` §6). `OPENAI_BASE_URL` is inert for Codex (card revision 2 live test) though `DelegationSettings.LlmEnvInheritance.Names` still lists it as opaque passthrough. | The Codex arm emits five args plus `OPENAI_API_KEY`. Passthrough names are untouched. |
| `AgentLaunchSpec.Args` exists but no stored per-agent or per-project args surface exists. | Confirmed. Codex `-c` args today come only from code: `AgentTaskDispatcher` (~line 7030) and `AgentSessionLaunchComposer` (~line 83) append `CodexLaunchArgs.*` into `ExtraArgs`. `AgentLaunchOptions.ExtraArgs` are caller-literal and not `$env:` expanded. | The projection is a resolver step, not a stored arg list: no free-text args are ever persisted. |
| The generic env layers cover this in theory. | `AgentTuiLaunchResolver.ResolveCoreAsync` (profile path) and `AgentRegistry.Resolve` (legacy path, lines 134-156) both merge: profile env/secrets → project default → inherited → agent `LaunchEnvJson` → launch override → `ExtraEnv`; then `$env:` arg expansion, kind defaults, `{{key:}}` resolution last. `AgentLaunchResolution.AttachProjectContextAsync` hoists one project identity for key scope and project default env. | The new layers attach in the same funnel and run in both resolvers. |
| No redaction concept for a new setting. | The setting carries no value, so there is nothing to redact at rest. Existing value guards: `ApiKeyEnvResolver` logs names only; `ApiKeyPlaceholder.EnsureAbsent` refuses `{{key:` in any argument; `DiagnosticsRedactor` masks `{{key:…}}`, `sk-…`, `Bearer …`; `HerdrLaunchScript` writes `<redacted>` env on disposal; `PhoneHomeLaunchPolicy.Project` refuses credential names on runner-bound Claude (`ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, …) and Codex (`OPENAI_API_KEY`, `CODEX_API_KEY`, `CODEX_ACCESS_TOKEN`) with 409 `phone_home_env_refused`, value never in the message. | New code adds host-only logging and a names-only preview; it must not add a value path. |
| Warm-pool reuse is unaffected. | `AgentTaskDispatcher.InheritedEnvMatchesPool` (line ~8196) compares the agent's `LaunchEnvJson` to the task's inherited snapshot on the inherit-list names (`ANTHROPIC_BASE_URL`, `GROK_CLI_CHAT_PROXY_BASE_URL`, `OPENAI_API_KEY`, …). A stored endpoint outside `LaunchEnvJson` would be invisible to that match. | S3 feeds the agent-side projection into that comparison. |
| The deferred B-agent Codex canary waits on this card. | `CodexRealCliStubProxyCanaryTests.B_agent_path_deferred_until_CARD_0167` throws `SkipTestException`; the class is `[Explicit]`, gated on `ANTIPHON_REAL_CLI_STUB_TESTS=1`, Windows, real binaries. | Its activation is an operator-run acceptance on the desktop lane, never a checkpoint row (a skip is `row_failed`). |

Runner reads (2026-10-22): `GET /api/runner-defaults` revision 2, `globalRunnerId` `server2`, no kind defaults. `GET /api/session-runners`: `desktop` (windows, eligible), `server2` (linux, eligible, codex 0.160.0), `server2-temp` (stale, draining). Every slice here is pure C#/TypeScript with no native pty or browser need, so omit `-Runner` and `-Platform`. Only the operator-run B-agent canary needs the Windows desktop.

### Per-kind asymmetry (the table the card asked for)

| Kind | API key | Base URL / proxy | Config file or home | Launch argument | Wire |
|---|---|---|---|---|---|
| ClaudeCode | env `ANTHROPIC_API_KEY` | env `ANTHROPIC_BASE_URL` | `CLAUDE_CONFIG_DIR` (OAuth store; runner projects `/state/claude`) | none | `x-api-key` header |
| Grok | env `GROK_CODE_XAI_API_KEY` | env `GROK_CLI_CHAT_PROXY_BASE_URL` (chat turns) **and** `GROK_XAI_API_BASE_URL` (`/api-key` lookup only) | `GROK_HOME/auth.json` (OAuth; never read, never written) | none | `Authorization: Bearer` on `/api-key`; OAuth JWT on chat when logged in |
| Codex | env named by `env_key`; this plan always uses `OPENAI_API_KEY` | **no env var**; five `-c` args naming `model_providers.<id>.base_url` ending `/v1` | `CODEX_HOME/auth.json`, `config.toml` (never read) | `-c model_providers.<id>.name`, `.base_url`, `.env_key`, `.wire_api=responses`, `-c model_provider=<id>` | `Authorization: Bearer` from `OPENAI_API_KEY` |
| gkp wrapper (Grok via `pwsh` profile) | `XAI_API_KEY` or `GROK_CODE_XAI_API_KEY` (dummy) + `X_LLM_PROJECT` | `GROK_BASE_URL` (+ chat proxy var) | n/a | profile args, `$env:` expanded | herdr lane refuses without them (`herdr_gkp_env_missing`) |

The gkp row is a wrapper program with its own contract. The projection targets the three real CLIs; a wrapper-managed profile keeps using the generic env layers (D-9).

## Design

### The setting

```
ProviderEndpoint { baseUrl: string, apiKeyName: string? }
```

- `baseUrl`: absolute `http`/`https`, no userinfo, no query, no fragment, at most 2 000 characters, trailing slash trimmed. A loopback host is allowed (local key proxies are the norm).
- `apiKeyName`: an `ApiKeyNaming`-valid name (`[A-Za-z0-9_.-]{1,128}`, Ordinal) of a key in the existing `ApiKey` store, global or project-scoped; the resolver's project-wins-over-global rule applies. Optional for Claude and Grok (a key-injecting proxy such as gkp needs only the URL), **required for Codex** because `env_key` must name a set variable. Neither field may contain `{{key:`.
- Project: `Project.ProviderEndpointsJson` (default `{}`), a dictionary keyed by `AgentKind` name → `ProviderEndpoint`. Only `ClaudeCode`, `Grok`, `Codex` are accepted (422 `provider_endpoint_unsupported_kind` otherwise).
- Agent: `Agent.ProviderEndpointMode` (`Inherit` default, `Endpoint`, `Direct`) and `Agent.ProviderEndpointJson` (nullable; required when mode is `Endpoint`). `Direct` exempts one agent from the project endpoint; the generic env box cannot express "unset", which is why the mode exists.
- No key value, no header value, no args text is ever stored. Rotation is a key-store write; the setting does not change.

### Projection (`ProviderEndpointProjection.For(kind, endpoint)` → `(Env, Args)`)

| Kind | Env | Args |
|---|---|---|
| ClaudeCode | `ANTHROPIC_BASE_URL=<url>`; `ANTHROPIC_API_KEY={{key:<name>}}` when a name is set | none |
| Grok | `GROK_CLI_CHAT_PROXY_BASE_URL=<url>`; `GROK_XAI_API_BASE_URL=<url>`; `GROK_CODE_XAI_API_KEY={{key:<name>}}` when a name is set | none |
| Codex | `OPENAI_API_KEY={{key:<name>}}` | `-c model_providers.antiphon.name="Antiphon endpoint"`, `-c model_providers.antiphon.base_url="<url>/v1"` (`/v1` appended when absent), `-c model_providers.antiphon.env_key="OPENAI_API_KEY"`, `-c model_providers.antiphon.wire_api="responses"`, `-c model_provider=antiphon` |
| OpenCode, Raw | throws `ConflictException("provider_endpoint_unsupported_kind")` | |

`Describe(kind)` returns the same shape with `<url>` and `<name>` as literal placeholders, for the UI and docs. The projection never takes a value and cannot emit one; `ApiKeyPlaceholder.EnsureAbsent` still runs over every argument after projection, which is what keeps the key out of argv by construction.

### Merge order (later wins)

| # | Layer | Change |
|---|---|---|
| 1 | Profile revision non-secret env | unchanged |
| 2 | Profile managed secrets | unchanged |
| 3 | Project default env | unchanged |
| **3b** | **Project provider endpoint for the resolved kind** | new |
| 4 | Inherited caller env | unchanged |
| 5 | Agent `LaunchEnvJson` | unchanged |
| **5b** | **Agent provider endpoint** (`Endpoint` mode); `Direct` mode removes nothing but suppresses 3b | new |
| 6 | Launch-time override | unchanged |
| 7 | `ExtraEnv` (`ANTIPHON_*`) | unchanged |

Rationale: the typed project setting outranks the generic project box of the same scope (a stale hand-typed `ANTHROPIC_BASE_URL` in the box loses to the thing the operator set on purpose), the caller's inherited routing and the agent's own hand-typed env still outrank the project's blanket, the agent's typed setting outranks that agent's own box, and the launch-time override and Antiphon's identity block outrank everything. Codex projection args are appended after `ExtraArgs` and count against `CommandLineBudgetChars` (CARD-0497 refusal unchanged). Both 3b and 5b are applied in `AgentTuiLaunchResolver.ResolveCoreAsync` and `AgentRegistry.Resolve`, after the generic layers and before `$env:` expansion, kind defaults and `{{key:}}` resolution.

### Redaction in every log and diagnostic

- At rest: URL and name only. The DTOs echo exactly what was written; there is no value field to omit.
- Resolver log line, Information: `"{Subject}: provider endpoint projected kind={Kind} host={Host} key={KeyName}"`. Host only, never the full URL (a path could carry a tenant token), never a value.
- Failure reasons: a missing key keeps `ApiKeyEnvResolver`'s existing `api_key_not_found` message (name and scopes). The unsupported-kind refusal names the kind. The runner refusal keeps `phone_home_env_refused` naming the variable.
- Preview and `Describe` output carry `{{key:NAME}}`, which `DiagnosticsRedactor` already masks to `{{key:***}}`.
- Witness (sentinel values only): a fake key store returns `sentinel-0167-never-printed`; a capturing logger, the thrown exception messages, the DTO JSON and the Codex argv are asserted never to contain it, while the child env does.

### Operator operations

| Operation | How |
|---|---|
| Set | `PUT /api/api-keys/{name}` (or the project route) with the key, once; then `PATCH /api/projects/{id}` with `providerEndpoints: { "ClaudeCode": { "baseUrl": "...", "apiKeyName": "..." } }`, or the project settings UI. Per agent: `PATCH /api/agents/{id}` with `providerEndpointMode` and `providerEndpoint`. |
| Rotate | Write the same key name again. No launch setting changes; the next launch resolves the new value. A warm-pool process launched before rotation keeps the old value until it is replaced (same as CARD-0106 today). |
| Clear | `providerEndpoints: {}` on the project or `providerEndpointMode: Inherit` with `providerEndpoint: null` on the agent. Deleting the key alone makes the next launch fail loud by name, which is the intended signal. |
| Inspect | `GET /api/projects/{id}` / `GET /api/agents/{id}` show URL and name. `GET /api/agent-tui/runner-types` shows per kind which variables and arguments a setting projects. |

### Failure modes

| Failure | Where it surfaces | Behaviour |
|---|---|---|
| Key name not found in global or project scope | launch resolution | existing `api_key_not_found`, names key and scopes, no value; launch refused. Save accepts the name and returns a `warnings` entry naming it (keys may be created later). |
| Setting on an unsupported kind | save: 422 `provider_endpoint_unsupported_kind`; resolve: 409 same code | the agent's kind is only known at resolve time on the agent path, so both guards exist. |
| Codex without a key name | save: 422 `provider_endpoint_key_required` | Codex's `env_key` must name a set variable. |
| URL with userinfo, query, fragment, relative form or `{{key:` | save: 422 naming the field | never stored. |
| Proxy unreachable or wrong wire shape | the CLI, during the session | Antiphon does not probe the URL at save or launch (D-7): a probe from the server host says nothing about the runner host, and the CLIs own their HTTP. Claude prints a connection error on its first turn; Codex fails closed on `/v1/models` (pinned by `Scripted_400_on_models_fails_closed_and_reconnect_loop_is_bounded`); Grok has no first-token timeout (CARD-0353) and a hung proxy reads as a hung provider, caught by the existing boot-turn deadline. |
| Runner-bound Claude or Codex with a key name | dispatch | 409 `phone_home_env_refused` as today (CARD-0628: no metered key on server2). The projection uses the canonical names on purpose so this policy cannot be dodged by naming the key variable differently. Grok has no such refusal. |
| Codex command line over budget | existing CARD-0497 guard | 409 `codex_command_line_too_long`; the ten extra argv elements are counted. |
| Non-empty per-task override | existing rule | excluded from warm-pool reuse; unchanged. |

### What must not change

- No setting → every launch's env and args are byte-identical to today (pinned by a resolver witness that compares against a captured baseline).
- Subscription and OAuth sign-in paths: `GROK_AUTH_PATH`, `auth.json`, `CLAUDE_CONFIG_DIR`, `CODEX_HOME` are never set, read or written by the projection. The registry `grok` definition stays key-free.
- CARD-0628 runner refusals, the `ANTIPHON_*` 422, `{{key:}}` syntax and scoping, the argv tripwire, and `LlmEnvInheritance.Names` are unchanged.
- `RealCliStubEnv` stays the only sanctioned **test** builder; `ProviderEndpointProjection` is the production counterpart and a cross-check test keeps their variable names and Codex `-c` key set aligned.
- The herdr gkp lane check and `$env:` profile-arg expansion are unchanged.

## Decisions

- **D-1 — The key is a reference to the existing `ApiKey` store, never a stored value.** `{{key:NAME}}` is the mechanism CARD-0106 shipped: Data-Protection ciphertext in the database, key ring on disk, write-only HTTP, project-over-global scope, names-only failures. Rejected: a second ciphertext column on the setting (a second custody path to back up and rotate); a profile managed secret (profile-scoped, not per project); a host env file read at launch (the runner, not the server, launches, and nothing in the current layers reads host files).
- **D-2 — Typed per-kind dictionary on the project, mode plus one endpoint on the agent.** The project is where "the trusted proxy this project always uses" belongs (card scope item 3); the operator asked for a first-class citizen, not a nicer env box. Rejected: a preset that writes into `DefaultLaunchEnvJson` (visible as raw names, cannot carry Codex args, cannot be cleared per agent).
- **D-3 — A pure projection class, one arm per kind, `RealCliStubEnv`-shaped.** Same spirit as `ModelLevelAliases.ForCodex` (card scope item 2). Codex provider id is `antiphon`, distinct from the stub's `stub`, so a canary and a production redirect never share a provider table entry.
- **D-4 — Merge ranks 3b and 5b as above**, in both resolvers, before expansion and placeholder resolution.
- **D-5 — The Codex key variable is always `OPENAI_API_KEY`.** `env_key` could name anything; using the canonical name keeps `PhoneHomeLaunchPolicy`'s refusal effective and the inherit-list projection meaningful.
- **D-6 — Both Grok base-URL variables, chat var mandatory**, matching CARD-0168 decision 4. `GROK_BASE_URL` (gkp) is not projected; it is a wrapper convention.
- **D-7 — No reachability probe.** Stated above. A "test endpoint" action is a separate card if wanted.
- **D-8 — Save-time key existence is a warning, not a refusal.** A key created after the setting is a normal order of operations; the launch is the loud gate.
- **D-9 — Wrapper-managed profiles are out of the projection's scope.** A profile whose executable is a wrapper (gkp) keeps its generic env; the projection applies by the resolved kind regardless, so an operator who sets a Grok endpoint on a project with a gkp profile gets both the projected vars and the wrapper's own. Documented, not guarded.
- **D-10 — Operator decision: may proxy credentials be stored in the database?** Default here: yes, as today under D-1 (encrypted `ApiKey` rows plus the on-disk key ring). If the operator says no, S1, S3's projection and S4/S5 stand; only the reference resolution changes (a host-side store behind `ApiKeyEnvResolver`), which would be a new card. S2 does not store a value either way.
- **D-11 — Operator decision: may a runner-bound Claude or Codex agent use an endpoint with a key?** Default here: no, unchanged from CARD-0628. A yes is a change to `PhoneHomeLaunchPolicy`, outside this card.
- **D-12 — Operator decision: UI prominence.** Default here: a dedicated "LLM endpoint" group per kind on the project settings page and one group in the agent modal, with the projected variable names shown read-only from `GET /api/agent-tui/runner-types`.
- **D-13 — Master only.** No rc backport; the setting is additive and dormant until used.
- **D-14 — Red first, as unlisted build-slot runs.** Each slice commits its tests against a skeleton (or against the unmodified line where the witness is an inversion), runs the exact method filter through `scripts/build-slot.ps1`, reports `executed=N failed=N` with the assertion names, then commits the implementation and runs the table row. The table lists green runs only; the reds are reported with that reason.

## Slices

Each slice is one Code task of 30 to 60 minutes. Files are named; tests are named. Any OS.

| Slice | Files | Change | Witness |
|---|---|---|---|
| S1 — value object and projection | `server/Application/Services/ProviderEndpoint.cs` (new: record, `ProviderEndpointMode`, `Validate(kind?, field)`, `Parse`/`Serialize`, `ParseMap`/`SerializeMap`), `server/Application/Services/ProviderEndpointProjection.cs` (new: `For`, `Describe`, `CodexProviderId`), `tests/Antiphon.Tests/ApiKeys/ProviderEndpointProjectionTests.cs` (new, `[Category("Unit")]`) | Pure code, no DB, no DI. | Red: commit the tests with `For` returning empty env/args and `Validate` returning its input; seven methods fail on assertions. Green: implement. Methods: `C0167_Claude_projects_base_url_and_key_placeholder`, `C0167_Grok_projects_both_base_urls_and_chat_var_is_present`, `C0167_Codex_projects_openai_key_and_five_args_ending_v1`, `C0167_Codex_requires_a_key_name`, `C0167_Unsupported_kind_refuses_by_code` (OpenCode and Raw via `[Arguments]`, counts 2), `C0167_Validate_refuses_userinfo_query_fragment_relative_and_placeholder` (five `[Arguments]`, counts 5), `C0167_Projection_names_match_RealCliStubEnv` (env names minus `CLAUDE_CONFIG_DIR`/`CODEX_HOME`/kind defaults; Codex `-c` key set with the provider id substituted; `ApiKeyPlaceholder.EnsureAbsent` passes over every arg). Roster: 12. |
| S2 — persistence and API | `server/Domain/Entities/Project.cs`, `server/Domain/Entities/Agent.cs`, one migration `AddProviderEndpoints` via `dotnet tool restore` then `dotnet ef migrations add AddProviderEndpoints --project server` (both columns, `Agent.ProviderEndpointMode` int default 0), `server/Application/Dtos/ProjectDto.cs`, `UpdateProjectRequest.cs`, `CreateProjectRequest.cs`, `server/Application/Dtos/AgentDtos.cs` (detail, create, update), `server/Application/Services/ProjectService.cs`, `server/Application/Services/AgentService.cs`, `tests/Antiphon.Tests/ApiKeys/ProviderEndpointApiTests.cs` (new, `[Category("Integration")]`, `ApiKeyApiTests` harness) | `providerEndpoints` on the project (null unchanged, `{}` clears); `providerEndpointMode` + `providerEndpoint` on the agent. Validation through `ProviderEndpoint.Validate`; save warning for an unknown key name. | Red: commit the tests before the DTO/service change; they fail on 404/400 shape or missing fields. Methods: `C0167_project_endpoints_round_trip_per_kind_and_clear_with_empty_map`, `C0167_project_endpoint_422_on_unsupported_kind_and_bad_url`, `C0167_project_endpoint_warns_on_unknown_key_name_and_accepts`, `C0167_agent_endpoint_mode_round_trips_and_endpoint_mode_requires_an_endpoint`, `C0167_codex_project_endpoint_without_key_name_is_422`. Roster: 5. |
| S3 — resolver wiring | `server/Application/Dtos/AgentLaunchSpec.cs` (`AgentLaunchOptions.ProjectProviderEndpoints`, `AgentProviderEndpoint`, `AgentProviderEndpointMode`), `server/Application/Services/AgentTuiLaunchResolver.cs` (attach in `ResolveForAgentAsync`; apply 3b/5b in `ResolveCoreAsync`; the host-only log line), `server/Application/Services/AgentRegistry.cs` (apply 3b/5b), `server/Application/Services/ApiKeyEnvResolver.cs` (`GetProjectProviderEndpointsAsync` beside `GetProjectDefaultEnvAsync`), `server/Application/Services/AgentTaskDispatcher.cs` (`InheritedEnvMatchesPool` compares the agent env **plus** its projection), `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs`, `tests/Antiphon.Tests/ApiKeys/LaunchEnvLayersIntegrationTests.cs`, `tests/Antiphon.Tests/ApiKeys/AgentLaunchEnvTests.cs`, `tests/Antiphon.Tests/Application/PhoneHomeTaskRoutingTests.cs` | The launch path. | Red: commit the tests first; they fail because the projected names are absent. Resolver methods: `C0167_no_endpoint_leaves_env_and_args_byte_identical`, `C0167_project_endpoint_outranks_project_default_env_on_the_same_name`, `C0167_agent_launch_env_outranks_project_endpoint`, `C0167_agent_endpoint_outranks_agent_launch_env_and_override_outranks_all`, `C0167_direct_mode_suppresses_the_project_endpoint`, `C0167_codex_endpoint_appends_five_args_after_extra_args_and_tripwire_passes`, `C0167_sentinel_key_value_reaches_env_and_nothing_else` (fake store, capturing logger, exception text, argv), `C0167_unsupported_kind_refuses_at_resolve`. Layers (legacy registry path): `C0167_legacy_registry_applies_project_and_agent_endpoints_in_rank_order`. Runner policy (`PhoneHomeTaskRoutingTests`, beside `Runner_bound_claude_launch_refuses_anthropic_credential_names`, passing a projected spec to `PhoneHomeLaunchPolicy.Project`): `C0167_runner_bound_claude_endpoint_projection_is_phone_home_env_refused`. Pool: `C0167_pool_match_includes_the_agent_projection` in `AgentLaunchEnvTests`. Roster: 11 new. |
| S4 — catalogue descriptor | `server/Application/Dtos/AgentTuiDtos.cs` (`AgentTuiRunnerTypeDto.ProviderEndpoint`: `Supported`, `KeyRequired`, `EnvironmentNames`, `ArgumentTemplate`), `server/Application/Services/AgentTuiRunnerCatalog.cs` (from `ProviderEndpointProjection.Describe`), `tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs` | `GET /api/agent-tui/runner-types` tells the UI what a setting projects, names only. | Red: commit the test first. Method: `C0167_runner_types_describe_provider_endpoint_projection_per_kind` (asserts Claude/Grok/Codex names and `KeyRequired`, OpenCode/Raw `Supported=false`, and that no response text contains `{{key:` resolved or a value). Roster: 1. |
| S5 — client UI | `client/src/api/projects.ts`, `client/src/api/agents.ts`, `client/src/api/agentTui.ts` (descriptor type), `client/src/features/settings/ProviderEndpointFields.tsx` (new, shared group: base URL input, key-name select from `GET /api/api-keys` names, per-kind projected names read-only from the descriptor; agent variant adds the mode radio), `client/src/features/settings/ProjectConfig.tsx`, `client/src/features/agents/AgentSettingsModal.tsx`, `client/src/features/settings/ProviderEndpointFields.test.tsx` (new) | D-12. Never renders a key value: the select lists names from the existing list route, which has no values. | Red: commit the test first against the unmodified components. Cases: renders stored endpoints per kind and submits the parsed map; clears with the empty map; agent mode `Direct` hides the fields and submits `providerEndpoint: null`; Codex shows the key-required hint and blocks submit without a name; projected names come from the descriptor. 5 `it` cases. |
| S6 — docs and canary unblock | `docs/agent-credentials.md` (§1 store table, §2 merge ranks 3b/5b, operator set/rotate/clear), `docs/agent-kinds.md` (§7 production redirect is now the setting; `RealCliStubEnv` stays the test builder), `docs/ai-agent-tui-configuration.md` (UI, API summary), `docs/antiphon-api.md` (fields), `tests/Antiphon.Tests/Application/ProviderEndpointDocumentationTests.cs` (new, `[Category("Unit")]`, the `*DocumentationTests` pattern: `C0167_credentials_doc_names_the_store_row_and_ranks_3b_and_5b`, `C0167_agent_kinds_doc_points_the_production_redirect_at_the_setting`, `C0167_docs_list_exactly_the_names_Describe_projects` comparing `ProviderEndpointProjection.Describe` to the documented variable table), `tests/Antiphon.Tests/Agents/CodexRealCliStubProxyCanaryTests.cs` (replace `B_agent_path_deferred_until_CARD_0167` with `B_agent_launch_projects_project_endpoint_and_hits_stub`: agent-based launch with a project Codex endpoint pointed at the stub, synthetic key through a test key store, dual-hit oracle; stays `[Explicit]` and gated) | Docs plus the one deferred cell. | Red: commit the three documentation pins before the doc edits; they fail on the missing text. Green: edit the docs. The canary is operator-run on the desktop lane (`ANTIPHON_REAL_CLI_STUB_TESTS=1`, Windows, real `codex`), reported outside the table; CP-6 proves it compiles and that the un-opted gate still skips nothing in the row. |

Order: S1 and S2 may run in parallel (different files; S2 does not reference S1 types until S3). S3 after S1 and S2. S4 after S1. S5 after S2 and S4. S6 after S3. Do not edit `RealCliStubEnv.cs`, `PhoneHomeLaunchPolicy.cs` or `DelegationSettings.cs` in any slice.

## Verification design

Red witnesses are unlisted build-slot runs by D-14: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0167-red-<slice> -- dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0167red/ -- --treenode-filter "<the row's filter>"`, reported with `executed=N failed=N` and the assertion names, on the committed test-first SHA. The table below is the green manifest. Delete every `bin-c0167*` directory after the last row.

Roster notes: CP-1 counts 12 TUnit results from S1 (two `[Arguments]` methods expand to 2 and 5) plus the three existing `RealCliStubEnv` self-tests. CP-3 runs three whole unit classes so the merge-order neighbours stay green: `AgentTuiLaunchResolverTests` 14 + 8 new, `AgentLaunchEnvTests` 22 + 1 new, `PhoneHomeTaskRoutingTests` 9 + 1 new, floor 55; CP-4 reuses that build for the Integration class `LaunchEnvLayersIntegrationTests` 41 + 1 new, floor 42 (split from one row because its derived timeout exceeded the 45 minute advisory). CP-7 adds a documentation pin class so the docs slice has a red witness of its own (the pins fail until the docs name the store row, the ranks and the projected variables). CP-6 is Vitest through the wrapper (typecheck, lint, then the one file); its verdict is the `CLIENT TESTS EXIT CODE` line.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0167a/` | projection | `/*/*/(ProviderEndpointProjectionTests*)\|(FakeLlmApiSelfTests*)/(C0167_*)\|(ForClaude_sets*)\|(ForGrok_requires*)\|(ForCodex_emits*)` | D-1, D-3, D-5, D-6 | 15 passed, 0 failed, 0 skipped | 15 | 12 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c0167b/` | persistence | `/*/*/(ProviderEndpointApiTests*)\|(ApiKeyApiTests*)/*` | D-2, D-8, D-10 | 11 passed, 0 failed, 0 skipped | 11 | 14 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c0167c/` | resolver-unit | `/*/*/(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)\|(PhoneHomeTaskRoutingTests*)/*` | D-4, D-5, D-11, redaction witness, byte-identical witness, pool witness | all listed, 0 failed, 0 skipped | 55 | 12 |
| CP-4 | S3 | CP-3 | resolver-layers | `/*/*/LaunchEnvLayersIntegrationTests/*` | D-4 on the legacy registry path, rank order across both resolvers | all listed, 0 failed, 0 skipped | 42 | 10 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c0167d/` | runner-types | `/*/*/AgentTuiApiTests/(C0167_*)\|(Runner_catalogue_and_profile_crud*)` | D-12 descriptor | 2 passed, 0 failed, 0 skipped | 2 | 10 |
| CP-6 | S5 | n/a | client-endpoint-fields | `pwsh -File scripts/test-client.ps1 ProviderEndpointFields.test` | D-12 UI | `CLIENT TESTS EXIT CODE: 0`, 5 tests passed, typecheck and lint 0 errors | n/a | 6 |
| CP-7 | S6 | `tests/Antiphon.Tests -> bin-c0167f/` | docs-pins | `/*/*/(ProviderEndpointDocumentationTests*)\|(FakeLlmApiSelfTests*)/(C0167_*)\|(ForClaude_sets*)\|(ForGrok_requires*)\|(ForCodex_emits*)` | docs pins; the rewritten canary compiles (same assembly; the `[Explicit]` class is not selected because a skip is `row_failed`) | 6 passed, 0 failed, 0 skipped | 6 | 10 |

### Cost

Table floor 74 minutes. Red runs: one per slice at about 6 minutes each (36), unlisted by D-14. Authoring: S1 45, S2 60, S3 60, S4 30, S5 60, S6 45 minutes. Six Code tasks; `-ExpectAbout` per task is its authoring time plus its row plus its red run.

### Operator acceptance outside the table

1. On the desktop lane with `ANTIPHON_REAL_CLI_STUB_TESTS=1`: run `CodexRealCliStubProxyCanaryTests` (`--treenode-filter "/*/*/CodexRealCliStubProxyCanaryTests/B_agent_launch_projects_project_endpoint_and_hits_stub"`). The dual-hit oracle (nonce on `/v1/responses`, synthetic bearer on the credential path) is the proof that the agent path now carries Codex endpoint injection. The synthetic key is `stub-codex-<guid>`; nothing real transits.
2. Set a Claude endpoint on a project whose key proxy is already running, launch one agent, confirm its first `UserPrompt` transcript row, then clear the setting and confirm the next launch's session shows no `ANTHROPIC_BASE_URL` (names only, through the launch read route or the resolver log line).

## Shipment

Commit and push each slice to `feat/card-task-0f389926` with `git push origin HEAD:refs/heads/feat/card-task-0f389926` (a Code dispatch gets its own branch; same rule). Fetch `origin/master` and run `git merge-tree --write-tree origin/master HEAD` before reporting landable. Land master only. The setting is dormant until an operator writes one, so no restart ordering applies beyond the usual post-land activation check.
