# CARD-0345 — expand `$env:NAME` in TUI profile args at launch

**Date:** 2026-09-03 · **Card:** CARD-0345 (`1f234d1f-0a5d-4921-948c-029784050880`, GitHub #33) ·
**Status:** plan · **Verified against:** `332098ca` (`origin/master` at plan time). Line numbers
below were re-read on that commit.

**Go straight to Code.** The card already names the method, the slot, the token grammar, the
ExtraArgs exclusion, and the pin test. There is no design fork. One slice.

This is independent of CARD-0341 / GitHub #32 (herdr script env application). Land as its own PR.

A prototype of `ExpandDollarEnvArg` was on a checkout and **reverted** so this lands from
`origin/master`. Do not copy stash or uncommitted bytes; write from this plan.

---

## Why CARD-0341 did not close this

CARD-0341's plan said the pty-host lane already worked because `gkp.ps1` expands the hint from
process env, so `$env:` resolution belonged only where herdr single-quotes arguments. That
assumption is false:

- **PtyHost** launches with `CreateProcess`. It does not expand `$env:`. The child receives the
  literal argv token `$env:X_LLM_PROJECT`. `gkp.ps1` / `cxp.ps1` / `clproxy.ps1` then call
  `Expand-LlmProxyProjectHint` on that string, which is not a project name.
- **Herdr** (CARD-0341, shipped) expands whole-argument `$env:` tokens in the typed launch script
  before quoting. That repairs the herdr lane only.

`X_LLM_PROJECT` **is** already on the merged launch env (agent `launchEnv` / project
`DefaultLaunchEnv`). The child never sees `--project PredictionMarkets` on PtyHost until the
**server** rewrites the profile argv.

Both expansions can coexist. If this card runs first, herdr's expander is a no-op on already-
resolved values. ExtraArgs that still contain `$env:` remain a herdr-only concern (out of scope
here). Do not extract a shared type across server and session-runner.

---

## What the code does today

`AgentTuiLaunchResolver.ResolveCoreAsync`
(`server/Application/Services/AgentTuiLaunchResolver.cs`) — this is what
`ResolveForAgentAsync` calls; `ResolveDefaultAsync` goes through the same method.

1. Deserialize `revision.ArgumentsJson` (`:307`).
2. Merge env, later wins (`:352-385`): profile non-secret + managed secrets → project default →
   inherited → agent `LaunchEnvJson` → launch-time override → `ExtraEnv`.
3. `var args = new List<string>(arguments);` then `args.AddRange(options.ExtraArgs)` (`:387-389`).
4. `ApplyModelArgument` (`:391`).
5. Kind defaults, then `{{key:NAME}}` resolution over the fully-merged env (`:393-441`).
   `ApiKeyPlaceholder.EnsureAbsent` refuses any argument that still carries `{{key:`.

There is no `$env:` rewrite on this path. The legacy `AgentRegistry.Resolve` path is out of scope
(wrapper profiles `grok-gkp-project` / `claude-clproxy-project` / `codex-cxp-project` are managed
TUI profiles and go through `ResolveCoreAsync`).

Matcher already shipped for the herdr lane:
`HerdrLaunchScript.TryResolveEnvToken` / `TryReadEnvTokenName`
(`src/Antiphon.SessionRunner/HerdrLaunchScript.cs:88-131`). Copy it. Do not share it.

```
^\$(?:\{[Ee][Nn][Vv]:(?<braced>[^}]+)\}|[Ee][Nn][Vv]:(?<bare>[A-Za-z_][A-Za-z0-9_]*))$
```

- Whole token only. `--project=$env:X` and `$env:X_LLM_PROJECT/sub` do not match.
- Bare NAME is `[A-Za-z_][A-Za-z0-9_]*` (`X_LLM_PROJECT` matches). Braced NAME is `[^}]+`.
- Lookup: exact key first, then ordinal-ignore-case walk (Windows env names). Missing name:
  leave the token. Present-and-empty: replace with `""` (wrapper already exits 2 on empty
  `--project`).

---

## Decision

Expand **profile arguments only**, after the env merge and **before** ExtraArgs are appended and
before `ApplyModelArgument`. ExtraArgs are not expanded — the caller already resolved those.

Placement is the card's, not after `{{key:NAME}}`. `X_LLM_PROJECT` is a project name in launch env,
not a key placeholder. If a name's value is still `{{key:…}}`, the expanded arg trips
`EnsureAbsent` and the launch refuses — fail-closed, not a thing to paper over.

Kind defaults (`GROK_TELEMETRY_ENABLED` etc.) run after `ApplyModelArgument` and do not feed this
expansion. That is fine: wrappers do not put those names in argv.

Do not log the replacement. An argv token could theoretically name a secret; argv is already
visible to process listings, and a log line would duplicate it.

---

## S1 — helper, call site, pin tests, living docs

**Files**

| Path | Change |
|---|---|
| `server/Application/Services/DollarEnvArg.cs` | **New.** `internal static partial class` with `Expand`, `TryResolve`, `TryReadName`, and the same `[GeneratedRegex]` as `HerdrLaunchScript.EnvToken`. Comment: keep in lockstep with `HerdrLaunchScript.TryResolveEnvToken` (CARD-0341); do not share a type. |
| `server/Application/Services/AgentTuiLaunchResolver.cs` | In `ResolveCoreAsync`, after the ExtraEnv merge (`:385`) and before ExtraArgs (`:387-389`): copy each profile argument through `DollarEnvArg.Expand(argument, environment)` into `args`, then `AddRange` ExtraArgs unchanged, then `ApplyModelArgument`. |
| `tests/Antiphon.Tests/AgentTui/DollarEnvArgTests.cs` | **New.** No DB. Matcher matrix (below). |
| `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs` | One resolver test (below). Add optional `string[]? arguments = null` to `SeedProfileAsync` (default stays `["--auto", "--mini"]`). |
| `docs/ai-agent-tui-configuration.md` | In the gkp profile section (`:103-104`): the **server** expands whole-token `$env:NAME` in **profile** args so PtyHost `CreateProcess` gets the value. ExtraArgs are not expanded. CARD-0341 herdr expansion remains a second pass on that lane. |
| `docs/agent-credentials.md` | After the merge-order table (`:80-86`): profile argv whole-token `$env:NAME` / `${env:NAME}` is expanded against that merged dict before kind defaults and `{{key:}}`. ExtraArgs are not. |

Do not edit `docs/cards/` (generated). Do not touch `HerdrLaunchScript`, the gkp gate, or
`AgentRegistry.Resolve`.

**Call site shape** (`ResolveCoreAsync`, replacing `:387-389`):

```csharp
var args = new List<string>(arguments.Length + (options.ExtraArgs?.Count ?? 0));
foreach (var argument in arguments)
    args.Add(DollarEnvArg.Expand(argument, environment));
if (options.ExtraArgs is not null)
    args.AddRange(options.ExtraArgs);

var (effectiveModelId, modelArgument) = ApplyModelArgument(profile, revision, agent, options, args);
```

`Expand` returns the argument unchanged when it is not a resolvable whole-token.

**`DollarEnvArgTests` (no DB)**

| Case | Input | Env | Output |
|---|---|---|---|
| pin | `$env:X_LLM_PROJECT` | `X_LLM_PROJECT=PredictionMarkets` | `PredictionMarkets` |
| braced | `${env:X_LLM_PROJECT}` | same | `PredictionMarkets` |
| `env:` case | `$ENV:X_LLM_PROJECT` | same | `PredictionMarkets` |
| name case | `$env:x_llm_project` | `X_LLM_PROJECT=PredictionMarkets` | `PredictionMarkets` (ignore-case fallback) |
| unknown | `$env:MISSING` | (no `MISSING`) | `$env:MISSING` |
| substring | `--project=$env:X_LLM_PROJECT` | set | unchanged |
| suffix | `$env:X_LLM_PROJECT/sub` | set | unchanged |
| empty name | `$env:` / `${env:}` | — | unchanged |
| empty value | `$env:X_LLM_PROJECT` | `X_LLM_PROJECT=""` | `""` (name exists) |
| non-token | `--project` | set | unchanged |

**Resolver pin** (isolated schema, the card's test):

Seed a Grok (or OpenCode) profile with arguments
`["--project", "$env:X_LLM_PROJECT"]`. Resolve with

```csharp
new AgentLaunchOptions(
    Cols: 120,
    Rows: 30,
    AgentEnv: new Dictionary<string, string> { ["X_LLM_PROJECT"] = "PredictionMarkets" },
    ExtraArgs: ["$env:X_LLM_PROJECT"])
```

Assert:

- `resolved.Spec.Args` contains `"PredictionMarkets"` immediately after `"--project"`.
- `resolved.Spec.Args` still contains the ExtraArgs token `"$env:X_LLM_PROJECT"` (not a second
  `PredictionMarkets` from ExtraArgs).
- `resolved.Spec.Env["X_LLM_PROJECT"]` is `"PredictionMarkets"`.

Blank `ModelArgumentName` on that seed so `ApplyModelArgument` does not append `--model` and
scramble `TakeLast`.

---

## Out of scope

- Interpolating `$env:` inside a larger string (`--project=$env:X`).
- Expanding ExtraArgs, discovery args, or version args.
- The legacy `AgentRegistry.Resolve` / no-profile path.
- Sharing a helper with `HerdrLaunchScript` or changing CARD-0341.
- Moving expansion to after `{{key:NAME}}` or kind defaults.
- UI, API, migrations.

---

## Verify

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0345/ -- --treenode-filter "/*/*/DollarEnvArgTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0345/ -- --treenode-filter "/*/*/AgentTuiLaunchResolverTests/*"
```

Forward slash on `OutputPath`. Delete every `bin-card0345` directory the graph dropped before
finishing.

No client, E2E, or session-runner tests. No AppHost restart.
