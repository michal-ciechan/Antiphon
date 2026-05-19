# Antiphon Agent Context

All AI coding agents working on this project MUST read and follow:

- **Primary conventions:** [docs/project-context.md](docs/project-context.md)

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

### Start the frontend (React/Vite — port 17282)

```
cd client
npm run dev
```

The Vite dev server proxies `/api` and `/hubs` to `http://localhost:17281`, so the backend must be running first.

Open **http://localhost:17282** in your browser.
