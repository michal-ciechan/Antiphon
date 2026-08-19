# Antiphon Agent Context

All AI coding agents working on this project MUST read and follow:

- **Primary conventions:** [docs/project-context.md](docs/project-context.md)
- **Telegram integration** (formatting, gateway, settings): [docs/telegram.md](docs/telegram.md)
- **Orchestrating work through delegates** (card → plan → implement → verify → deploy → close, the
  model tiers, how to write a brief, how to check on a delegate without waiting for a notification):
  [docs/orchestration-loop.md](docs/orchestration-loop.md)

This file contains naming conventions, layer boundaries, enforcement rules,
and architectural decisions that all code must comply with.

## Working cards from a shell

`scripts/card.ps1` talks to the board API so nothing has to hand-compose HTTP or hand-quote card
text. Its header comment is the full reference; this is the synopsis.

- **A card is addressed the way it's *named*.** `CARD-0051`, `card-51`, `#51`, `51`, or its guid —
  every verb takes any of those. There is no separate "look up the id first" step.
- **Verbs:** `get`, `history`, `new`, `edit`, `move`, `close`, `reopen`, `archive`, `unarchive`, and `-Limits`
  (prints the current title/description/reason/actor length ceilings).
- **All long text comes from a file** — `-DescriptionFile` / `-ReasonFile` (`Get-Content -Raw`), not
  `-Description` / `-Reason` typed inline. This is not a nicety: hand-quoting a multi-line
  description through PowerShell's own escaping is what produced roughly fifteen throwaway scripts
  in a single session.
- **The concurrency-token tradeoff, stated plainly:** every write needs the card's current
  `concurrencyToken`, and the server rotates it on every write. By default the script re-reads the
  card immediately before writing and uses that token, so the window in which someone else's write
  could be clobbered is milliseconds rather than the minutes of a manual read-then-write. That
  window is not zero — it is accepted because two truly concurrent writers still collide on the
  database's unique `(CardId, RevisionNumber)` index, and every content write has been
  revision-logged since CARD-0019, so a clobber is readable and reversible from the card's history.
  Pass `-Token <guid>` for true compare-and-swap against a token you read earlier.
- **A move into an active column no longer starts an agent unless you pass `-Spawn`.** Before
  CARD-0051 it always did, silently — that cost two dead sessions and a stray worktree from one
  bookkeeping PATCH. If you have muscle memory from before, assume nothing starts unless you ask.
  The tick will not pick that card up either (CARD-0087); `-Spawn` or `POST /spawn` starts it.

## Running Locally

### Prerequisites

- .NET SDK matching [`global.json`](global.json) (currently 10.0.204,
  `rollForward: latestMinor`). Projects target `net9.0`.
- Node.js 20+
- Docker Desktop (PostgreSQL is the always-on `antiphon-postgres` container on port 17280)
- pwsh 7 via `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`

### First-time setup

Follow [docs/bootstrap.md](docs/bootstrap.md). Canonical path is the Aspire
stack (API 17202, Vite 17203, session-runner 17204; Postgres already on 17280).
Do **not** copy `appsettings.json.example` over the tracked
`server/appsettings.json` — that file is already in git. Put LLM keys in
`dotnet user-secrets` against id `antiphon-server`, or in the Settings UI after
first start. The root `appsettings.json.example` is a shape reference only.

### Canonical local restart

Aspire is the default. If an AppHost may already be running:

```
pwsh -File scripts/restart-apphost.ps1
```

Never launch a second bare `dev-aspire.ps1` — it collides with the old process
and the new code never goes live. Smoke: `http://localhost:17202/health`,
client at `http://localhost:17203`, session-runner at `http://localhost:17204`,
dashboard pinned at `http://localhost:17205`.
`pwsh -File verify-dev-stack.ps1 -SkipBrowser` is the health check.

Simple-mode fallback (no Aspire): `.\dev-start.ps1` / `.\restart.ps1`, ports
17281 (API) / 17282 (Vite) / 17204 (session-runner, the always-on daemon). Postgres is still 17280.
`pwsh -File verify-dev-stack.ps1 -SimpleMode`.

### Manual backend fallback (ASP.NET Core — port 17281)

```
cd server
dotnet run --urls "http://localhost:17281"
```

Migrations run automatically on startup. The server also seeds initial data.

### Creating EF Migrations

**Always stop the server before creating a migration** — the running Aspire process holds file locks.

1. Stop: `.\stop-server.ps1`
2. Create migration: `dotnet ef migrations add <MigrationName> --project server`
3. Restart & verify: `.\restart-server.ps1`
4. Check `C:\MavLog\Antiphon\antiphon-YYYYMMDD.log` — confirm migration applied with no `[ERR]`/`[FTL]` entries

### Logs: where they are, how long they last, how to turn a source back up

| File | Written by | Retention |
|---|---|---|
| `server/logs/antiphon-*.log` | server Serilog (`Serilog:LogPath`) | **5 days**, plus a 14 x 100 MB (1.4 GB) disk backstop |
| `%TEMP%\antiphon-logs\session-runner-*.log` | session-runner Serilog — **not** under `logs/` | 14 days / 14 x 50 MB |
| `logs/session-runner.log`, `logs/fake-gateway.log` | `scripts/run-daemon.ps1` stdout capture | rolled aside at daemon (re)launch above 20 MB; rolls pruned after 5 days / 10 files |
| `C:\logs\antiphon\session-runner\*.ansi.log` | per-session raw PTY output | `AuditCleanupService` |

**Retention is by TIME, and `retainedFileCountLimit` is only a disk backstop.** It counts FILES, not
days: before CARD-0043 the server wrote a measured 40.9 MB/hour (99.9% of it two sources at
Information), so a 100 MB file filled in 21 minutes under load and the "14" that read as a fortnight
was 5-45 HOURS. What actually delivers 5 days is the write rate staying under
1.4 GB / 5 days = **11.7 MB/hour**; measured after the change it is 0.03 MB/hour average,
0.9 MB/hour in the busiest window.

**`Logging:LogLevel` in `server/appsettings.json` does not filter the server's logs.** Under
`UseSerilog` the Serilog logger factory bypasses the Microsoft.Extensions.Logging filter rules
entirely — `Microsoft.AspNetCore` events were still being written at Information despite that section
saying Warning. **`Serilog:MinimumLevel`** is the one that decides.

**Turning a source back up for one debugging session** needs no rebuild — the levels are
configuration. Either edit `server/appsettings.json`:

```jsonc
"Serilog": { "MinimumLevel": { "Override": {
  "Microsoft.EntityFrameworkCore.Database.Command": "Information"   // full SQL per query, ~30 MB/hour
} } }
```

...or set it in the environment and restart the server (`pwsh -File scripts/restart-apphost.ps1`):

```powershell
$env:Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command = 'Information'
```

Re-arming EF or `System.Net.Http.HttpClient` puts the rate back to ~40 MB/hour, which is ~34 hours of
history inside the 1.4 GB backstop. To keep 5 days at that rate, raise the budget too —
`Serilog:RetainedFileCountLimit` (and `Serilog:FileSizeLimitMb`, `Serilog:RetainedFileTimeLimitDays`)
are configuration as well; 5 days at 40.9 MB/hour needs ~4.8 GB.

`Antiphon` is pinned to Information explicitly, so delegation, session, orchestration and supervision
logging survives any future turn-down of `Default`. Do not remove that override.

### Start the frontend (React/Vite — port 17282)

```
cd client
npm run dev
```

The Vite dev server proxies `/api` and `/hubs` to `http://localhost:17281`, so the backend must be running first.

Open **http://localhost:17282** in your browser.
