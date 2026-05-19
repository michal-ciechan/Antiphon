# Claude Code Configuration

See [AGENTS.md](AGENTS.md) for all project conventions and context.

## Dev Port Map

| Port  | Service              |
|-------|----------------------|
| 17280 | PostgreSQL           |
| 17281 | .NET server (API)    |
| 17282 | Vite dev client      |
| 17283 | Storybook            |

## Gotchas

- **Canonical local restart**: Use `.\restart.ps1`. It restarts the Aspire resources, enforces the fixed dev ports, and smoke-checks backend health, the Vite API proxy, SignalR negotiate, and a Playwright render of `http://localhost:17282`.
- **Stale dev ports**: If `.\restart.ps1` reports a process on `17281` or `17282`, either stop that PID manually or rerun `.\restart.ps1 -StopPortOwners` to intentionally stop the listed port owners.
- **Dev compose file**: Always use `docker compose -f docker-compose.dev.yml up -d` — the default `docker-compose.yml` is not the dev stack.
- **Startup order**: Postgres must be healthy before starting the .NET server. Check with `docker compose -f docker-compose.dev.yml ps`.
- **npm install first**: `client/node_modules` may not exist — run `npm install` before `npm run dev` or `npm run storybook`.
- **Storybook v9+**: `@storybook/addon-essentials` does not exist for Storybook v9+. It was folded into core. Remove it from `package.json` if present — do not try to install it.
- **Orphaned Aspire DCP conflict**: If Postgres auth fails with wrong user/DB ("school_app" instead of "antiphon"), check for a stale `dcp.exe` from a different Aspire project: `Get-NetTCPConnection -LocalPort 17280 -State Listen`. If a foreign process owns port 17280, kill it: `Stop-Process -Id <pid> -Force`. It's safe — restarting the AppHost respawns DCP.
- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group.
