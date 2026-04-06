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

### Start the backend (ASP.NET Core — port 5000)

```
cd server
dotnet run --urls "http://localhost:5000"
```

Migrations run automatically on startup. The server also seeds initial data.

### Creating EF Migrations

**Always stop the server before creating a migration** — the running Aspire process holds file locks.

1. Stop: `.\stop-server.ps1`
2. Create migration: `dotnet ef migrations add <MigrationName> --project server`
3. Restart & verify: `.\restart-server.ps1`
4. Check `C:\MavLog\Antiphon\antiphon-YYYYMMDD.log` — confirm migration applied with no `[ERR]`/`[FTL]` entries

### Start the frontend (React/Vite — port 5173)

```
cd client
npm run dev
```

The Vite dev server proxies `/api` and `/hubs` to `http://localhost:5000`, so the backend must be running first.

Open **http://localhost:5173** in your browser.
