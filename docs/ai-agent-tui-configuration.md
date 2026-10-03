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

Windows Codex profiles that still name `codex.cmd` (the default in `Agents:Definitions:codex`) do not need a profile migration: the session runner rewrites a recognized npm shim to `node.exe` plus the installed `codex.js` and refuses with 409 `codex_command_line_too_long` / `codex_launcher_unavailable` / `codex_launcher_unsupported` when the fully quoted line, including the longer Node-to-native hop, would overflow. Custom wrappers are not rewritten. See [agent-kinds.md](agent-kinds.md).

CARD-0959's CLI observation on a runner row covers its default launcher. An explicit
launcher diagnostic can observe another native installation or recognized Node/Codex
package through a descriptor-specific probe. It never executes profile `VersionArguments`
or opens authentication files. Profile validation's `RunnerVersion` remains a separate
diagnostic. Unknown wrappers stay unverified; no version observation blocks create,
retry, reuse or dispatch, and those paths start no CLI probe. Existing launcher safety
and authentication rules remain in force. The [CARD-0959 plan](superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md)
defines reporting-only rollout; future compatibility enforcement belongs to CARD-1023.

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

The Linux runner image pins Grok 1.0.41 (CARD-0986), matching the startup
classifier's captured 120x30 dashboard. Other versions or terminal geometries
require separate qualification.

**Startup screen qualification (CARD-0778 / CARD-1004).** The empty composer
marker is ASCII `>` on Windows ConPTY and `❯` (U+276F) in Linux runner containers.
The classifier accepts exactly those two markers, with no following text or ghost
suggestion. It remains fail-closed on the rest of the captured layout: 30 rendered
rows, composer borders at columns 2 and 117, blank status rows, and the exact
enabled shortcut hint. Spinner/`Starting session…`, working hints, sign-in and
trust screens cannot authorize a first prompt. The ready region must still settle.

| Grok Build | Platform | Terminal | Marker | Evidence |
|---|---|---|---|---|
| 1.0.41 | Windows modern ConPTY | 120x30 | ASCII `>` | CARD-0778 captured screen replay and measured complete prompt |
| 1.0.41 | Linux server2-temp container | 120x30 | U+276F `❯` | CARD-1004 real startup frame; real first-prompt canary pending after server activation |
| 1.0.40 | Linux server2 container | 120x30 | U+276F `❯` | CARD-1004 matching real startup frame; this does not change the image pin |

**CARD-1011 Windows routing gate: pending.** The accepted real `d6e4138e`
canary proves its complete task UserPrompt and returned report. WQ-1 is
operator-excluded for CARD-1022; modern Review and amended WQ-3 remain the gates. The proposed Debug policy is inactive until the
[owner's gates](orchestration-loop.md#windows-review-and-debug-routing) pass.

| CLI/build | Actual backend | Geometry/marker | Trust | Task/session/SHA | Receipt/release |
|---|---|---|---|---|---|
| 1.0.41 / `4220f3b224a6` (reported) | Unknown; attribution pending | 120x30 / `>` to confirm | Not observed | `d6e4138e` / `26d8a18c` / `5f214b0c` | UserPrompt/report accepted; runner release ownership pending |
| 1.0.46 / `2765805b9442` [stable] (caller-reported) | ModernConPty 1.24.260710001; WQ-2 reported met, WQ-1 operator-excluded | 120x30 / `>` | Record trust presence/absence; [] or one y accordingly, cleared trust/Ready, one nonce turn required (WQ-3) | `02e5b9b7`; full identities/receipts in [ledger](investigations/2026-10-03-card-1011-windows-grok-qualification.md) pending reconciliation | Whole transcript and release required; modern needs loaded binary paths/hashes |

Backend requests and advertised capabilities do not establish actual execution;
use each session's host log and reject modern fallback. Restore and verify any
temporary backend configuration before accepting WQ-2/3. Diagnose failures
with server-log `screenReason` and the named
`%TEMP%\antiphon-grok-startup\grok-startup-*.txt` (or configured capture directory):
the file keys are `outcome`/`lastScreenReason`. Sign-in content stays suppressed;
captures are diagnostics, and complete UserPrompt transcripts own delivery.

Windows captures live in `tests/Antiphon.Tests/Agents/Fixtures/card0778/`;
the two decoded Linux frames and their dated provenance live in
[`tests/Antiphon.Tests/Agents/Fixtures/card1004/`](../tests/Antiphon.Tests/Agents/Fixtures/card1004/provenance.md).
Version qualification is an evidence policy; the classifier inspects the current
screen shape rather than a version banner. Other sizes remain CARD-0861 work.
Activation requires the server's canonical AppHost restart, with no runner-image
change. The orchestrator's post-rollout unpinned server2-temp Grok canary must
confirm the complete matching `UserPrompt` transcript; readiness or a redraw alone
is not delivery proof. FakeGrok can exercise U+276F through the native PTY and
adapter with `ANTIPHON_FAKE_GROK_LINUX_COMPOSER=1`; its default stays ASCII `>`.

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

Pick `grok-4.7` (default), `grok-4.6`, or `grok-4.5` from the catalogue. Sessions resume with `--resume <session-id>`. CARD-0395 sends composed standing instructions as a typed payload to a runner advertising `grokRulesFileV1`; the runner writes the complete file and adds a short `--rules` bootstrap requesting a read. Ordinary work waits for the internal read acknowledgement. This is model-mediated retrieval; real-model compliance and two-compaction acceptance remain open.

The CARD-0382 Windows raw-argv guard remains unchanged: CR, LF, NUL or more than 4,096 UTF-16 units refuses with `grok_rules_argv_unsafe`. A safe explicit rules value plus composed rules refuses with `grok_rules_source_conflict`; move the append to `SystemPromptAppend`. Installed 1.0.13 treats `--rules @path` as literal text and native resume retains old rules. Neither is a file-loading configuration workaround.

Both server and runner accept positive `GrokRules:MaxFileBytes` (262144), `GrokRules:InitializationTimeoutSeconds` (480), and `GrokRules:RefreshTimeoutSeconds` (480). The file preserves strict UTF-8 bytes without BOM or newline normalization. Invalid content refuses with `grok_rules_content_invalid`; a missing runner feature refuses with `grok_rules_transport_unsupported`. Storage and receipt failures use `grok_rules_file_write_failed` and `grok_rules_receipt_invalid`, with bounded diagnostics.

Rules barriers reject Now/send-now with `grok_rules_initialization_pending`, or the persisted `grok_rules_initialization_failed` / `grok_rules_refresh_failed` after failure. Pending ordinary messages remain held for an explicit decision. Migrating an inline legacy session requires the existing `POST /api/agents/{id}/start` action with `{"fresh":true}`; first checkpoint or hand off as desired. The refusal `grok_rules_legacy_resume_requires_fresh_start` retains history. Removing all rules from a file-backed session likewise requires fresh start (`grok_rules_removal_requires_fresh_start`) because its old bootstrap survives native resume.

**Structured activity is live for Grok** (CARD-0080 S2): the runner tails Grok's own ACP
`updates.jsonl` at `GROK_HOME/sessions/<url-enc-cwd>/<session-id>/updates.jsonl`, selected by the
launch request's `transcriptFormat: "grok"`. Do not point the Claude JSONL tailer at it — the two
formats and their discovery rules are different, and Grok's path is deterministic precisely because
it needs none of Claude's claim machinery.

Note that the level ladder resolves **every** tier to `grok-4.7` (CARD-0169 collapse;
2026-09-21 bump); `grok-4.6` and `grok-4.5` remain selectable as explicit profile models but
are not what a dispatch will pick.

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
