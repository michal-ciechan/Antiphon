# Bootstrapping Antiphon

A brand-new Windows machine or a fresh clone. Aspire is the path. Every
automatable step already has a tracked script — this file sequences them.
Do not copy `appsettings.json.example` over the tracked
`server/appsettings.json`.

## What "done" means

- The Aspire stack answers `/health` on **17202** (API), the Vite client is
  on **17203**, and the session-runner is on **17204**. Postgres is the
  always-on `antiphon-postgres` container on **17280**. The Aspire dashboard
  is pinned at **http://localhost:17205**.
- `pwsh -File scripts/bootstrap-check.ps1` exits 0.
- You can create an agent in the UI (or `POST /api/agents` then
  `POST /api/agents/{id}/start`) and it reaches **Running**.

Cards, boards, and agents from some other deployment are **not** part of
done. A fresh database correctly contains only the seeder: admin user, BMAD
templates, empty-key LLM provider rows, model routing. Telegram / Redpanda /
channel bind is a separate stand-up — see
[telegram-bot-ops.md](telegram-bot-ops.md).

## Which scenario is this?

Pick one before touching the machine.

### (a) This operator, new machine

`~/.claude` is a git checkout of this operator's private `claude-home` repo
(not a public clone URL — a stranger cannot use it). On the new box: clone
that repo into `~/.claude`, run the `sync` skill, then the machine steps
below. Recurring build-junk cleanup is Windmill on server2
(`u/lndcobra/antiphon_build_junk_cleanup`, Mon 09:00 Europe/London), not a
Windows Scheduled Task — do not re-add a local task.

### (b) New operator

A clone of this repo already has the project skills under
`.claude/skills/`: `antiphon-delegate`, `antiphon-run`, `antiphon-start`,
`telegram-e2e-smoke`, `claude-web`, and the vendored `bmad-*` set. That is
enough to start, stop, and delegate inside Antiphon.

A new operator does **not** get the operator-environment pieces that live
only in `claude-home` / the user profile. Named so the absence is visible:

- Global `~/.claude/CLAUDE.md` and its `@`-imports (`browser-harness`,
  `bitwarden`, `docker-desktop`).
- The rebase-only git policy (`git pull --rebase`; no merge commits).
- The memsearch plugin.
- Always using browser-harness for UI verification (user-level skill).
- Browser-test CDP against the logged-in desktop browser (user-level).
- `~/.claude/commands/rc-status.md` (remote-control LIVE/DROPPED/NO-RC).
- Grok / Claude MCP registrations (see [MCP](#mcp-optional) — none of them
  are required for "done").

### (c) Fresh deployment vs migration

- **Fresh:** seeder-only DB is correct. Create the first agent as step 9.
  Do not invent seed data.
- **Migration:** on the old box run `.\dev-backup.ps1` (defaults to
  `<repo>/backups/`). On the new box run
  `.\dev-restore.ps1 -BackupFile <path-to.zip>`. Also restore the Data
  Protection ring (`%LOCALAPPDATA%\Antiphon\DataProtection-Keys`) *with*
  that dump — see [ai-agent-tui-configuration.md](ai-agent-tui-configuration.md).
  Do not inline `pg_dump`. `.\dev-fresh.ps1` is the nuclear reset (volume +
  `C:\Antiphon\worktrees`), not a bootstrap step.

## Machine steps

Dependency order. Each step points at an existing script; do not reimplement
them here.

### 1. Prerequisites

| Need | Detail |
|---|---|
| Docker Desktop | Running, tray icon idle. Compose file is `docker-compose.dev.yml`, never the default `docker-compose.yml`. |
| .NET SDK | The version `global.json` pins (today **10.0.204**, `rollForward: latestMinor`). Projects target `net9.0` — that is not a contradiction. |
| Node.js | 20+. |
| pwsh 7 | Via the version-independent alias `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`. Never bake a version-pinned MSIX `pwsh.exe` path into a Scheduled Task (that path vanishes on the next PowerShell update). |
| `claude.exe` and/or `grok.exe` | On PATH and logged in as the Windows user. Wrapper-managed TUI auth is how agent sessions authenticate. Empty `Llm:Providers:*:ApiKey` does **not** block a TUI agent. |

### 2. Clone and Windows convention directories

Clone this repo. Then create the directories if they are missing (the
tracked configs assume them; they are outside the repo on purpose):

```
C:\Antiphon\worktrees
C:\logs\antiphon\session-runner
C:\logs\antiphon\check-interpreter
```

`src/Antiphon.SessionRunner/appsettings.json` sets
`SessionLogPath = C:\logs\antiphon\session-runner`. Create the directory;
do not "fix" that path.

### 3. Secrets

See the [secrets table](#secrets) below. Preferred overlay for a new
machine: `dotnet user-secrets set` against id `antiphon-server`. Also
accepted: a gitignored `server/appsettings.Development.json`. **Never**
the tracked `server/appsettings.json`.

### 4. Postgres (and Redpanda)

```
docker compose -f docker-compose.dev.yml up -d
```

Brings up `antiphon-postgres` on **17280** and Redpanda on **19092**. Only
Postgres is required for "done". Channel bridge stays `Enabled: false` in
the tracked file (AppHost forces it on). Telegram / Kafka is not part of
`/health` — [telegram-bot-ops.md](telegram-bot-ops.md).

### 5. First build

```
dotnet build
cd client
npm install
npm run build
```

The E2E fixture (`AntiphonAppFixture.EnsureClientBundleIsCurrent`) hard-fails
when any `client/src` file is newer than `client/dist/index.html`. Build
`client/` once so that check starts green.

### 6. Always-on Scheduled Tasks

```
pwsh -File scripts/install-autostart.ps1
```

Registers **Antiphon Session Runner** (port 17204) and **Antiphon AppHost**
(server 17202, client 17203, dashboard 17205, control API 17207). Caveats
already in the script header / CLAUDE.md:

- Re-running the installer Unregister+Registers, which **terminates a
  running session-runner**. Use `-AppHostOnly` to refresh the AppHost task
  without touching a healthy runner.
- The installer prefers the version-independent
  `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` alias. Never hand-register
  a `WindowsApps\Microsoft.PowerShell_<version>_…\pwsh.exe` path.

Already registered and you just want them up now:

```
Start-ScheduledTask -TaskName "Antiphon Session Runner"
Start-ScheduledTask -TaskName "Antiphon AppHost"
```

### 7. Start the Aspire stack

If an AppHost may already exist (including the logon task from step 6):

```
pwsh -File scripts/restart-apphost.ps1
```

That kills the old AppHost tree, frees the ports, relaunches, and waits for
health. It preserves the session-runner. **Never** launch a second bare
`dev-aspire.ps1` — the old server keeps the ports and the new code never
goes live.

If you are certain nothing is listening yet, first launch only:

```
Start-Process pwsh -ArgumentList @('-NoLogo','-File','<repo>\dev-aspire.ps1') -WindowStyle Normal
```

Never `wt new-tab` (fails `0x80070002` when the title has a space). Never
`-NoNewWindow` (attaches the AppHost to the tool session and kills it when
the session ends). The script exits after ~60s; the AppHost continues in
the background (`logs/apphost.pid`).

### 8. Self-check

```
pwsh -File scripts/bootstrap-check.ps1
```

Read-only. Exit code is the number of FAILs. This is the last automated
step; do not skip it. (The script is slice 2 of CARD-0088; it wraps
`verify-dev-stack.ps1 -SkipBrowser` for the live half.)

### 9. Create the first agent

The seeder did not do this. In the UI at http://localhost:17203 create an
agent and start it, or:

```
POST /api/agents
POST /api/agents/{id}/start
```

"Done" is that agent reaching **Running**.

## Secrets

Never print a secret value. Config keys stay empty in the tracked file.

| Key / secret | Lives today | New machine |
|---|---|---|
| `Llm:Providers:anthropic:ApiKey` / `openai:ApiKey` | Database (Settings UI). Tracked config empty. `dotnet user-secrets list --id antiphon-server` empty. No matching Bitwarden item on 2026-08-19. | `dotnet user-secrets set "Llm:Providers:anthropic:ApiKey" … --id antiphon-server` (preferred) or Settings UI after first start. Seeder copies a non-empty config key into the DB. Also accepted: gitignored `server/appsettings.Development.json`. Never the tracked `server/appsettings.json`. Empty keys do not block a TUI agent. |
| `GitHub:PersonalAccessToken` | Tracked file empty, `Enabled: false`. | Optional. Same user-secrets id if wanted. |
| Agent TUI wrapper auth | `~/.claude` (Claude), `~/.grok/auth.json` (Grok). | Log the TUI in as the Windows user. |
| Agent TUI managed secrets + Data Protection ring | DB ciphertext + `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`. | Fresh ring is expected; re-enter managed secrets. On a migration, back up the ring *with* the `dev-backup.ps1` output ([ai-agent-tui-configuration.md](ai-agent-tui-configuration.md)). |
| Telegram bot token | Bitwarden item **Telegram Bot Tokens (Antiphon / School Revision)** (type: Secure Note). Fields include `antiphon_assistant_bot`, `school_revision_bot`, `antiphon_test_bot`, `school_revision_test_bot`. Stand-up is [telegram-bot-ops.md](telegram-bot-ops.md). | Only if standing up channels. Env / user-secrets on the *gateway*, not the desktop `appsettings.json`. |

User-secrets against the server project:

```
dotnet user-secrets set "Llm:Providers:anthropic:ApiKey" "<paste>" --id antiphon-server
dotnet user-secrets set "Llm:Providers:openai:ApiKey" "<paste>" --id antiphon-server
```

## MCP (optional)

Not required for "done". `scripts/bootstrap-check.ps1` does not probe MCP.
This checkout's Claude project `mcpServers` is empty.

Observed on this operator's machine, 2026-08-19 — not a required set:

| Where | What is registered | Token / notes |
|---|---|---|
| Claude user (`~/.claude.json` `mcpServers`; `claude mcp list`) | `todoist` — `C:\src\ClaudeBot\scripts\todoist-mcp-launch.cmd`. `rider` — JetBrains stdio (currently fails to connect). | Todoist: `%USERPROFILE%\.todoist-token`, refreshed from Bitwarden item **Todoist** field `api_token` by `C:\src\ClaudeBot\scripts\todoist-token-refresh.ps1`. Rider: no token. |
| Grok CLI (`grok mcp list`) | None configured. | — |
| This Grok TUI session | `todoist`, `tasks`, `rider` (rider handshake failed). | Session-injected / inherited; `todoist` uses the same token file. Not product state. |

A new operator who wants Todoist MCP: install `@doist/todoist-mcp` globally,
put the token in `%USERPROFILE%\.todoist-token`, register the launcher. Skip
the whole subsection if you do not care.

## Simple-mode fallback

Not the default. Use this only when you are deliberately not running Aspire.

| | Aspire (default) | Simple mode |
|---|---|---|
| Start | `dev-aspire.ps1` / `scripts/restart-apphost.ps1` | `.\dev-start.ps1` |
| Restart | `pwsh -File scripts/restart-apphost.ps1` | `.\restart.ps1` |
| API | 17202 | 17281 |
| Client | 17203 | 17282 |
| Session-runner | 17204 | 17204 |
| Postgres | 17280 (`antiphon-postgres`) | 17280 (same container) |
| Health | `pwsh -File verify-dev-stack.ps1 -SkipBrowser` | `pwsh -File verify-dev-stack.ps1 -SimpleMode` |

`.\dev-stop.ps1` stops server + client; Postgres stays up. Add
`-IncludePostgres` to stop the container too.

---

pwsh -File scripts/bootstrap-check.ps1
