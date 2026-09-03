# AI Agent TUI Configuration — Operator Guide

Configure terminal AI runners (Claude Code, Codex, OpenCode, Grok Build TUI) through Antiphon instead of editing server files by hand.

> **Scope.** This page is the **profiles UI**: how to create, edit, validate and recover a runner
> profile. Two companion references carry what used to be missing here:
>
> - **[agent-kinds.md](agent-kinds.md)** — the per-kind facts. Which executable and arguments,
>   which model a tier resolves to, and **the actual environment variable names each provider
>   reads** (`ANTHROPIC_BASE_URL` / `ANTHROPIC_API_KEY`; `GROK_CODE_XAI_API_KEY` +
>   `GROK_CLI_CHAT_PROXY_BASE_URL`, and why `GROK_XAI_API_BASE_URL` alone is a false safety;
>   `OPENAI_API_KEY` plus Codex's five `-c` launch arguments).
> - **[agent-credentials.md](agent-credentials.md)** — where a secret may live, the six-layer env
>   merge order, and `{{key:NAME}}` API-key placeholders.

## Concepts

| Concept | Meaning |
|---|---|
| **Profile** | Named launch settings: executable, ordered arguments, env, auth mode, guidance |
| **Revision** | Immutable snapshot of a profile. Running sessions keep their revision; edits affect the next session only |
| **Wrapper-managed auth** | Wrapper script owns keys/proxy. Antiphon stores no credential |
| **Managed secrets** | Write-only env values encrypted with ASP.NET Data Protection. Keys live outside the database |
| **Exact model** | Opaque runner model id passed as separate `--model` + value args, or omitted, in which case the agent's tier picks the model for Claude/Grok/Codex ([agent-kinds.md](agent-kinds.md)), and a profile whose model argument is blank passes none at all |

## UI

1. Open **Settings → AI Agent TUI**.
2. Create or edit a profile (direct executable or wrapper script).
3. For managed auth, set secret env names, then set/replace/clear values (inputs clear after save).
4. **Validate** and **Refresh models**.
5. In agent create/settings, pick an enabled profile and optional exact model.

## Local OpenCode Gateway profile

On this machine, create **OpenCode Gateway** as wrapper-managed:

| Field | Value |
|---|---|
| Executable | `pwsh.exe` |
| Launch args | `-NoProfile`, `-ExecutionPolicy`, `Bypass`, `-File`, `C:\Users\mike.ciechan\.local\bin\ocg.ps1`, `--auto`, `--mini` |
| Version args | same prefix + `--version` |
| Discovery args | same prefix + `models` |
| Auth | WrapperManaged |
| Model arg | `--model` |

Do **not** copy API keys or proxy values out of `ocg.ps1`.

Assign Atlas (or any agent) to that profile. Leave model empty to omit `--model` (OpenCode; Claude/Grok/Codex agents receive their tier's alias instead); pick an exact identifier from the profile's discovery catalogue. On this local `ocg.ps1` wrapper, the runnable Grok 4.5 selection is the discovered `maven/grok-4.5` identifier. Do not rewrite a selected identifier to a wrapper default.

## Local Grok Build TUI profile

Grok is a first-class runner kind (`AgentKind.Grok`), not only an OpenCode model id. Create **Grok** as wrapper-managed:

| Field | Value |
|---|---|
| Runner type | Grok |
| Executable | `grok.exe` (typically `%USERPROFILE%\.grok\bin\grok.exe`) |
| Launch args | `--always-approve`, `--no-alt-screen` |
| Version args | `--version` |
| Discovery args | `models` |
| Auth | WrapperManaged (login lives in `~/.grok/auth.json`) |
| Model arg | `--model` |

Pick `grok-4.6` (default) or `grok-4.5` from the catalogue. Sessions resume with `--resume <session-id>`; standing instructions go through `--rules`, never `--append-system-prompt`.

**Structured activity is live for Grok** (CARD-0080 S2): the runner tails Grok's own ACP
`updates.jsonl` at `GROK_HOME/sessions/<url-enc-cwd>/<session-id>/updates.jsonl`, selected by the
launch request's `transcriptFormat: "grok"`. Do not point the Claude JSONL tailer at it — the two
formats and their discovery rules are different, and Grok's path is deterministic precisely because
it needs none of Claude's claim machinery.

Note that the level ladder resolves **every** tier to `grok-4.6` (CARD-0169); `grok-4.5` remains
selectable as an explicit profile model but is not what a `Low`/`Medium` dispatch will pick.

## Local llm-key-proxy (gkp) Grok profile

`gkp` accepts exactly one model and pins it itself. A profile that still passes `--model` (the
pre-CARD-0182 default) contradicts that and the wrapper exits 1. Create **Grok (gkp)** as
wrapper-managed:

| Field | Value |
|---|---|
| Runner type | Grok |
| Executable | `pwsh.exe` |
| Launch args | `-NoProfile`, `-ExecutionPolicy`, `Bypass`, `-File`, `C:\Users\mike.ciechan\.local\bin\gkp.ps1` (plus whatever `gk-common.ps1` already takes) |
| Auth | WrapperManaged |
| **Model arg** | **blank** |
| Models list | `maven-grok` optional |

Leave the model argument blank and leave every agent's exact model empty. Pinning `maven-grok` on
the agent also works and is what a profile saved before CARD-0182 does (the backfill writes
`--model` into those revisions so the workaround stays byte-identical on deploy). Blanking the
field on a new revision is what then activates "no argument". An exact model on a blank-field
profile is 409 `model_argument_unsupported`.

**Launch env the gkp profile needs (CARD-0341).** A gkp launch is refused by the session runner on
the herdr lane (409 `herdr_gkp_env_missing`, stored as the session's `FailureReason`) unless the
merged launch env carries `X_LLM_PROJECT` (or the profile passes a literal `--project` value),
`GROK_BASE_URL`, and a dummy `XAI_API_KEY` (or `GROK_CODE_XAI_API_KEY`); `GROK_CLI_CHAT_PROXY_BASE_URL`
should be there too or Grok's chat-proxy calls go to its default `cli-chat-proxy.grok.com`. Seed
them on the project's `DefaultLaunchEnv` (every agent on that board inherits) or the agent's
`launchEnv`. The **server** expands a whole-token `$env:NAME` / `${env:NAME}` in **profile** args
against that merged env (CARD-0345) so PtyHost `CreateProcess` receives the value, not the token.
ExtraArgs are not expanded. CARD-0341's herdr expansion remains a second pass on that lane, so a
`--project $env:X_LLM_PROJECT` profile arg reaches the wrapper as the project name whichever pane
it lands in.

## Key custody

- Default key ring (Windows): `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`
- Default key ring (Linux/macOS): `$XDG_DATA_HOME/antiphon/data-protection-keys` or `~/.local/share/antiphon/data-protection-keys`
- Override with `AgentTui:KeyRingPath` (absolute path only).
- Production-like installs should protect the ring with an installation X.509 cert (`AgentTui:KeyProtection`).
- Back up key ring **with** the database. Lost keys make managed ciphertext unrecoverable — replace secrets, do not bypass encryption.
- Wrapper-managed profiles still launch when keys are missing.

## Recovery

| Problem | Action |
|---|---|
| Key ring missing/wrong | Restore keys or replace managed secrets; disable managed profiles if needed |
| Stale discovery | Keep prior catalogue; retry refresh; curated suggestions remain selectable |
| Invalid executable | Fix path/args; re-validate |
| Failed validation | Read stage results; fix auth/cwd/exe; re-test |
| Rollback | File definitions remain seed/rollback source; imported provenance is retained |

## API surface (summary)

- `GET /api/agent-tui/runner-types` — the per-kind capability catalogue
- `GET/POST/PATCH/DELETE /api/agent-tui/profiles…`, `POST …/profiles/{id}/duplicate`
- `PUT/DELETE …/profiles/{id}/secrets/{environmentName}` (write-only)
- `GET …/profiles/{id}/models`, `POST …/models/refresh`, `GET …/profiles/{id}/capabilities`
- `POST …/profiles/{id}/validate`, `GET /api/agent-tui/validation-runs/{runId}`
- `GET /metrics/agent-tui` (root route, not under `/api`)
- Agent create/update accepts `tuiProfileId` + `modelId` (plus `launchEnv`, `sessionBackend`)
- Named secrets that several profiles share live in the separate API-key store —
  `/api/api-keys` and `/api/projects/{projectId}/api-keys`, referenced as `{{key:NAME}}`
  ([agent-credentials.md](agent-credentials.md))

The full route map is [antiphon-api.md](antiphon-api.md).

## Smoke verification

`verify-agent-tui-profile.ps1` defaults to `-BaseUrl http://localhost:17282` — the **simple-mode**
Vite origin, which proxies `/api`. On the canonical Aspire stack pass
`-BaseUrl http://localhost:17202` instead.

```powershell
# After stack is up:
.\scripts\verify-agent-tui-profile.ps1 `
  -BaseUrl http://localhost:17282 `
  -AgentName Atlas-Orchestrator `
  -ProfileName "OpenCode Gateway" `
  -ExpectedReply "Atlas OpenCode default verified."

.\scripts\verify-agent-tui-profile.ps1 `
  -BaseUrl http://localhost:17282 `
  -AgentName Atlas-Orchestrator `
  -ProfileName "OpenCode Gateway" `
  -ModelId "maven/grok-4.5" `
  -ExpectedReply "Atlas OpenCode explicit model verified."

.\verify-dev-stack.ps1 -SimpleMode
```

The smoke script refuses to retain evidence containing a supplied canary secret.

## Deployment scope

This checkout has no separate DEV, production, GitOps, or CI deployment target. The Mikeys.Tools-hosted simple stack is the accepted local DEV-equivalent and release installation for this feature. Run the two OpenCode probes above and `verify-dev-stack.ps1 -SimpleMode` after a restart; retain only their sanitized evidence. A future deployed target must name its key-ring custody and release owner before it is treated as a production deployment.

## Observability

- Metrics: `/metrics/agent-tui` (bounded labels only — no secrets, paths, or model ids as labels)
- Dashboard contract: **AI Agent TUI Configuration Health** (see feature `07-observability.md`)
- Failures that need a human: key protection down with managed profiles; import cannot establish a default

## Ownership

- Antiphon maintainers: product behaviour, adapters, redaction, metrics
- Installation operator: key ring backup, credential rotation, local wrappers (`ocg.ps1`)
