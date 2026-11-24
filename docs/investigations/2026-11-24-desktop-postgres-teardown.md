# 2026-11-24: desktop Postgres leftover teardown

Task eb55da04 (Debug/Ops), operator authority 2026-11-24 ("once confirmed take another backup if
missing, and then teardown the database on desktop"). It follows the backup verification in
[2026-11-24-server2-db-backup-verification.md](2026-11-24-server2-db-backup-verification.md)
(task 2fb54ef1, TEARDOWN-GO).

**Verdict: done.** The desktop container `antiphon-postgres` and its volume `antiphon_pgdata` are
removed. Nothing else changed. The desktop server stayed healthy on the production database on
server2 (`100.93.77.126:5432`) throughout.

## Pre-checks (all passed, 2026-11-24 ~09:40Z)

| Check | Result |
|---|---|
| Production backup `C:\Users\lndco\antiphon-backups\antiphon-20261124T090703Z.dump` | 905,456,635 bytes, SHA-256 `36AD51C138A100CF834D4663A3E86922F02520A8076508056381E3B55C41B89F` (matches) |
| `GET http://localhost:17202/api/version` | `6ed4cb58cd75fe386e94965165d96752452431a8`, `land-v2` |
| `Antiphon.Server.exe` (pid 101588) connections | 13 Established to `100.93.77.126:5432`; none to port 17280 (no process on the host had one) |
| Desktop `pg_stat_activity` other sessions on `antiphon` | 0 (client backends on any database: 0) |
| `ANTIPHON_SKIP_DEV_POSTGRES` (User) | `1` |
| `docker inspect antiphon-postgres` | id `2e84b79675e5...`, `postgres:16`, mount `antiphon_pgdata` -> `/var/lib/postgresql/data`, port 5432 -> 17280 |
| Containers mounting `antiphon_pgdata` | only `antiphon-postgres` |

## Safety-net dump of the leftover

`docker exec antiphon-postgres pg_dump -U antiphon -Fc -f /tmp/... antiphon`, then `docker cp`
(no PowerShell text pipe).

- File: `C:\Users\lndco\antiphon-backups\desktop-leftover-20261124.dump` (kept locally, not uploaded)
- Size: 735,276,428 bytes
- SHA-256: `A30170A0899B1166E34ED870982E7206350EFBF2A46DF70320EEF8420A6484E8` (same in-container
  and on the host)
- `pg_restore -l`: custom format, 934 TOC entries, PostgreSQL 16.14, created 2026-11-24 09:42:59 UTC

## Teardown

1. `docker stop antiphon-postgres` at 2026-11-24T09:45:36Z, then 60 s wait.
   - `/api/version` unchanged, `/health` 200 `Healthy`, same server pid, 12 Established
     connections to `100.93.77.126:5432` (pool size varies).
   - Server log `C:\src\Antiphon\server\logs\antiphon-20261124.log` after the stop: no
     `[ERR]`/`[FTL]` lines, no Npgsql/PostgresException/17280 lines. The `LIVE_SLOW_CALL
     phase=db-query` warnings (13 in that minute, all `outcome=completed`) are the existing
     server2 latency baseline: 192 of them in the 15 minutes before the stop.
2. `docker rm antiphon-postgres` (container only): exit 0.
3. `docker volume rm antiphon_pgdata` (exact name): exit 0.

## Before and after listings

`docker ps -a` before:

```
1b0a524790b2|desktop-links-1|desktop-links|Up 12 days
24bf26c4bdf6|antiphon-redpanda|redpandadata/redpanda:latest|Up 12 days (healthy)
2e84b79675e5|antiphon-postgres|postgres:16|Up 4 days (healthy)
b189914edf00|windmill-desktop-worker|ghcr.io/windmill-labs/windmill|Up 12 days
e59da1a5febd|desktop-caddy-1|desktop-caddy|Up 12 days
```

`docker ps -a` after:

```
e59da1a5febd|desktop-caddy-1|desktop-caddy|Up 12 days
1b0a524790b2|desktop-links-1|desktop-links|Up 12 days
24bf26c4bdf6|antiphon-redpanda|redpandadata/redpanda:latest|Up 12 days (healthy)
b189914edf00|windmill-desktop-worker|ghcr.io/windmill-labs/windmill|Up 12 days
```

`docker volume ls`: 39 before, 38 after. `Compare-Object` of the sorted before and after lists
shows exactly one difference for each listing:

```
containers: 2e84b79675e5|antiphon-postgres|postgres:16   <= (removed)
volumes:    local|antiphon_pgdata                        <= (removed)
```

Named volumes left unchanged: `antiphon_redpanda`, `desktop_caddy_config`, `desktop_caddy_data`,
`gym-stat-datamodel_gymstat-pgdata`, `school-revision-pgdata`, `windmill_desktop_cache`, plus 32
anonymous volumes (all present before and after).

After: `/api/version` `6ed4cb58cd75fe386e94965165d96752452431a8`, `/health` 200 `Healthy`,
`ANTIPHON_SKIP_DEV_POSTGRES=1` still set (2026-11-24T09:47:00Z).

## Not changed

`docker-compose.dev.yml` still declares the Postgres service and `antiphon_pgdata`. Running
`docker compose -f docker-compose.dev.yml up -d` on the desktop would recreate an empty
container and volume. Scripts that still expect the desktop container are filed as follow-up
Backlog cards:

- CARD-2064: `verify-dev-stack.ps1` and `scripts/bootstrap-check.ps1` should respect
  `ANTIPHON_SKIP_DEV_POSTGRES` instead of failing when the desktop container is absent.
- CARD-2065: `deploy-local.ps1`, `reap-zombie-agents.ps1`, `reap-orphaned-pty-hosts.ps1`,
  `pg-qps.ps1` and `measure-session-state-cache.ps1` should default to the server2 production
  database. CARD-0705 (pg_stat_statements on the desktop Postgres) targets the removed container.
