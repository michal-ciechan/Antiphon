# AI Agent TUI Configuration — Operator Guide

Configure terminal AI runners (Claude Code, Codex, OpenCode) through Antiphon instead of editing server files by hand.

## Concepts

| Concept | Meaning |
|---|---|
| **Profile** | Named launch settings: executable, ordered arguments, env, auth mode, guidance |
| **Revision** | Immutable snapshot of a profile. Running sessions keep their revision; edits affect the next session only |
| **Wrapper-managed auth** | Wrapper script owns keys/proxy. Antiphon stores no credential |
| **Managed secrets** | Write-only env values encrypted with ASP.NET Data Protection. Keys live outside the database |
| **Exact model** | Opaque runner model id passed as separate `--model` + value args, or omitted for runner default |

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

Assign Atlas (or any agent) to that profile. Leave model empty to omit `--model`; pick an exact identifier from the profile's discovery catalogue. On this local `ocg.ps1` wrapper, the runnable Grok 4.5 selection is the discovered `maven/grok-4.5` identifier. Do not rewrite a selected identifier to a wrapper default.

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

- `GET/POST/PATCH/DELETE /api/agent-tui/profiles…`
- `PUT/DELETE …/secrets/{name}` (write-only)
- `POST …/models/refresh`, `POST …/validate`
- `GET /metrics/agent-tui`
- Agent create/update accepts `tuiProfileId` + `modelId`

## Smoke verification

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
