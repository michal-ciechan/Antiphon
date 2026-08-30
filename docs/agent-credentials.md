# Agent credentials and the launch environment

Every agent session is a child process, and everything it knows about who it is and how to reach a
model arrives as **environment variables on that process**. This document is the reference for
where those values come from, which layer wins when two disagree, and where a secret is allowed to
live.

Which *variables* a given agent kind needs — `ANTHROPIC_API_KEY`, `GROK_CLI_CHAT_PROXY_BASE_URL`,
`OPENAI_API_KEY` and the rest — is [agent-kinds.md](agent-kinds.md). This document is the
machinery underneath.

## Source of truth

| Fact | Owner |
|---|---|
| Merge order and kind defaults | `server/Application/Services/AgentTuiLaunchResolver.cs` |
| `{{key:NAME}}` syntax, where it is legal, the tripwire | `server/Application/Services/ApiKeyPlaceholder.cs` |
| Key lookup, scoping, failure messages | `server/Application/Services/ApiKeyEnvResolver.cs` |
| Env name/value validation and the `ANTIPHON_*` refusal | `server/Application/Services/AgentLaunchEnv.cs` |
| Key-ring location and protection modes | `server/Application/Settings/AgentTuiSettings.cs` |

## 1. Four places a credential can live

They are not interchangeable. Pick by what the credential is *for*.

| Store | What it holds | Reached as | Use when |
|---|---|---|---|
| **Wrapper-managed auth** | nothing in Antiphon — `claude.exe` / `grok.exe` is already logged in as the Windows user, or a wrapper script owns the keys | n/a | **the default, and the right answer for normal operation** |
| **API keys** (`ApiKey` entity, `/api/api-keys`) | a named, Data-Protection-encrypted value, global or per project | `{{key:NAME}}` inside any env **value** | a secret several agents reference, under whatever variable name each one needs |
| **Profile managed secrets** (`AgentTuiSecret`, `/api/agent-tui/profiles/{id}/secrets/{name}`) | a Data-Protection-encrypted value whose **name *is* the environment variable name** | exported directly on any session launched from that profile | a credential that authenticates the runner program under one profile |
| **LLM providers** (`LlmProvider`, `/api/settings/providers`) | the key for **Antiphon's own** model calls (drafting, the check interpreter, summarisation) — *not* agent sessions | server-side only, never returned to the frontend | configuring the server's own LLM access |

The last row is the one people conflate. `Llm:Providers:anthropic:ApiKey` has nothing to do with an
agent session: an empty provider key does not block a TUI agent from launching. See
[bootstrap.md](bootstrap.md) for how those are seeded (`dotnet user-secrets --id antiphon-server`,
or the Settings UI).

`ApiKey` and `AgentTuiSecret` coexist deliberately. A profile secret says *"this profile
authenticates with `ANTHROPIC_API_KEY=<this>`"*; an API key says *"the value called
`anthropic-default` is `<this>`, and anyone may reference it"*.

## 2. Merge order

`AgentTuiLaunchResolver` builds one dictionary, in this order. **Later wins.**

| # | Layer | Where it is set |
|---|---|---|
| 1 | Profile revision non-secret env | TUI profile revision |
| 2 | Profile managed secrets | `PUT /api/agent-tui/profiles/{id}/secrets/{name}` |
| 3 | Project default env | `Project.DefaultLaunchEnvJson` |
| 4 | Inherited caller env | task `InheritedLaunchEnvJson` (CARD-0260: snapshot of the caller's LLM-routing names at create) |
| 5 | **The agent's own launch env** | `Agent.LaunchEnvJson` (agent PATCH `launchEnv`) |
| 6 | Launch-time override | task `envOverride` / `delegate.ps1 -EnvOverride` |
| 7 | `ExtraEnv` — Antiphon's `ANTIPHON_*` orchestration identity | code |

The reasoning, which is worth keeping in mind when you are tempted to reorder it: the agent's own
field outranks inherited env and the profile because it is the more specific thing somebody wrote
about *this agent*; inherited env outranks the project default because the caller's actual routing
is more specific than the blanket; a project default outranks the shared profile because it is a
credential/endpoint fact about this project's agents; none of them outrank `ExtraEnv`, which
carries Antiphon's own identity plumbing. An explicit `-EnvOverride` still wins over inherited.

**Then** two things happen, in this order:

1. **Kind defaults are applied** (`ApplyClaudeEnvironmentDefaults`, `ApplyGrokEnvironmentDefaults`)
   — but only for keys nobody has set. See [agent-kinds.md](agent-kinds.md) §4/§5 for the values.
2. **`{{key:NAME}}` placeholders are resolved** over the fully-merged result. This is deliberately
   last, so a placeholder works identically whichever layer contributed the value.

## 3. `{{key:NAME}}` — API key placeholders

```
ANTHROPIC_API_KEY = {{key:anthropic-default}}
```

**Legal in environment VALUES only.** Not in variable names, not in launch arguments, not in
`--append-system-prompt` text, not in a brief. Arguments are visible to any process lister and are
quoted into failure reasons and argv-integrity tests; system-prompt text additionally lands in
transcripts. A secret in either is a secret published.

That rule is **enforced, not documented**: `ApiKeyPlaceholder.EnsureAbsent` refuses a launch whose
arguments still carry the marker, rather than silently stripping it — silently stripping would
launch an agent whose instructions lost a line nobody was told about.

**Detection is looser than resolution, on purpose.** The resolver replaces well-formed tokens; the
tripwire refuses anything still carrying the six characters `{{key:`. A malformed name such as
`{{key:has space}}` matches the marker but not the token, so a strict-only tripwire would export it
to the child as literal text. Loud beats literal. The documented cost: a value that genuinely
contains those six characters fails its launch by name, and there is no escape syntax.

**Names.** `[A-Za-z0-9_.-]+`, at most 128 characters, compared **Ordinal** (case-sensitive). The
stored-name charset and the placeholder charset are the same class by construction — a key that
cannot be spelled in a placeholder is a key nothing can ever reference.

**Scope.** A key is global (`ProjectId` null) or belongs to one project. **Project wins over
global.** The project is resolved from the agent's `BoardId → Board.ProjectId` — the mapping an
operator actually set. Deriving a project from a working directory was rejected as unreliable
(worktrees are sibling directories, and a prefix match would silently mis-scope a secret). An
unscoped pool delegate resolves **global keys only**.

**Failures.** Every arm names the key and the scopes searched, and none of them carries a value.
The resolver logs key *names* only — never a value, never a substituted environment value.

**Endpoints.**

```
GET    /api/api-keys                              # every key, both scopes
GET    /api/api-keys/global
PUT    /api/api-keys/{name}                       # create or replace a global key
DELETE /api/api-keys/{id}
GET    /api/projects/{projectId}/api-keys
PUT    /api/projects/{projectId}/api-keys/{name}
```

Values are **write-only** — nothing reads a key back out over HTTP.

## 4. Validation limits

`AgentLaunchEnv.Validate` (agent `launchEnv`, project default, task override):

| Rule | Limit |
|---|---|
| Entries | 64 |
| Name length | 200 |
| Value length | 4 000 (same ceiling the key store enforces, at write time *and* after decrypt) |
| Name may not contain | `=`, `\0`, a `{{key:` marker, or a duplicate of another name |
| Value may not contain | a **malformed** placeholder — the 422 names the variable and restates the form |

`AgentLaunchEnv.ValidateOverride` adds one rule for the **newer** surfaces (task `envOverride`,
project default): **any name starting `ANTIPHON_` is refused 422 by name.** Reject, never strip — a
stripped key is a silent no-op the operator discovers only when the launch behaves as if they typed
nothing. This is deliberately *not* retrofitted onto the agent PATCH `launchEnv` surface, because
refusing there now could 422 an already-stored config on its next unrelated save.

A non-empty per-task override also **excludes that task from warm-pool reuse** — reuse launches no
process, so the overlay could never apply — and combining it with `-OnAgent` is refused 422 for the
same reason.

```powershell
pwsh -File scripts/delegate.ps1 Code -Goal '...' `
  -EnvOverride @{ ANTHROPIC_BASE_URL='http://proxy:8080'; ANTHROPIC_API_KEY='{{key:proxy-key}}' }
```

## 5. Key custody

Both encrypted stores use ASP.NET Data Protection. Ciphertext is in the database; **the keys are
not.**

| Platform | Default key ring |
|---|---|
| Windows | `%LOCALAPPDATA%\Antiphon\DataProtection-Keys` |
| Linux / macOS | `$XDG_DATA_HOME/antiphon/data-protection-keys`, else `~/.local/share/antiphon/data-protection-keys` |

- Override with `AgentTui:KeyRingPath`. **Absolute paths only** — a relative value is refused at
  startup.
- Production-like installs should protect the ring with an installation X.509 certificate
  (`AgentTui:KeyProtection`, modes under `Mode` / `CertificatePath` / `CertificateThumbprint` / …).
- **Back the key ring up *with* the database.** Losing it makes every managed ciphertext
  unrecoverable. The recovery is to replace the secrets, never to bypass encryption.
- Protection is per-row and keyed on the row's `Id`, never its `Name`, so renaming a key does not
  orphan its ciphertext.
- Wrapper-managed profiles still launch when the ring is missing. That is the fallback worth
  keeping.

## 6. Things that have gone wrong here before

- **Setting only `GROK_XAI_API_BASE_URL` and believing the CLI is redirected.** It redirects the
  credential lookup and nothing else; real turns still reach xAI and still cost money. The
  chat redirect is `GROK_CLI_CHAT_PROXY_BASE_URL`. `RealCliStubEnv.ForGrok` throws rather than let
  you build the unsafe overlay.
- **Putting a secret in a launch argument.** Refused, by design — see §3.
- **Assuming `Llm:Providers:*:ApiKey` powers agent sessions.** It does not. It powers Antiphon's
  own model calls.
- **Expecting `ANTIPHON_*` in an override to take effect.** It is refused 422 on the newer
  surfaces, and outranked everywhere else.
- **Echoing a value while debugging.** `bw-fill.ps1` and the key resolver both go out of their way
  never to print one; a bare `Write-Output $secret` or a `Format-List` undoes that.

## See also

- [agent-kinds.md](agent-kinds.md) — which variables each kind actually reads.
- [ai-agent-tui-configuration.md](ai-agent-tui-configuration.md) — the profiles UI, key-ring
  recovery, validation runs.
- [bootstrap.md](bootstrap.md) — where every secret this deployment needs comes from on a fresh
  machine.
