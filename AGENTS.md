# Antiphon Agent Context

All AI coding agents working on this project MUST read and follow:

- **Primary conventions:** [docs/project-context.md](docs/project-context.md)
- **Telegram integration** (formatting, gateway, settings): [docs/telegram.md](docs/telegram.md)
- **Orchestrating work through delegates** (card → plan → implement → verify → deploy → close, the
  model tiers, how to write a brief, how to check on a delegate without waiting for a notification):
  [docs/orchestration-loop.md](docs/orchestration-loop.md)

This file contains naming conventions, layer boundaries, enforcement rules,
and architectural decisions that all code must comply with.

## Running Locally

### Prerequisites

- .NET 9 SDK
- Node.js 20+
- Docker (for PostgreSQL)

### First-time setup

1. Copy `appsettings.json.example` to `server/appsettings.json` and fill in your LLM API key(s).

2. Start PostgreSQL:
   ```
   docker compose -f docker-compose.dev.yml up -d
   ```

### Canonical local restart

Use the repo restart script so Aspire, the backend, and the Vite proxy agree on
the fixed dev ports:

```
.\restart.ps1
```

The script restarts server and client resources, checks that stale processes are
not occupying the fixed dev ports, and runs a smoke check against:

- Backend health: `http://localhost:17281/health`
- Frontend/API proxy: `http://localhost:17282/api/projects`
- SignalR negotiate: `http://localhost:17282/hubs/antiphon/negotiate`
- Browser render: `http://localhost:17282` showing `Workflows`

If a stale process owns a dev port, the script prints the PID/process name and
aborts. To intentionally stop the listed port owners, rerun:

```
.\restart.ps1 -StopPortOwners
```

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
