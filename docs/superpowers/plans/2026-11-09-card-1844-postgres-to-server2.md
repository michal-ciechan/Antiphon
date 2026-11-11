# CARD-1844: move the AppHost Postgres from the desktop to server2

Date: 2026-11-11. Plan task `17db3443` on `feat/card-task-17db3443`, base `3fd231a2e498e463443079c40cf38711c0be8504`. Read-only on every host. No volume, container, or live database was touched. No secret is recorded here.

The same text is at `docs/superpowers/plans/card-1844-postgres-to-server2.md`.

## Answer

AppHost must not start or ensure the server2 Postgres container. It already does not start the desktop one. `Antiphon.AppHost/Program.cs:19-25` calls `AddConnectionString("DefaultConnection")` and `Program.cs:66-68` passes that resource to the server with `WithReference`. The desktop container is `docker-compose.dev.yml` project `antiphon`, service `postgres`, container `antiphon-postgres`, volume `antiphon_pgdata`, host port 17280, `restart: unless-stopped` (`docker-compose.dev.yml:13-41` and `:77-78`).

The server2 container already has an owner: host-daemon compose project `antiphon-server2-postgres`, volume `antiphon-server2-postgres_pgdata`, publish `${ANTIPHON_POSTGRES_BIND_ADDRESS}:5432` (`docker-compose.server2-postgres.yml:1-47`). `scripts/deploy-server2-postgres.ps1` defaults to `-WhatIf`. `-Execute` runs only on the server2 host daemon. `restart: unless-stopped` on that daemon is the ensure. CARD-1406 is the card for that service. It is in Review at plan time, and the files are already on this master base. CARD-1407 (Done) is the operator window that points the AppHost user secret at the new listener and stops the desktop container without removing `antiphon_pgdata`.

What this card adds is the contract those two cards leave implicit, plus the desktop health check that would otherwise stay glued to port 17280 after the secret moves.

## Intended behaviour

### Before

```text
Docker Desktop
  project antiphon
    antiphon-postgres  --publish 17280-->  volume antiphon_pgdata
AppHost (Aspire, desktop)
  AddConnectionString("DefaultConnection")     no container lifecycle
  user-secrets id aspire-antiphon-apphost, else Antiphon.AppHost/appsettings.json:10
  WithReference --> server process environment
server Program.cs:124-137 UseNpgsql
  Hangfire:Storage unset --> InMemory (HangfireSettings.cs:23-33, Program.cs:994-1006)
session runner                                      no Postgres reference
Antiphon.Tests                                      own postgres:16-alpine Testcontainer
server2 host daemon                                 phone-home runner only, as production
                                                    host Postgres compose exists, window not run
```

A dead Docker Desktop takes `antiphon-postgres` with it. The server then has no database. The card's 2026-11-10 ~16:48Z engine death is that failure. Qualification chunks `antiphon-all-group-115` through `118` are named as the Docker-outage victim list by `docs/investigations/2026-11-10-why-missed-e2e-split-7-land-delivery.md:18`. A later chunk in that note started Testcontainers database `antiphon_e2e`. Those chunks are not clients of `antiphon_pgdata`. Moving the production database leaves qualification Testcontainers on Docker Desktop.

### After (topology A2, not topology B)

```text
server2 host dockerd
  project antiphon-server2-postgres
    postgres --publish <tailnet>:5432--> volume antiphon-server2-postgres_pgdata
    restart unless-stopped, mem_limit 4g, cpus 4
AppHost (still on the desktop)
  same AddConnectionString
  user secret ConnectionStrings:DefaultConnection host is the tailnet address, port 5432
  log line is host and port only
  TCP reachability is a check, not a start
desktop antiphon-postgres
  docker compose stop, status exited, volume antiphon_pgdata still listed
tracked appsettings
  Host=localhost;Port=17280 on both AppHost and server files
dev clone and tests
  local compose and Testcontainers, unchanged
```

One writer. The desktop AppHost server is that writer until CARD-1744. `docker-compose.server2-server.yml` joins network `antiphon-server2` and uses `Host=postgres` only after that later window (`docs/docker-stack.md:1732-1736`). CARD-1742 staging keeps scratch volume `antiphon-server2-staging_pgdata` and does not attach to the CARD-1406 service until CARD-1406 is Done (`docs/docker-stack.md:1720-1724`).

### Files and lines Code changes

| Slice | File | Lines | Change |
|---|---|---|---|
| S1 | `Antiphon.AppHost/Program.cs` | 19-25, and a log beside 126-139 | State that AppHost does not start the server2 container. Log `Database endpoint {Host}:{Port}` from the configured connection string. |
| S2 | `scripts/lib/DatabaseEndpoint.ps1` | new | Return host and port only. |
| S2 | `scripts/bootstrap-check.ps1` | 13-15, 379-452 | `-DatabaseProbeOnly` and `-ConnectionString`. Remote mode probes the configured host and port. Loopback port 17280 keeps today's container and volume checks. |
| S3 | `docs/docker-stack.md` | after 1718 | CARD-1844 subsection: owner, health check, recycle boundary. |
| S3 | `docs/bootstrap.md` | 97-101 | One sentence: a non-loopback AppHost secret is healthy when that host and port accept TCP. A fresh clone still means container `antiphon-postgres` on 17280. |

### What stays unchanged

- `docker-compose.dev.yml`, `docker-compose.server2-postgres.yml`, `docker-compose.server2-postgres-drill.yml`, `docker-compose.server2-staging-server.yml`, `docker-compose.server2-server.yml`.
- `Antiphon.AppHost/appsettings.json:9-11` and `server/appsettings.json:2-4`. Both stay `Host=localhost;Port=17280` with `No Reset On Close=true`. The committed dev password stays in those existing files and is not copied into this plan.
- `scripts/cutover-postgres-server2.ps1`, including the secret write at 504-541 and the desktop stop at 860-882.
- `scripts/deploy-server2-postgres.ps1` and the temp-retirement allow list at `scripts/deploy-server2.ps1:1249`.
- `server/Program.cs` Npgsql and Hangfire registration. `server/appsettings.json:185-188` has no `Hangfire:Storage`, so storage stays InMemory.
- Session runner. No `DefaultConnection` under `src/Antiphon.SessionRunner`.
- EF migrations. Latest on this base is `server/Migrations/20261110213738_Card1294ShadowCohort.cs`. No new migration.
- Testcontainers image `postgres:16-alpine` in `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:26-27`. Tests do not use port 17280 or the server2 listener.
- Windmill's database and port 5433.
- CARD-1008 recycle targets. `docs/docker-stack.md:1169-1170` already says volume recycle and temp retirement are unchanged by where Postgres sits.

## Ground truth

| The card assumes | What the code does |
|---|---|
| AppHost starts Postgres | It does not. Compose starts the desktop container. AppHost only injects the connection string (`Program.cs:19-25`, `docker-compose.dev.yml:1-9`). |
| Moving the database is a new design | CARD-1401 is in Review and is the parent. CARD-1406 (Review) is the host service. CARD-1407 (Done, landed) is the window script. The window has not been run: docker-stack records A2 as not yet executed (`docs/docker-stack.md:967`). |
| server2 root is 91% full | Measured here on 2026-11-11 from the runner container: `/dev/mapper/ubuntu--vg-ubuntu--lv` 705G size, 611G used, 66G available, 91%, mounted on `/work`, `/`, and `/tmp`. The host dockerd data-root is not this container's daemon. Nested dockerd is the wrong daemon. Host free space is still an operator measurement. |
| The database is about 2 GB | CARD-1831 records `antiphon` in `antiphon-postgres` as 2,099,575,831 bytes on 2026-11-10. This plan did not remeasure it. The card's "~2 GB RAM" is resident memory, a different number, also not remeasured here. |
| Hangfire, the session runner, nightly, and Windmill use this database | Hangfire storage is InMemory. The session runner does not open it. `scripts/nightly-run.ps1` has no 17280 dependency. Windmill keeps port 5433. Nightly and qualification Postgres is Testcontainers. |
| Backups exist | CARD-1831 (Review) says no daily job runs. CARD-1405's encrypted snapshot script exists. The job must follow the database after the window. |
| A migration is required | No schema change. Restore carries `__EFMigrationsHistory`. |

## Decisions

### D-1. AppHost checks the configured endpoint and does not start a container

Reason: the 2026-11-10 failure is a desktop Docker engine death. A database whose lifecycle is an AppHost `docker compose` or SSH call dies with the desktop again, and a dead desktop cannot start it. `deploy-server2-postgres.ps1` already refuses `-Execute` off the host daemon.

Rejected:

- Aspire `AddPostgres` or any DCP-managed container. `Program.cs:23-24` already records stale `DefaultConnection` containers and HNS hangs, and that container would be created by the desktop engine.
- An Aspire external resource beyond `AddConnectionString`. That call is the external resource.
- AppHost SSH or a docker context that runs compose on server2. That puts a production daemon credential in the desktop process and lets an AppHost restart reconcile the production database.
- A tracked connection-string edit to a tailnet address. Clones and `dev-start.ps1` would then require server2. Production override stays the user secret CARD-1407 already writes (`aspire-antiphon-apphost`, `Antiphon.AppHost.csproj:10`).

### D-2. Dev and tests keep a local Postgres

Tracked appsettings and `docker-compose.dev.yml` stay the local database. `Antiphon.Tests` keeps its assembly Testcontainer (`TestDbOperations` in `TestDbFixtureLifecycle.cs`). A fresh clone's bootstrap-check still requires `antiphon-postgres` and `antiphon_pgdata`. Remote mode runs only when the resolved AppHost connection string host is not loopback.

Rejected: pointing tests or the nightly checkout at the server2 listener. That shares one production writer with the desktop server and with whatever else can open the port.

### D-3. The staged rollout does not gain a new Postgres owner

| Piece | Owner after this plan |
|---|---|
| Create and restart the server2 database | `scripts/deploy-server2-postgres.ps1 -Execute` on the host, CARD-1406 |
| Scratch restore | `-RestoreDrill` plus the operator sequence in `docs/docker-stack.md:1691-1718` |
| Desktop secret, stop, rollback | `scripts/cutover-postgres-server2.ps1 -ConfirmWindow`, CARD-1407 |
| AppHost reachability wording and log | this card, S1-S2 |
| Volume recycle | CARD-1008, unchanged names |
| Staging server | CARD-1742 scratch database until CARD-1406 is Done |
| Production server process on server2 | CARD-1744, later, one writer, `Host=postgres` on network `antiphon-server2` |
| Daily backup | CARD-1831, must follow the database, not this card |

Rejected: folding the window, the host `-Execute`, or a backup job into this card's Code stage. Those are operator steps or other cards.

### D-4. Network boundary stays the tailnet bind

No TLS in compose or appsettings today, and this card adds none. The listener is a Tailscale CGNAT address (`deploy-server2-postgres.ps1:64-67` requires `100.64.0.0/10`; `-Execute` requires `tailscale ip -4`). Port 5432 on that address. Not `0.0.0.0`, not a LAN address, not 5433, not 17280.

The new cluster password is not the committed dev password. It lives in the untracked mode-0600 env file on the server2 host (`docs/docker-stack.md:1176`). The desktop stores the connection string in the AppHost user secret and nowhere else (`docs/docker-stack.md:1180`). `docs/agent-credentials.md` does not hold the database password. Code and the witnesses print host and port only.

The server process launched by AppHost receives the string through `WithReference`, which sets the child environment and outranks the server user secret `antiphon-server`. `dev-start.ps1` is the standalone path and stays on localhost:17280. After the window, a standalone server must not be a second writer.

### D-5. Desktop container stays stopped, volume stays

CARD-1407 phase 4 runs `docker compose stop postgres`, requires status `exited`, and requires volume name `antiphon_pgdata` still listed (`cutover-postgres-server2.ps1:860-882`). `restart: unless-stopped` does not start a container that was explicitly stopped when the daemon returns. Rollback phase 6 starts it again. Nobody deletes the volume. `restart-apphost.ps1` already preserves `antiphon-postgres` (lines 146 and 274) and is not given a new stop.

### D-6. Disk and latency gates stay operator measurements

This plan does not book the window. The operator measures, on the server2 host, `docker info` data-root and `df` of that path, and refuses `-Execute` when free space there is under 20 GiB. Twenty GiB is about ten times the 2026-11-10 database size and covers the image, a custom-format dump, the drill volume, and the ciphertext. Reclaim, if needed, uses CARD-1008's named runner volumes only.

The 30-minute downtime envelope from the CARD-1401 plan applies only when the database is at most 2 GiB and a scratch drill's dump, copy, and restore finish within 10 minutes. 2,099,575,831 bytes is under 2 GiB (2,147,483,648) by about 48 MiB. Dump elapsed time is still unmeasured. A fresh size over 2 GiB, or a drill over 10 minutes, means that envelope does not apply and the operator books a longer window.

Latency was not remeasured. The 2026-10-21 CARD-1401 investigation recorded a desktop-to-server2 handshake median of 3.83 ms on the LAN and 6.72 ms on Tailscale. The existing abort remains: a fresh desktop-origin Tailscale median above 20 ms stops the window. Every query pays that RTT. Historical receipt-scan execution was tens of milliseconds, so the extra handshake is visible and was already accepted for A2.

## Migrations

None. Do not add a file under `server/Migrations`. There is no `Down` to write. A restore that already contains `20261110213738_Card1294ShadowCohort` migrates nothing new on boot.

## Existing tests affected

None for migrations, public DTOs, routes, operation labels, remote script functions, or statement budgets. `tests/impacted-classes.json` has no row for `Antiphon.AppHost/Program.cs`, `scripts/bootstrap-check.ps1`, or `docs/docker-stack.md`. CARD-1782 selects nothing from this diff. The new witnesses are the script pins below.

`scripts/bootstrap-check.ps1` is human-invoked (`docs/bootstrap.md:101`). `verify-dev-stack.ps1` does not call it. A loopback secret keeps the current 17280 checks, so a dev desktop does not change.

## Verification design

Red-first, inside each slice, before the checkpoint row: commit the new witness, run it once, confirm it fails on this base, then commit the production edit. That red run is unlisted. Reason: `red-first witness before the fix`. The checkpoint row is the green run after the fix commit. A witness that cannot fail on the base is a stub.

| Id | Kind | Asserts | Mutant |
|---|---|---|---|
| V-1 | red on base | `scripts/test-apphost-postgres-owner.ps1` reads `Antiphon.AppHost/Program.cs`. It requires the sentence `AppHost does not start the server2 Postgres container.` and a log template `Database endpoint {Host}:{Port}`. It forbids `AddPostgres`, `docker context`, and a log argument that passes the connection string through. | M-1 delete the sentence or the template. M-2 log the raw connection string. |
| R-1 | regression, green on base | Same script. Both tracked connection strings stay `Host=localhost;Port=17280` and `No Reset On Close=true`. Neither file gains a `100.` host. | M-7 put a tailnet host in either appsettings file. |
| V-2 | red on base | `scripts/test-bootstrap-database-endpoint.ps1` invokes `bootstrap-check.ps1 -DatabaseProbeOnly -ConnectionString 'Host=100.64.0.2;Port=5432;Password=secret-value;Username=antiphon;Database=antiphon'`. Stdout is one line `database-endpoint host=100.64.0.2 port=5432 mode=remote`. The token `secret-value` is absent. Remote mode does not inspect `antiphon_pgdata`, does not require `antiphon-postgres`, and does not probe `127.0.0.1:17280`. The probe switch exits before docker and before any socket. | M-3 remote mode still inspects the local volume or probes 17280. M-4 the probe line contains the password. |
| V-3 | red until the switch exists; local behaviour preserved | Same script, connection string `Host=localhost;Port=17280;Password=secret-value;Username=antiphon;Database=antiphon`, expects `database-endpoint host=localhost port=17280 mode=local`. A second case with no `-ConnectionString` resolves the tracked AppHost appsettings to that same local line. | M-8 loopback starts reporting `mode=remote`. |
| R-2 | regression | The full script without `-DatabaseProbeOnly` is not invoked by the witness, so the checkpoint never contacts a database. Source pin: the 17280 inspect block remains the `mode=local` branch. | M-3 covers the remote branch deleting that local branch. |
| V-4 | red on base | `scripts/test-card1844-postgres-contract.ps1` requires `docs/docker-stack.md` to contain a `CARD-1844` heading and the sentences `AppHost does not start the server2 Postgres container.`, `scripts/deploy-server2-postgres.ps1` is the owner, and CARD-1008 does not remove `antiphon_pgdata` or `antiphon-server2-postgres_pgdata`. | M-5 delete those sentences. |
| R-3 | regression, green on base | Same script. `scripts/deploy-server2.ps1` allow list stays exactly `antiphon-runner-temp_work`, `antiphon-runner-temp_runner-tmp`, `antiphon-runner-temp_dind-data`, `antiphon-runner-temp_runner-state`. | M-6 add `antiphon_pgdata` or `antiphon-server2-postgres_pgdata` to that list. |

Doc-contract pins are V-1's sentence, V-4's sentences, and R-1's tracked connection strings.

### Checkpoints

Command rows. Lane is the script. No `-Runner`, no `-Platform`. Card platform is Any.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---:|
| CP-1 | S1 | n/a | apphost-owner | `pwsh -NoProfile -File scripts/test-apphost-postgres-owner.ps1` | V-1, R-1 | exit 0; V-1 and R-1 sentences hold; password text absent | n/a | 4 |
| CP-2 | S2 | n/a | bootstrap-endpoint | `pwsh -NoProfile -File scripts/test-bootstrap-database-endpoint.ps1` | V-2, V-3, R-2 | exit 0; remote and local probe lines; secret-value absent; no docker call | n/a | 6 |
| CP-3 | S3 | n/a | contract-pin | `pwsh -NoProfile -File scripts/test-card1844-postgres-contract.ps1` | V-4, R-3 | exit 0; CARD-1844 sentences present; temp volume allow list unchanged | n/a | 4 |

### Cost

Ordinary Code floor is 4 + 6 + 4 = 14 minutes, plus authoring. `-ExpectAbout` for the Code dispatch is that sum plus authoring time inside each 60-120 minute slice. No `totalTimeoutMinutes` override. The tool default `max(30, 2*sum+10)` applies.

## Performance budget

These rows are source and argument pins. They do not boot AppHost, a web host, a container, or a database. They do not migrate or seed.

| Item | Budget |
|---|---|
| CP-1 / CP-2 / CP-3 wall | 4 / 6 / 4 minutes, provisional. No measured p95 exists: the scripts are new. The first green checkpoint receipt is the sample. A later change tightens toward about 2x that p95. Until then the estimate is the budget, and it is labelled provisional. |
| Row deadline | Importer `DeriveRowMinutes` from those estimates. Each estimate is under 15 minutes, so 3x stays inside the 45-minute advisory and import emits no split warning. |
| Run wall | Sum 14 minutes. Under the R1 30-minute Debug threshold. No Debug task. |
| Phases | Parse and string compare only. Container start, readiness wait, data prep, web host, migration, snapshot, and teardown are not in these rows. Record them as absent, not as zero, if a timing sidecar is attached. External TCP, docker, and SQL are forbidden in the witness process. |
| Fixture lifetime | One process per script. No shared fixture, because there is no expensive setup (R3). |
| Recording | The checkpoint `CHECKPOINT` line and `report.json`. No new `ActivitySource`. |
| Timeouts | About 2x the provisional estimate via the derived row deadline (R5). Queue admission stays the checkpoint slot wait, separate from execution. |
| Category | Script and docs. No test-assembly category change. Downstream of `Program.cs` is the desktop AppHost process; the witness does not launch it. |

`repeat-proof: none required after green.`

## Slices

Each slice is one Code dispatch, 60-120 minutes, two commits (red witness, then fix), then its CP row. Do not run `-Execute`, `-ConfirmWindow`, or any docker command against a live daemon.

### S1. AppHost owner pin and endpoint log

Edit `Antiphon.AppHost/Program.cs` as in the file table. Parse `Host` and `Port` locally. Default port 5432 when `Port` is absent. Do not log the connection string, the username, or the password. Keep top-level statements; a local function is enough. No new project reference.

Witness: `scripts/test-apphost-postgres-owner.ps1`, ASCII. Commit it red, then commit the Program.cs edit.

### S2. Bootstrap-check follows the effective endpoint

Add `scripts/lib/DatabaseEndpoint.ps1`, ASCII, Windows PowerShell 5.1 safe. `Get-AntiphonDatabaseEndpoint` accepts one connection string and returns host, port, and mode `local` or `remote`. Loopback is `localhost`, `127.0.0.1`, or `::1` together with port 17280. Any other host, or another port, is `remote`. The returned object has no password property.

`bootstrap-check.ps1` gains `-ConnectionString` and `-DatabaseProbeOnly`. Probe-only prints `database-endpoint host=<host> port=<port> mode=<local|remote>` and exits 0 before docker. Without the switch:

- Resolve the string from `-ConnectionString` when passed, otherwise the AppHost user-secrets file for id `aspire-antiphon-apphost` when that file exists, otherwise `Antiphon.AppHost/appsettings.json`. Do not call `dotnet user-secrets list`. Read the secrets file, keep host and port, drop the rest.
- `mode=local` keeps lines 379-452 as they are, including the `antiphon-postgres` and `antiphon_pgdata` checks and TCP `127.0.0.1:17280`.
- `mode=remote` skips those three local requirements and TCP-checks the resolved host and port. The PASS/FAIL detail names host and port and does not name a password. Remedial text tells the operator to look at the host compose, not at `docker compose -f docker-compose.dev.yml up -d`.

Witness: `scripts/test-bootstrap-database-endpoint.ps1`. It supplies the fixture strings. It does not read the real user-secrets file and does not open a socket.

### S3. Doc contract

Add the CARD-1844 subsection to `docs/docker-stack.md` after the scratch-drill block, and the one bootstrap sentence in `docs/bootstrap.md:97-101`. The subsection states D-1 through D-3 in the words V-4 searches for. It does not change the CARD-1221 topology table: A stays the supported production deployment until the operator window, and A2 stays "not yet executed" until that window.

Witness: `scripts/test-card1844-postgres-contract.ps1`.

## Risks

| Risk | What holds it |
|---|---|
| Split brain if the desktop container starts again and something still uses localhost | Tracked files stay on localhost, so a missing secret means local. After a successful window the secret is set and the container is stopped. Rollback removes the secret and starts the container. Do not run `dev-start.ps1` against the tailnet database. |
| Second writer | Staging uses its own volume. CARD-1744 stops the desktop server first. Hangfire on the desktop is InMemory, so it does not take the CARD-1743 advisory lock. Do not set `Hangfire:Storage` to PostgreSql on a second process. |
| Recycle deletes the database | The allow list at `deploy-server2.ps1:1249` is four temp runner volumes. R-3 pins it. Drill teardown may remove only a name ending in `-drill_pgdata`. `down -v` on project `antiphon` or `antiphon-server2-postgres` is forbidden. |
| Disk | 91% on the LV this container can see, with 66G free. Host data-root may differ. Operator gate is 20 GiB free on the data-root before `-Execute`. |
| Size margin | About 48 MiB under the 2 GiB envelope on the 2026-11-10 figure. Remeasure before booking. |
| Qualification still needs Docker Desktop | Groups 115-118 are not fixed by this move. |
| CARD-1406 still in Review | Code for S1-S3 does not start a container. The operator does not `-Execute` until that review settles, unless the operator explicitly accepts the compose already on master `3fd231a2e498e463443079c40cf38711c0be8504`. |
| Secret in a log or a witness | V-1, V-3, and the probe switch forbid it. The cutover script already refuses to print the value. |

## Operator-only steps

Nothing in Code, Review, or this plan runs these. Nothing here runs on the desktop.

1. Wait until CARD-1406 is Done, or explicitly accept the master compose.
2. On the server2 host, record `docker info` data-root and `df` of that path. Stop if free space is under 20 GiB. Do not delete `antiphon_pgdata`, `antiphon-server2-postgres_pgdata`, or any other database volume to make room.
3. Create the mode-0600 env file outside the repo. Bind is the host `tailscale ip -4`. Image is a `postgres:16` digest, not Alpine, not a floating tag. Password is new.
4. `scripts/deploy-server2-postgres.ps1 -WhatIf`, then `-Execute` on the host. Empty cluster first.
5. Take or locate an encrypted CARD-1405 snapshot of the desktop database. Run the scratch drill in `docs/docker-stack.md:1699-1718`. Record dump, copy, and restore seconds and the dump size. Shred the plaintext. Remove only the drill volume.
6. Remeasure the desktop database size. Fresh Tailscale median from the desktop. Abort the 30-minute envelope when size exceeds 2 GiB, the drill exceeds 10 minutes, or the median exceeds 20 ms.
7. Book the window. Run `pwsh -File scripts/cutover-postgres-server2.ps1` with `-ConfirmWindow` from the main desktop checkout, not from a worktree. The script stops AppHost, dumps, restores, stops `antiphon-postgres`, sets the AppHost user secret, and restarts. Volume `antiphon_pgdata` stays.
8. Confirm `GET /api/version` and a query path that does not print rows. Confirm bootstrap-check remote mode after S2 is deployed. Confirm the desktop container status is `exited` and the volume is still listed.
9. Point CARD-1831's future job at the server2 database. Do not schedule it against localhost after the secret moves, and do not start it before the window.
10. Rollback is the cutover script's phase 6: start desktop Postgres, remove the AppHost secret, restart AppHost on the tracked localhost string. Leave both volumes in place.

## Owed natively on Windows

- `bootstrap-check.ps1` and `DatabaseEndpoint.ps1` stay ASCII so Windows PowerShell 5.1 can parse them. Checkpoints parse them with `pwsh` on Linux. A 5.1 parse is owed on the desktop and is not part of these rows, because this plan runs nothing on the desktop.
- The user-secrets file, `docker compose stop`, and `-ConfirmWindow` exist only on the production desktop. Code does not call them.
- AppHost itself is the Windows Aspire process. S1's log is covered by the source witness, not by launching AppHost.

## Platform

Read on 2026-11-11: `GET /api/runner-defaults` revision 2, a global runner id is set, `unresolvedReferences` 0, supported kinds Grok, ClaudeCode, Codex. `GET /api/session-runners` returned three runners: one `windows`, available and dispatch-eligible; one `linux`, available and dispatch-eligible; one with no platform, draining, stale, and not eligible. This plan does not pin `-Runner` or `-Platform`. The checkpoint lane is the command in the table.
