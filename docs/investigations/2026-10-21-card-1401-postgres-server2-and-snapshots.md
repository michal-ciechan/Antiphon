# CARD-1401: production Postgres on server2, and daily off-host snapshots

Date: 2026-10-21. Investigate task `760095f1` on `feat/card-task-760095f1`. This session ran inside the server2 runner container (`hostname` `0c18bb7f591e`, cgroup `/docker/0c18bb7f591e…`, kernel `4.15.0-213-generic`). It cannot see the desktop Docker daemon or the desktop disk. Desktop-only figures below are cited from repo evidence and marked when they are not a current measurement. No database login, dump, restore, volume change, or credential was performed. Connection strings and passwords are not copied here.

Verdict: **confirmed placement and path, ready for Plan.** The production database is the desktop `antiphon-postgres` container. server2 has no standing Antiphon Postgres. A DB-only move across the path measured today adds on the order of 70–130 ms to every query and is a poor default while receipt scans still return thousands of transcript rows per pass. Daily snapshots do not need that move and should be planned first. Remaining gaps are listed at the end.

## 1. Where production Postgres runs, and what uses it

The always-on development database is a host Docker container, not an Aspire resource.

| Fact | Evidence |
|---|---|
| Compose project `antiphon`, service `postgres`, image `postgres:16`, container `antiphon-postgres` | `docker-compose.dev.yml:13-26` |
| Host port 17280 mapped to container 5432, bind unspecified (all host interfaces) | `docker-compose.dev.yml:32-33` |
| Named volume `pgdata` (project volume `antiphon_pgdata`) mounted at `/var/lib/postgresql/data` | `docker-compose.dev.yml:34-35`, `77-78`; `AGENTS.md` forbids deleting `antiphon_pgdata` unless the database is deliberately recreated |
| `restart: unless-stopped`; preload `pg_stat_statements` is in the compose command | `docker-compose.dev.yml:18-27` |
| AppHost does not start Postgres. It passes `ConnectionStrings:DefaultConnection` through | `Antiphon.AppHost/Program.cs:13-19` |
| Both committed config files set that key to host `localhost`, port `17280`, database `antiphon`, user `antiphon`, with a password present | `Antiphon.AppHost/appsettings.json` key `ConnectionStrings.DefaultConnection`; `server/appsettings.json` same key. `server/appsettings.Development.json` and `Antiphon.AppHost/appsettings.Development.json` are absent |
| Server opens it with Npgsql, Postgres version pinned to 16 | `server/Program.cs:112-122` |
| Live control plane at measurement time | `GET https://antiphon.desktop.codeperf.net/api/version` returned 200, `version=5acbd708d640c9d457c272ec4fb3801122e806fe`, around 2026-10-21 13:40Z. Two samples: TCP connect 18 ms then 2.6 ms, TLS 138 ms then 121 ms, time to first byte 220 ms then 173 ms |

The password value is committed next to the compose environment (`docker-compose.dev.yml:28-31`) and in the two appsettings files. It is not repeated here. There is no `SslMode` anywhere in compose, appsettings, or C# (`rg SslMode` found nothing). The desktop listener is plaintext.

Who opens that database:

- The desktop server process does. Every hosted service in that process shares the pool, including the land worker, land sweep, land notification, dispatcher, retention, and channel bridge (`server/Program.cs:803-932`). Historical sample, 2026-09-25: 14–16 application connections, `max_connections` 100 (`docs/investigations/2026-09-25-card-0698-antiphon-postgres-burns-5-6-cores-transcriptentries-polled-by-full-seq-scan-36x-s.md` container table).
- Hangfire does **not** store jobs in Postgres. Storage is in-memory (`server/Program.cs:934-939`). The worker starts only when `Hangfire:ServerEnabled` is true (`:940-942`). Root `docker-compose.yml:44` sets `Hangfire__ServerEnabled` to false. Recurring jobs are re-registered in process at startup (`server/Program.cs:1085-1107`); a missed fire while the process is down is gone.
- Session runners do not open Postgres. The desktop runner is a separate process on port 17204 (`Antiphon.AppHost/Program.cs:27-33`). The server2 runner phones home and publishes no port (`docker-compose.server2-runner.yml:1-4` and the comment at `:164`).
- `Antiphon.Tests` uses its own `postgres:16-alpine` Testcontainer, database name `antiphon_test`, not port 17280 (`docs/testing-and-build.md` integration-test paragraph; `TestDbFixtureLifecycle` cited from the 2026-09-25 investigation). The 2026-09-25 census saw only databases `antiphon`, `postgres`, `template0`, and `template1` on the dev instance.
- The messaging service uses a different key, `ConnectionStrings:Messaging` (`src/Antiphon.Messaging.Service/Program.cs:17`). It is not the production Antiphon database. The same 2026-09-25 census did not see a second application database.
- Desktop scripts that `docker exec` into `antiphon-postgres` by name: `dev-aspire.ps1`, `dev-backup.ps1`, `dev-restore.ps1`, `scripts/deploy-local.ps1`, `scripts/measure-session-state-cache.ps1`, `scripts/reap-orphaned-pty-hosts.ps1`, `scripts/codex-boot-census.ps1`, `scripts/bootstrap-check.ps1`.

The self-contained stack in root `docker-compose.yml` is a different Postgres: service `postgres`, volume `pgdata`, hostname `postgres` on the compose network, password from `POSTGRES_PASSWORD`, no host port. `docker-compose.server2-runner.yml` is the only standing Antiphon process on server2 and does not include that service. `docs/docker-stack.md:869` states that (CARD-1221 still cites the same sentence at `:771`; the line has moved).

### Size and footprint (not measured today)

This session could not run `docker stats` or `psql` against the desktop container. Stored measurements:

| When | What | Value |
|---|---|---|
| 2026-09-25, 180 s window | Container CPU | mean 425.77% (about 4.3 cores), range 319–519% |
| 2026-09-25 | Container memory | 766–811 MiB, Docker limit reported 7.756 GiB |
| 2026-09-25 | `TranscriptEntries` total | 247.93 MiB, about 377,647 live rows. Other large tables: Alerts 40.48 MiB, land notifications 34.48 MiB, queued messages 28.76 MiB. 95 public tables. Database total bytes were not recorded |
| 2026-10-08, three instantaneous samples | Container CPU | 26.5%, 42.9%, 6.0% (`docs/investigations/2026-10-08-card-1121-receipt-scan-sample.md`) |
| 2026-10-08, 72.6 s | Receipt scans | 180 calls, 223,965 rows, 11,963 ms execution. Means 80.7 ms (two-kind) and 45.1 ms (one-kind). About 3,080 Text rows/s |

Transcript retention default is 7 days (`server/Application/Settings/RetentionSettings.cs:14`). That caps transcript growth if the sweep is running. It does not cap every table (tasks 180 days, sessions 90, queued messages 30). Current disk size, current CPU, and the biggest tables today are a desktop follow-up.

On 2026-09-25 `shared_preload_libraries` was empty and `pg_stat_statements` was not installed, despite the compose command that now requests it. Whether the live container was recreated after that compose change is unmeasured.

## 2. Latency and topology

Three paths exist in the docs. Only two were measurable from this container, and neither is a same-LAN desktop measurement.

Historical addresses, still the ones that answered today (CARD-0604 plan, 2026-09-22; CARD-0545 plan, 2026-09-17):

- server2 Tailscale `100.93.77.126`, LAN `192.168.4.34`.
- Desktop Tailscale address used in that plan: `100.79.51.37`. Direct `http://100.79.51.37:17202` was refused then. Phone-home origin is `https://antiphon.desktop.codeperf.net` (Caddy + Cloudflare). That origin returned 200 today.

TCP connect from this container, three attempts, 3 s timeout, around 2026-10-21 13:40Z. No payload was sent.

| Target | Result |
|---|---|
| `100.79.51.37:17202` (desktop AppHost, historical) | timeout, all three |
| `100.79.51.37:443` | open, min 150 ms, median 195 ms |
| `100.79.51.37:17280` (published Postgres) | **open**, min 72 ms, median 127 ms |
| `100.93.77.126:5432` and `192.168.4.34:5432` | connection refused (32 ms and 2 ms) |
| `100.93.77.126:5433` | open, min 0.34 ms (hairpin to this host) |
| `192.168.4.34:5433` | connection refused |
| `100.93.77.126:22` and `192.168.4.34:22` | open, about 0.3 ms (hairpin) |
| `100.93.77.126:8000` | open, one sample 23 ms; `GET /api/version` returned **404** |
| Cloudflare `antiphon.desktop.codeperf.net:443` | TCP connect 2.6–18 ms to the edge; time to first byte 173–220 ms |

`ping` is not installed here and `Test-Connection` is unsupported, so there is no ICMP figure. Connect time is the RTT proxy (one handshake).

What that means for the hot paths:

- CARD-0698's multi-core sequential scans were the 2026-09-25 mechanism. By 2026-10-08 the container was no longer at that CPU, and the expensive remaining work was repeated receipt scans of frozen transcripts: about 2.5 large SELECTs per second, each returning on the order of 850–1,500 `Text` rows, with execution already 45–81 ms (`docs/investigations/2026-10-08-card-1121-receipt-scan-sample.md`).
- CARD-0701 caches queue and working-state bits in the server process. UI polls (agent list about 5 s, working badge about 3 s) hit the server, not Postgres, once the cache is warm (`docs/investigations/2026-10-04-card-0701-investigation.md`). Residual durable receipt queries still hit Postgres. The 500 ms figure in that note is delivery-verification polling (`SupervisionSettings` `DeliveryVerificationSettings.PollIntervalMs` is now at `server/Application/Settings/SupervisionSettings.cs:323`); it is not by itself a Postgres round trip.
- Adding the measured 72–127 ms handshake to each receipt call is about 180 × 70 ms ≈ 13 s of extra wait per 72 s window, on top of the 12 s of execution already measured, before counting the bytes of those Text rows. Small indexed queries that are a few milliseconds locally become RTT-bound. Connection count is not the problem (14–16 sessions versus `max_connections` 100).

**DB-only move.** Viable if the desktop can reach server2 Postgres in well under 10 ms (same LAN, not the Tailscale path measured here) **or** the fat receipt scans are gone before cutover. It is a poor default on the path measured today. Cloudflare is the wrong transport for Postgres: the edge handshake is fast and the origin response is slower than Tailscale, and the current tunnel is HTTPS to the control plane, not a TCP database port.

**Control plane.** Stays on the desktop for this card. server2's standing process is the phone-home runner only. CARD-1221 (Backlog, Decide) is the open contract for a full Linux production deployment. Root `docker-compose.yml` still disables interpreters, the distiller, the Hangfire worker, census, schedules, the channel bridge, and digest (`docker-compose.yml:41-49` at the lines CARD-1221 cited; those keys are present in the current file at `:41-49`). `docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md` is not in this checkout. CARD-1221's body carries that report's conclusion: the base stack is not a decision that the desktop control plane has moved. Moving the server with the database would avoid the RTT, and it is out of scope until CARD-1221 says which services must move with it.

The published desktop port 17280 accepting TCP from this runner is a current exposure, because the dev password is committed. This task did not authenticate.

## 3. What server2 already has

Measured from inside the runner container on 2026-10-21. The nested dockerd is not the host dockerd (`docker-compose.server2-runner.yml` states the host daemon never sees session containers, and no host socket is mounted).

| Check | Result |
|---|---|
| CPUs visible | 24 |
| Memory | `MemTotal` 132,014,068 kB (about 126 GiB), `MemAvailable` 108,014,608 kB (about 103 GiB). No cgroup memory cap is visible, which matches the build-slot comment in `docker-compose.server2-runner.yml:65-71` (floor 16 GiB available) |
| Disk | `/work`, `/state`, and `/tmp` are `/dev/mapper/ubuntu--vg-ubuntu--lv` ext4, 705 G size, 228 G used, **449 G available** (34%). The container root overlay reports the same numbers |
| Nested Docker | 27.5.1, 0 containers, 0 volumes, images only `postgres:16-alpine` and `testcontainers/ryuk` (test leftovers, not a standing database) |
| Host TCP 5432 | refused on both the Tailscale and LAN addresses |
| Host TCP 5433 | open on the Tailscale address only, sub-millisecond hairpin. Matches the 2026-09-17 record of Windmill's `windmill_db` published on `100.93.77.126:5433` (`docs/superpowers/plans/2026-09-17-card-0545-independent-nightly-watchdog-plan.md`). LAN 5433 is refused, so that publish is address-specific |
| Windmill HTTP | Port 8000 on the Tailscale address accepts TCP. `GET /api/version` returned 404, so this session did **not** confirm the 2026-09-16 Windmill version. No Windmill token was used |
| `rclone`, `tailscale`, `pg_dump`, `psql`, `ufw` | not installed in this container |
| OneDrive | no sync directory in this container. The repo mentions OneDrive once, as a process ruled out of a file lock (`docs/superpowers/plans/2026-09-01-card-0308-pty-kill-process-tree-plan.md`). No sync path is recorded |

Historical host facts that this session could not re-list (host `docker ps` is not visible here): Ubuntu 18.04, host Docker 24.0.2, `ufw` inactive, iptables INPUT ACCEPT, Windmill compose at `/home/mc/docker/windmill` with `windmill_server`, workers, and `windmill_db`. Disk free in that 2026-09-17 note was 71 GB; the LV mounted into this container today has 449 GB free. Those can both be true if they were different mounts, so the follow-up on the host should run `df` on the Docker data root.

What a new Antiphon Postgres on server2 would need, and does not have:

- A **host** compose service beside `antiphon-runner`, not a container on the nested daemon. Nested data lives on the `dind-data` volume and dies with runner recreation.
- Its own volume. Do not reuse `windmill_db` or port 5433.
- A port bound to the Tailscale address only, the way 5433 is bound today. Not `0.0.0.0`, and not the desktop's 17280.
- A password that is not the committed dev password, supplied through the custody path in `docs/agent-credentials.md` (user-secrets or an operator-held env file). Nothing in this card should print it.
- Firewall: 2026-09-17 said `ufw` was inactive and INPUT was ACCEPT. Re-check on the host before publishing. The desktop must be able to open that Tailscale port; the reverse direction to desktop `:17202` still times out, which does not by itself block a new inbound port on server2.
- Memory: the 2026-09-25 container used about 0.8 GiB. This host had about 103 GiB available at the sample, so capacity is not the constraint. Disk: hundreds of MiB to low GiB against 449 GiB free is not the constraint either, once the current database size is measured.

## 4. Snapshot recommendation

What exists today is a manual desktop script, not a schedule.

- `dev-backup.ps1` runs `pg_dump` inside the compose service as plain SQL (`--no-owner --no-acl`), zips it, writes `<repo>/backups/antiphon-<timestamp>.sql.zip`, and keeps 14 files (`dev-backup.ps1:1-50`).
- `backups/` is gitignored (`.gitignore:27`).
- `dev-restore.ps1` drops and recreates the `antiphon` database in that same compose project (`dev-restore.ps1:34-66`). That is a production restore, not a drill.
- No `pg_basebackup`, `archive_command`, or OneDrive path exists in the repo. The card's own search (backup, `pg_dump`, snapshot, OneDrive, pgdata) found no backup card. This search agrees: nothing schedules `dev-backup.ps1`.

Recommendation for Plan:

| Choice | Recommendation |
|---|---|
| Format | `pg_dump -Fc` (custom format, compressed, `pg_restore`). Plain SQL is what the script does now; it is larger and the restore is a streamed script. Base backup plus WAL is the wrong tool for a daily snapshot: it needs `archive_command`, continuous archive disk, and a same-major-version physical restore. Use it only if someone later asks for point-in-time recovery |
| Where it runs | On the host that has the data, so the dump does not cross the 70 ms path. While the database stays on the desktop, that is the desktop |
| Scheduler | Windmill, not in-app Hangfire. Hangfire storage is in-memory and the worker is off in the Linux stack, so a daily fire depends on the desktop server process being up at that minute. Windmill already owns cross-machine jobs (`u/lndcobra/antiphon_nightly_tests` on the desktop worker; `u/lndcobra/antiphon_host_cleanup_server2` for server2 host roots, `docs/host-cleanup.md` plan line 62). A new script there matches that pattern. Do not register it inside the runner container |
| Destination | The desktop OneDrive sync folder, once a desktop task records the real path. The job writes an encrypted file into that folder and the desktop sync client uploads it. server2 does not need a OneDrive login for that shape. `rclone` is not installed here and was not authenticated. If the database later moves, the server2 job copies the encrypted file to that same desktop folder over the existing SSH path |
| Encryption | Encrypt before the file reaches the sync folder. Transcript text is plaintext in the database. API keys are Data Protection ciphertext (`server/Infrastructure/Security/DataProtectionApiKeyProtector.cs`); the key ring is a filesystem path (`AgentTui__KeyRingPath` in `docker-compose.yml:38`), not a row in the dump. A dump therefore exposes transcripts and does not, by itself, restore those secrets. The operator holds the backup key outside the dump and outside OneDrive |
| Retention | 14 daily encrypted files, the same count `dev-backup.ps1` already uses, kept off the Docker volume. Add weeklies only if the operator wants them; the repo does not require them |
| Restore drill | A scratch `postgres:16` container and a scratch volume. `pg_restore`, then compare `__EFMigrationsHistory` to the source, then boot a server pointed at the scratch port and read `/health`. Delete only the scratch volume. Do not point `dev-restore.ps1` at `antiphon_pgdata` |
| Expected size and time | Unmeasured today. The 2026-09-25 largest tables were a few hundred MiB combined. A custom-format dump of that footprint is minutes on a local socket, not hours. Plan should treat "current `pg_database_size`" as a gate before promising a window |

Taking the snapshot does not require moving the database. It removes the "only copy is `antiphon_pgdata` on the desktop" risk even if the move waits.

## 5. Cutover outline, downtime, rollback, hard stops

Do this only after Plan, and only if a desktop measurement shows a path under about 10 ms or the receipt-scan volume has dropped. Otherwise stop after the snapshot job.

1. Operator agrees the window. No volume is deleted in any step.
2. Quiesce writers. Stop new dispatch. Let an in-flight land finish. The repository mutation lease is an in-process file lock (`server/Infrastructure/Git/RepositoryMutationLease.cs` holds a `FileStream`), so it dies with the server process rather than as a database row. Stop the desktop AppHost so the pool closes. Runners keep their sessions but cannot commit through a stopped server.
3. `pg_dump -Fc` from the desktop container while nothing is writing. Keep that file even after the restore succeeds.
4. On the server2 **host**, start a new `postgres:16` service with a new volume, `pg_stat_statements` if the desktop container actually uses it, and the port bound to `100.93.77.126` only. Restore with `pg_restore`. Do not start the desktop server against it yet.
5. Operator updates the desktop connection config (the two appsettings keys and any user-secret override) to the Tailscale address and port, still without logging the password. Restart AppHost.
6. Verify `GET /health`, `GET /api/version`, one dispatch, and one land. Compare migration history and a few row counts to the dump.
7. Leave the old container stopped and `antiphon_pgdata` untouched.

Rollback is step 5 in reverse: point the config back at `localhost` port 17280, start the old container, restart AppHost. The old volume was never written after the dump if step 2 held. If any write landed on the new database, those writes are not on the old volume; rollback then needs an explicit choice to drop them or to dump them back. That is why step 2 has to be real.

Downtime is the dump, the restore, and the AppHost restart. Budget **30 minutes** until the current size is known. The September footprint says the copy itself should be a few minutes. Sessions on runners stay up but the board is frozen while the server is down.

Hard stops, operator only:

- Deleting, recreating, or `docker volume rm` of `antiphon_pgdata`.
- Choosing, storing, and rotating the database password off the committed dev value. This includes closing the current Tailscale exposure of port 17280 if the operator wants that before any move.
- Stopping production AppHost for the cutover window.
- OneDrive path confirmation and any `rclone` or cloud login.
- Publishing a new host port, and confirming host firewall state.
- Deciding to move the control plane (CARD-1221), if the latency gate fails and someone still wants the database off the desktop.

## Unmeasured gaps (desktop follow-up)

1. `docker stats` once for `antiphon-postgres` (CPU and memory now).
2. Read-only `pg_database_size` and the largest tables (`TranscriptEntries` and the rest). No credential in the output.
3. Whether `pg_stat_statements` is loaded in the live container.
4. The desktop OneDrive sync directory, if any.
5. From the desktop: TCP connect time to `192.168.4.34` and to `100.93.77.126` on a closed high port (RTT only). This session measured the reverse direction.
6. On the server2 host, not inside the runner: `docker ps` and `docker volume ls`, to confirm there is still no unpublished loopback Postgres. Port 5432 is already refused on the Tailscale and LAN addresses.

## Remaining uncertainties

- Desktop and server2 may still share LAN `192.168.4.0/24`. If they do, desktop→server2 RTT can be about 1 ms and the DB-only move becomes reasonable. Nothing in this session proves the desktop's route.
- Text-row bytes per receipt call were not measured, so the bandwidth term on top of the 70 ms RTT is unknown. The row counts are known.
- Windmill's HTTP API was not identified (port 8000 returned 404 for `/api/version`). The `:5433` publish is the evidence that the Windmill database container from the 2026-09-17 note is still up. Plan should re-check the Windmill UI or an authenticated route before binding a new schedule to it.
- Host Docker data-root free space might differ from the 449 GB LV figure if Docker uses another mount. The follow-up `df` covers that.

## Not done, noted

No compose service, Windmill schedule, dump, restore, password rotation, firewall change, or volume operation was designed as a patch or run. Plan should write the snapshot job and the latency gate before any cutover.
