# Claude Code Configuration

See [AGENTS.md](AGENTS.md) for all project conventions and context.

## Dev Port Map

| Port  | Service                    |
|-------|----------------------------|
| 17200 | AppHost resource service   |
| 17201 | PostgreSQL (dynamic host)  |
| 17202 | .NET server (API)          |
| 17203 | Vite dev client            |
| 17204 | Session runner             |

## Gotchas

- **Dev compose file**: Always use `docker compose -f docker-compose.dev.yml up -d` — the default `docker-compose.yml` is not the dev stack.
- **Startup order**: Postgres must be healthy before starting the .NET server. The AppHost handles this via `WaitFor(postgres)`.
- **npm install first**: `client/node_modules` may not exist — run `npm install` before `npm run dev` or `npm run storybook`.
- **Storybook v9+**: `@storybook/addon-essentials` does not exist for Storybook v9+. It was folded into core. Remove it from `package.json` if present — do not try to install it.
- **Orphaned Aspire DCP conflict**: Check for a stale `dcpctrl.exe` from a different Aspire project holding port 17202: `Get-NetTCPConnection -LocalPort 17202 -State Listen`. Kill the owning PID if foreign. Restarting the AppHost respawns DCP.
- **Postgres password**: The AppHost uses a fixed password (`antiphon_dev`) via the `pg-password` parameter so the persistent `antiphon-pgdata` volume survives restarts. Do not delete the volume without also recreating it.
- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group.
