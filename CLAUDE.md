# Claude Code Configuration

See [AGENTS.md](AGENTS.md) for all project conventions and context.

## Gotchas

- **Dev compose file**: Always use `docker compose -f docker-compose.dev.yml up -d` — the default `docker-compose.yml` is not the dev stack.
- **Startup order**: Postgres must be healthy before starting the .NET server. Check with `docker compose -f docker-compose.dev.yml ps`.
- **npm install first**: `client/node_modules` may not exist — run `npm install` before `npm run dev` or `npm run storybook`.
- **Storybook v9+**: `@storybook/addon-essentials` does not exist for Storybook v9+. It was folded into core. Remove it from `package.json` if present — do not try to install it.
- **Orphaned Aspire DCP conflict**: If Postgres auth fails with wrong user/DB ("school_app" instead of "antiphon"), check for a stale `dcpctrl.exe` from a different Aspire project: `Get-NetTCPConnection -LocalPort 5432 -State Listen`. If `dcpctrl.exe` owns port 5432, kill it: `taskkill /F /PID <pid>`. It's safe — the correct AppHost respawns it.
- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group.
