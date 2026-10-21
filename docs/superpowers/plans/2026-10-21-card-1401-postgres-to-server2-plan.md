# CARD-1401 plan: move production Postgres to server2 first

Date: 2026-10-21. Plan task `c4e4f277` on `feat/card-task-c4e4f277`.
Card: CARD-1401 (`38b2bce2-2c19-4002-91f3-af9076a13cf0`), board Antiphon.
Investigation: `docs/investigations/2026-10-21-card-1401-postgres-server2-and-snapshots.md` at `f94464fff0ef430e2a0ab142d84b6c7fb4fe0127` on `feat/card-task-760095f1` (not in this branch).
This session did not move data, restart a host, or read a password or connection string.

The operator decision stands: stage C moves the database to server2 before the control plane. The interim state is accepted: AppHost and the server stay on the desktop, Postgres moves to server2. Stage A (CARD-1403) and stage B (daily snapshots) run in parallel with preparing C. They are not gates for that preparation. The cutover window itself waits for one verified snapshot and one scratch restore. Stage D (AppHost, server, and land worker move to server2) is out of scope except that C must leave it possible. CARD-1221 remains the Linux deployment contract.

Desktop measurement task `5414c8f6` was supposed to leave facts in `C:\Users\lndco\AppData\Local\Temp\dbfacts.txt`. That path is not readable from this runner mirror. Every figure that file was meant to supply is a `{{TOKEN}}` below. The cutover window does not start until those tokens are filled from that file or a re-run of the same read-only measurements. Preparation cards can be filed before the file arrives.

## Decisions

### D-1. One quiesced `pg_dump -Fc`, then `pg_restore`

The cutover copy uses the same custom-format dump as the daily snapshot. The desktop server is stopped first, so the dump is a quiet point, not a live catch-up.

Rejected: logical replication. A publication and a slot shorten the final pause only if the database is large. They keep a second writer path open across the tailnet until promotion. Use them only if the size gate in D-16 fails.

Rejected: base backup plus WAL archiving. The card asks for a daily snapshot and a cutover, not point-in-time recovery.

### D-2. The interim state is the plan, not a veto

The investigation's "stop unless desktop-to-server2 is under about 10 ms, or the fat receipt scans are gone" is superseded by the operator decision on 2026-10-21. CARD-1403 stays parallel. It is not a precondition of the move.

The 72-127 ms figure is the server2-to-desktop path measured on 2026-10-21 (TCP connect to the published desktop Postgres port, min 72 ms, median 127 ms). It is not a measurement of desktop-to-server2. `{{DESKTOP_TO_SERVER2_TAILNET_MS}}` and `{{DESKTOP_TO_SERVER2_LAN_MS}}` come from task `5414c8f6`. The window uses the desktop-to-server2 tailnet number as the expected per-round-trip cost. A result inside the already accepted band is not a reason to abort. A path that is much worse than that band is a pre-window stop (D-11), because the operator accepted the measured band, not an unmeasured worse one.

### D-3. A host compose service beside the runner, on the host daemon

New project name `antiphon-server2-postgres`. New file `docker-compose.server2-postgres.yml`. The Docker volume is `antiphon-server2-postgres_pgdata`. It is not `antiphon_pgdata`, not the runner's `dind-data`, and not Windmill's database volume.

Rejected: a container on the nested dockerd inside `antiphon-runner`. That store dies when the runner is recreated, and the host daemon never sees those containers (`docker-compose.server2-runner.yml` header).

Rejected: reuse of Windmill's database or its published port. Host TCP 5433 on the server2 Tailscale address was open on 2026-10-21 and matches the recorded Windmill database publish. LAN 5433 was refused.

The service is started with the host Docker socket, from a directory on the server2 host that is a sibling of the Windmill compose directory. The 2026-09-17 note put Windmill at `/home/mc/docker/windmill`. Pre-flight re-lists that parent and records it as `{{SERVER2_COMPOSE_PARENT}}`. The proposed directory is `{{SERVER2_COMPOSE_PARENT}}/antiphon-postgres`. The runner container must not be the process that creates this service.

### D-4. Tailnet-only plaintext, no committed address

Postgres listens on the server2 Tailscale IPv4 only. The compose port binding is `${ANTIPHON_POSTGRES_BIND_ADDRESS:?}:5432:5432`. Compose refuses to start when the variable is empty, so a forgotten file cannot fall through to `0.0.0.0`. The address is read on the host at pre-flight (`tailscale ip -4`) into an untracked env file mode `0600`. It is not committed. The investigation's measured addresses stay evidence in the ground-truth table; they are not the config.

Transport encryption is the tailnet (WireGuard). Postgres itself stays plaintext. Nothing in the repo sets `SslMode` today, and the desktop listener is plaintext.

Rejected: Cloudflare or any public port. The current tunnel is HTTPS to the control plane, not a TCP database port.

Rejected: publishing on the LAN interface, even if `{{DESKTOP_TO_SERVER2_LAN_MS}}` is about 1 ms. The operator decision is tailnet-only. A wider publish is a separate operator decision (human-only).

Rejected: provisioning a server certificate and turning on Npgsql TLS in this cutover. It adds a second secret and a second failure mode on the day the host changes. The tailnet bind is the transport control.

### D-5. Port 5432 on that address

Host TCP 5432 was refused on both the server2 Tailscale address and the LAN address on 2026-10-21. Pre-flight checks that this is still true before the bind is added.

Rejected: the desktop's 17280. Rejected: 5433, which is Windmill's publish. If 5432 is no longer refused, the window stops. The plan does not hunt for a spare port on the day.

### D-6. Same image major as the desktop container, digest-pinned, with the same preload

Desktop compose uses `postgres:16`, not Alpine (`docker-compose.dev.yml`). Tests use `postgres:16-alpine`; this service does not. At pre-flight, record the running desktop image id and digest into `ANTIPHON_POSTGRES_IMAGE`. The compose file requires that variable. Do not let server2 float on a newer `postgres:16` pull than the desktop container.

The service command matches the desktop preload: `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`. Migration `20260925120000_EnablePgStatStatements` creates the extension. A restore of a database that has the extension fails closed without the library. Whether the live desktop container actually has it loaded was false on 2026-09-25 and has not been remeasured (`{{PG_STAT_STATEMENTS_LOADED}}`). The target preloads it either way. The verify phase compares extension name and version to the source and records a mismatch as a stop, not a surprise to fix mid-window.

Healthcheck matches the desktop file: `pg_isready -U antiphon -d antiphon`, interval 5s, timeout 5s, retries 12, start period 10s. Restart policy `unless-stopped`.

### D-7. New password on the new cluster only; tracked appsettings stay on localhost

The new cluster's `POSTGRES_PASSWORD` comes from the untracked env file on the server2 host. It must not be the committed dev password, and it must not be printed by any script.

The live desktop process gets its string from AppHost. `Antiphon.AppHost/Program.cs` calls `AddConnectionString("DefaultConnection")` and `WithReference(postgres)` on the server project. The value that wins is AppHost configuration, user-secrets id `aspire-antiphon-apphost`, key `ConnectionStrings:DefaultConnection`. The server project's own user-secrets id is `antiphon-server`. Production is the AppHost path, so the cutover writes the AppHost secret only.

The override keeps `No Reset On Close=true`, database `antiphon`, user `antiphon`, the new password, the tailnet host, and port 5432. Scripts accept the secret from the operator store and do not echo it. Tracked `server/appsettings.json` and `Antiphon.AppHost/appsettings.json` stay on `localhost` port 17280. Rollback is deleting that user-secret key and restarting. A fresh clone still talks to a local desktop database.

The desktop volume keeps its current password. Rollback therefore still authenticates with the tracked string. Rotating that desktop password is not part of the move.

When a rotation is required:

- Creating the new server2 password is required before the new listener starts. That is an operator action.
- Rotating the desktop password is required only if port 17280 remains published. This plan stops the desktop container instead, and leaves the volume in place.

Rejected: committing the tailnet host or the new password. Rejected: putting the new password into `antiphon-server` user-secrets as well. A standalone server start would then write the new database while AppHost was assumed to be the only client. With only the AppHost secret set, a standalone start keeps using localhost, and D-15 makes that fail closed.

### D-8. Do not add pool knobs on cutover day

`No Reset On Close=true` is already in both tracked connection strings and in root `docker-compose.yml` (CARD-0712, landed `69ffaffaf`). It already removes the `DISCARD ALL` round trip when a connection returns to the pool. The cutover override must keep the flag. Confirm before the window with `GET /api/diagnostics/session-state`. That route returns `pool.Pooling`, `pool.NoResetOnClose`, `pool.MaxAutoPrepare`, and `pool.Multiplexing`, and it does not serialize the connection string (`DiagnosticsEndpoints`).

Expected today: `Pooling` true (Npgsql default; the string does not disable it), `NoResetOnClose` true, `MaxAutoPrepare` 0, `Multiplexing` false. A different `Pooling` or `NoResetOnClose` stops the window until someone explains it. Do not "fix" it inside the window.

`Max Auto Prepare` stays 0. CARD-0712's audit found no production `Prepare` and left auto-prepare off. Turning it on removes a Parse round trip per statement after warmup. Each execution still costs one round trip. Enabling it on the same day as the move confounds the rollback trigger.

Command batching is not a connection-string switch. It is CARD-1403 (`NpgsqlBatch` and fewer round trips at the call sites). It is not available as an immediate config change.

Rejected: `Multiplexing=true`. The server uses EF Core transactions. Multiplexing is a different driver mode, and this cutover will not discover its interactions with the pool.

What actually hurts is one round trip per command at `{{DESKTOP_TO_SERVER2_TAILNET_MS}}`. Pooling avoids a new TCP handshake per command. It does not remove the command round trip. No remaining connection-string flag does that.

### D-9. Stop with `-StopOnly`, restart only after the repoint, on the rc tip

`scripts/restart-apphost.ps1` always relaunches (`dev-aspire.ps1` after the kill). Using the full script as the quiesce would start writers again on the old database before the dump. C2 adds `-StopOnly`. That mode runs the script's existing graceful stop and the port free that follows it, then exits before the relaunch.

The graceful stop is the one the restart script already uses: `POST /api/operator/shutdown` with the operator token, reason set, then wait for the returned server PID (`Invoke-AppHostGracefulStop` in `scripts/apphost-common.ps1`). Default wait stays `-GracefulStopTimeoutSec` 20. The token is never printed.

`OperatorShutdownCoordinator` drains in-flight launches. It does not drain the land queue. `AgentTasks.LandRequestedAt` is the durable queue and a boot sweep resumes it (`docs/session-runtime-invariants.md`). A pending land in the dump becomes a land against the new database after restart. Phase 1 therefore requires `landPending=true` to return zero items before `-StopOnly`.

The relaunch is a normal `scripts/restart-apphost.ps1` from the main checkout, after the user-secret repoint and after the desktop container is stopped (D-15). Product hosts track the current `rc/YYYY-MM-DD` tip, not master (`docs/release-gates.md`). As of this plan the line is `rc/2026-10-18`. Pre-flight re-reads the current line. `-ExpectedServerSha` is that tip. The connection-string change is not a commit, so the SHA does not move. The restart refuses a worktree (exit 3). Do not pass `-AllowWorktree`.

Postgres on 17280 and the session runner on 17204 survive an ordinary restart. `-StopOnly` must use the same port set the restart script already refuses to kill. It must not stop `antiphon-postgres`. Phase 4 stops that container on purpose, later, and does not remove the volume.

### D-10. One verified snapshot and one scratch restore gate the window

Filing B1, C1, C2, and C3, standing up an empty server2 cluster, and merging the `-WhatIf` script do not wait for CARD-1403 or for a day of successful snapshots.

The production window waits for all of:

- one encrypted custom-format dump of the desktop database, written by the B1 job or by the same command the job will use
- one `pg_restore` of that dump into a scratch cluster on the server2 host, with the verify queries in phase 3 passing
- the scratch volume removed afterwards
- `{{TOKENS}}` filled
- the operator window flag

### D-11. Rollback trigger

Record a 30-minute baseline immediately before phase 1, from the desktop, against the current localhost database.

- 20 samples of `GET /api/agent-tasks/pipeline?projectId=<Antiphon project id>`. Store minimum, median, and maximum. This call reads the database.
- Count of agent tasks that entered `Failed` in that window, and count of land requests that ended failed in that window.
- `GET /api/diagnostics/session-state` pool flags.
- `GET /health` and `GET /api/version` SHA, which must already equal the rc tip.

Abort before the dump (the old database is still the only writer; this is not a rollback):

- `landPending` is not empty
- preview of `scripts/recover-repository-children.ps1 -Repository <main checkout>` shows an uncertain child
- `{{DESKTOP_TO_SERVER2_TAILNET_MS}}` median is above 250 (about twice the 127 ms reverse-path median). The operator accepted the measured band, not a worse unmeasured path
- `{{PG_DATABASE_SIZE_BYTES}}` or the drill misses the D-16 envelope
- pool flags differ from D-8
- host 5432 is no longer refused, or the Tailscale address cannot be read

Roll back immediately during or after phase 4:

- any post-check in phase 5 fails
- the desktop container, if started for inspection, shows an application client while AppHost is supposed to be on server2 (split brain)
- AppHost comes up healthy while server2 `pg_stat_activity` shows no desktop client. The process is then on some other database. Treat that as split brain

Roll back if, in the first 30 minutes after phase 5 passes, either:

- the median of a fresh 20 samples of the same pipeline URL is greater than both 1 second and 3 times the baseline median
- the count of new dispatch or land failures exceeds the baseline count by more than 2

The monitor prints `ROLLBACK` and exits 2. It does not stop a container, delete a volume, or restart AppHost. The operator runs phase 6.

These thresholds are the stated defaults. The operator can tighten them in the window log before phase 1. Loosening them during the window is a new decision, not a script flag.

### D-12. Snapshots are encrypted custom dumps, 14 dailies, scheduled by Windmill

Format `pg_dump -Fc --no-owner --no-acl`, from inside the container that holds the data, so the dump does not cross the tailnet while the database still lives on the desktop. Encrypt with `age` to an operator recipient before the file reaches the sync folder. Retention is 14 successful dailies, the same count `dev-backup.ps1` uses. Prune only after the new encrypted file is durable. A failed run deletes its partial file and does not prune.

The scheduler is a new Windmill job on the desktop worker, the same shape as `u/lndcobra/antiphon_nightly_tests`. Schedule payload is created disabled. Enabling it is human-only.

Rejected: Hangfire. Storage is in-memory (`UseInMemoryStorage` in `server/Program.cs`). A desktop outage at the scheduled minute loses the fire. Root `docker-compose.yml` even sets `Hangfire__ServerEnabled` false for the Linux stack. The desktop `server/appsettings.json` has `Hangfire:ServerEnabled` true, which is why a restart must show the jobs again, and why Hangfire is still the wrong owner for an off-host copy.

Rejected: pointing `dev-backup.ps1` at OneDrive. That script writes a plaintext zip into `<repo>/backups/`, which is gitignored, and `dev-restore.ps1` drops the `antiphon` database in the production compose project. The drill must not call it.

Rejected: `rclone` or a server2 OneDrive login. While the database is on the desktop, the job writes the encrypted file into the desktop sync folder and the sync client uploads it. `{{ONEDRIVE_ANTIPHON_DIR}}` comes from task `5414c8f6`. If that directory is missing, the job fails closed and writes nothing. After the database moves, the job runs on the host that has the data and copies the already-encrypted file to the same desktop folder over the SSH path that already exists for that host. The OneDrive account stays on the desktop.

The operator holds the age private key outside the dump and outside the sync folder. A dump contains transcript text in the clear. API keys in the database are Data Protection ciphertext; the key ring is a filesystem path, not a row, so a restore does not restore those secrets (`docs/agent-credentials.md`).

### D-13. Resource limits default to 4 GiB and 4 CPUs, then follow the measurement

`mem_limit: 4g` and `cpus: 4.0` in the compose service. The 2026-09-25 container sat around 0.8 GiB and, during that incident, about four cores. The 2026-10-08 samples were far below that CPU. server2 had about 103 GiB available and 24 CPUs visible from the runner on 2026-10-21, so this cap does not squeeze the host. It does stop a runaway Postgres from taking the runner host.

Pre-flight: if `{{STATS_MEM_MIB}}` is above 2048, set the memory limit to twice that measurement, rounded up to the next GiB. If `{{STATS_CPU_PERCENT}}` sustained (not a one-second spike) is above 400, set `cpus` to twice that core count. Record the chosen numbers in the window log. Do not put the measurement into git as a fake current fact.

### D-14. Stage D stays a later card

C publishes Postgres on the tailnet so the desktop server can reach it. D, when CARD-1221 says the control plane moves, attaches the server container to the same user-defined network `antiphon-server2`, uses hostname `postgres` the way root `docker-compose.yml` does, and then drops the tailnet port publish. The volume name, the env-file custody, the snapshot script, and the scratch drill stay as they are. C does not bake the desktop tailnet address into an image, a committed appsettings file, or a Windmill script argument.

This plan does not choose which desktop services must move, and it does not treat root `docker-compose.yml` as that decision. That file still disables interpreters, the distiller, the Hangfire worker, the census, schedules, the channel bridge, and digest.

### D-15. Stop the desktop container before AppHost starts on the new string

After the dump, the restore, and the verify queries, stop `antiphon-postgres` with `docker compose -f docker-compose.dev.yml stop postgres`. Do not `rm` the container and do not remove `antiphon_pgdata`. Then set the AppHost user-secret, then run `restart-apphost.ps1` from the main checkout.

A missed or mistyped override then fails to connect to localhost:17280, because nothing is listening. It cannot silently keep writing the old volume. Rollback starts the container again, removes the user-secret key, and restarts AppHost.

### D-16. Downtime envelope

```
T_down = T_quiesce + T_dump + T_copy + T_restore + T_verify + T_restart
```

`T_quiesce` is the land drain plus `-StopOnly` (minutes, dominated by whatever land is in flight; the stop itself is the 20 s graceful wait plus the kill). `T_dump` and `T_restore` scale with `{{PG_DATABASE_SIZE_BYTES}}` and local disk, measured by the scratch drill as `{{DRILL_DUMP_RESTORE_SEC}}`. `T_copy` is the encrypted dump file crossing to server2 over the existing SSH path, measured by that same drill as `{{DRILL_COPY_SEC}}`. `T_verify` is one `count(*)` per user table, sequence readback, extension readback, and the key-table checksums (phase 3). `T_restart` is a normal AppHost restart.

Planning envelope: 30 minutes, and only when both of these hold:

- `{{PG_DATABASE_SIZE_BYTES}}` is at most 2 GiB
- `{{DRILL_DUMP_RESTORE_SEC}}` plus `{{DRILL_COPY_SEC}}` is at most 600

Otherwise the window does not start. Recompute `T_down` from the drill and ask the operator to accept that number. If the database is above 8 GiB, D-1's rejection of logical replication is reopened as a new plan, not stretched inside this window.

The September 2026 table sizes (largest tables a few hundred MiB, 95 public tables, database total bytes not recorded) are consistent with the envelope and are not a substitute for `{{PG_DATABASE_SIZE_BYTES}}`.

Sessions on the runner stay up. The board is frozen while the server is down. Hangfire's in-memory queue is discarded; recurring jobs are registered again at startup. A fire that was due during the outage does not run later.

## Ground truth

| The card or the investigation says | What the tree and the 2026-10-21 measurement show |
|---|---|
| Production Postgres is the desktop Docker database | `docker-compose.dev.yml` project `antiphon`, service `postgres`, image `postgres:16`, container `antiphon-postgres`, host port `17280:5432` with no bind address, volume `pgdata` (project volume `antiphon_pgdata`), `restart: unless-stopped`, `pg_stat_statements` preload. AppHost does not start it |
| server2 has no standing Antiphon Postgres | `docker-compose.server2-runner.yml` project `antiphon-runner` is the phone-home runner only. `docs/docker-stack.md` states that (the sentence CARD-1221 cites has moved; it was at line 869 when this plan was written). Host TCP 5432 refused on the Tailscale and LAN addresses. Host TCP 5433 open on the Tailscale address only |
| AppHost stays on the desktop for this card | `restart-apphost.ps1` relaunches `dev-aspire.ps1` from the main checkout. Product hosts track the rc tip. Current line recorded in `docs/release-gates.md`: `rc/2026-10-18` |
| Every query will pay the 72-127 ms | That sample is server2 to desktop TCP connect on port 17280, three attempts, around 2026-10-21 13:40Z. Desktop to server2 was not measured. Cloudflare to the control plane was 173-220 ms to first byte and is the wrong path for Postgres |
| The hot path is transcript receipt scans | 2026-10-08 sample (`docs/investigations/2026-10-08-card-1121-receipt-scan-sample.md`): about 2.5 large SELECTs per second, on the order of 850-1,500 `Text` rows, execution already 45-81 ms. CARD-0698's multi-core scans were the 2026-09-25 mechanism; CPU was no longer in that state on 2026-10-08 |
| CARD-0701 means the UI polls Postgres | Queue and working-state bits are cached in the server process. Agent list (about 5 s) and working badge (about 3 s) hit the server once the cache is warm. Residual receipt queries still hit Postgres. The 500 ms figure in that note is delivery-verification polling, not by itself a Postgres round trip |
| A connection-string tweak can remove the round trips | Pooling and `No Reset On Close` are already the production settings (D-8). Neither removes a per-command round trip. Batching is code on CARD-1403 |
| Hangfire can own the daily job | Desktop `Hangfire:ServerEnabled` is true, storage is in-memory, jobs are re-registered in process at startup. A stopped server misses the fire |
| A backup job already exists | `dev-backup.ps1` is manual plaintext SQL, zipped, 14 files, under gitignored `backups/`. Nothing in the repo schedules it. No OneDrive path is recorded |
| The land queue is drained by the graceful stop | Shutdown drains launches (`OperatorShutdownCoordinator`). Pending lands live in `LandRequestedAt` and resume on the next boot. The list filter is `GET /api/agent-tasks?landPending=true` |
| The repository lease is a database row | `RepositoryMutationLease` is in-process. An uncertain child journal under the git common directory still fences admission after the process is gone. Preview `scripts/recover-repository-children.ps1`. Do not pass `-Execute` in this window |
| Current size, CPU, OneDrive path, and desktop RTT are known | They are not, on this runner. Task `5414c8f6`'s file was not here |

## Measurement gates

Fill these from `dbfacts.txt` or a fresh read-only pass. Do not invent them. None of these reads log a password or a connection string. Database reads are `docker exec` into `antiphon-postgres` as the local superuser already used by `dev-backup.ps1`, printing only the requested column.

| Token | Read | Window rule |
|---|---|---|
| `{{STATS_CPU_PERCENT}}` | `docker stats` once, no stream, for `antiphon-postgres` | Feeds D-13. Does not by itself abort |
| `{{STATS_MEM_MIB}}` | same | Feeds D-13. Abort only if the chosen limit would exceed memory available on the server2 host |
| `{{PG_DATABASE_SIZE_BYTES}}` | `pg_database_size('antiphon')` | D-16 |
| `{{LARGEST_TABLES}}` | top relations by `pg_total_relation_size` | Names the checksum tables in phase 3. No row contents |
| `{{PG_STAT_STATEMENTS_LOADED}}` | `SHOW shared_preload_libraries` and `pg_extension` | Recorded. Target preloads either way. Mismatch after restore is a stop |
| `{{ONEDRIVE_ANTIPHON_DIR}}` | the desktop sync directory the operator already uses | B1 fails closed while this is empty. Confirm the client is signed in (human-only). Do not log in from an agent |
| `{{DESKTOP_TO_SERVER2_TAILNET_MS}}` | desktop TCP connect to the server2 Tailscale address on a closed high port | D-2 and D-11. This is the number the interim state will pay |
| `{{DESKTOP_TO_SERVER2_LAN_MS}}` | same toward the LAN address | Evidence only. D-4 does not publish on LAN |
| `{{SERVER2_HOST_DOCKER}}` | on the server2 host, not inside the runner: `docker ps`, `docker volume ls`, `df` of the Docker data root | Confirm there is still no Antiphon Postgres volume and that free space is many times `{{PG_DATABASE_SIZE_BYTES}}`. The runner saw 449 GiB free on its own LV; the Docker data root may be a different mount |
| `{{SERVER2_COMPOSE_PARENT}}` | parent directory of the live Windmill compose | D-3 |
| `{{DRILL_DUMP_RESTORE_SEC}}`, `{{DRILL_COPY_SEC}}` | the scratch drill | D-16 |

`{{ANTIPHON_PROJECT_ID}}` is the Antiphon project id from `GET /api/agent-tasks/pipeline?projectId=` once the caller already knows the board's project. The card's board id is `8988ca03-7414-47ad-b0b6-51556c701703`. The pipeline samples in D-11 use that project scope so the sample is not the whole fleet.

## Target topology

Host compose service, not a nested container and not Windmill.

| Item | Value |
|---|---|
| Where | server2 host daemon, directory `{{SERVER2_COMPOSE_PARENT}}/antiphon-postgres` |
| Compose project | `antiphon-server2-postgres` |
| Service name | `postgres` |
| Network | user-defined `antiphon-server2`, so stage D can join it |
| Image | `ANTIPHON_POSTGRES_IMAGE`, desktop `postgres:16` digest, never Alpine |
| Volume | project volume `pgdata` mounted at `/var/lib/postgresql/data` |
| Listen | Tailscale IPv4 only, container port 5432, host port 5432 |
| Limits | D-13 |
| Healthcheck | `pg_isready -U antiphon -d antiphon` as in D-6 |
| Restart | `unless-stopped` |
| Preload | `pg_stat_statements` as in D-6 |
| Env file | untracked, mode `0600`, holds `ANTIPHON_POSTGRES_BIND_ADDRESS`, `ANTIPHON_POSTGRES_IMAGE`, `POSTGRES_PASSWORD` |

The desktop dev compose file stays the local-development database definition. It is not edited to point at server2. After the window, the desktop container is stopped and the volume remains.

## Connection path and security

Desktop AppHost to server2 Tailscale address, TCP 5432, Postgres plaintext inside the tailnet. No public listener. No TLS in this cutover (D-4).

Custody, and nothing else:

- server2 env file, mode `0600`, outside the repo, for the cluster password, bind address, and image digest
- desktop user-secrets id `aspire-antiphon-apphost`, key `ConnectionStrings:DefaultConnection`, for the string AppHost injects
- age recipient public key on the desktop for snapshots; private key with the operator, not in OneDrive and not in the repo

Scripts take those values from the files the operator already trusts (`dotnet user-secrets`, the env file, the age recipient file). They do not accept the password as a command-line argument, and they do not write it to stdout, stderr, or the window log. A log line may say that the key is present or absent. Pre-flight runs `dotnet user-secrets list --id aspire-antiphon-apphost` only to see whether `ConnectionStrings:DefaultConnection` is already set, and the output is not pasted into the report. If it is already set, the operator replaces that value in the window; the plan does not print the old one.

Password rotation: required for the new cluster before it listens (D-7). Not required for the retained desktop volume. Required for the desktop volume only if someone publishes 17280 again.

## Cutover runbook

Owner script: `scripts/cutover-postgres-server2.ps1`. Default is `-WhatIf`, which prints the phase list and the non-secret commands and touches nothing. `-Execute` without `-ConfirmWindow` refuses. `-Execute -ConfirmWindow` is the operator window. The script has no volume-delete command.

Each phase has an exit check and a stop gate. A stop gate means the old container is still the database if the dump has not started, or phase 6 if the new database has taken any writer.

### Phase 0. Pre-flight

Work that can happen on ordinary days, before the window.

1. Fill every token in the measurement table.
2. Confirm `GET /api/version` SHA equals the main checkout `HEAD` and that `HEAD` is the current rc tip.
3. Confirm pool flags (D-8).
4. On the server2 host, confirm 5432 is still refused on the Tailscale address and on the LAN address, and that 5433 is still the only published database port.
5. Place the env file and the new password (operator). Start the empty `antiphon-server2-postgres` service. Exit check: healthcheck passes, and a TCP connect from the desktop to that Tailscale address on 5432 succeeds. Stop gate: the port is reachable from anywhere other than the tailnet, or the bind is `0.0.0.0`.
6. Take the first encrypted snapshot (B1's command) and restore it into a scratch project `antiphon-server2-postgres-drill` with its own volume. Run phase 3's queries against scratch versus source. Exit check: queries match. Then remove only the scratch container and the scratch volume.
7. Record `{{DRILL_DUMP_RESTORE_SEC}}` and `{{DRILL_COPY_SEC}}`. Apply D-16.
8. Run the C3 monitor long enough to store the baseline format, without treating that rehearsal as the window baseline.

Stop gate for leaving phase 0: any token empty, drill queries mismatch, D-16 missed, or the new listener reachable off the tailnet. No production writer has moved, so the response is to fix the rehearsal, not to roll back a cutover.

### Phase 1. Quiesce

1. Orchestrator hold for the window: `scripts/keep-six.ps1 -Hold` for the standing orchestrator, with a reason and an `Until` that covers the window. This stops the keep-six reminder from pulling new cards. It does not stop the server dispatcher. Already queued tasks can still launch until the process stops. Say that in the window log.
2. `GET /api/agent-tasks?projectId={{ANTIPHON_PROJECT_ID}}&landPending=true` until `items` is empty. Do not shut down while it is not empty.
3. Preview `scripts/recover-repository-children.ps1 -Repository <main checkout>`. Uncertain children are a stop gate. Do not pass `-Execute`.
4. From the main checkout, `pwsh -NoProfile -File scripts/restart-apphost.ps1 -StopOnly`. Exit check: the server PID has exited, every port in the script's existing `$appHostPorts` set (17200, 17202, 17203, 17205, 17206, 17207, 17209) has no listener, port 17204's listener is still the session runner, and `antiphon-postgres` is still running. Stop gate: `-StopOnly` would relaunch, or port 17204 or 17280 was touched. Abort and leave the old database in place.
5. Second land check is impossible against a stopped server. The empty `landPending` result from step 2 is the witness. Copy it into the window log before step 4.

The desktop Postgres container stays up. Runners stay up. Their sessions cannot commit through a stopped server.

### Phase 2. Final dump and restore

1. Encrypted `pg_dump -Fc` from the desktop container, same command as B1, while `antiphon-postgres` has no client except that dump. Exit check: `docker exec` shows no other sessions in `pg_stat_activity` for database `antiphon`. Stop gate: another session appears. Do not dump under writers. Start AppHost again on the old string if the window is abandoned here (the user-secret has not changed yet).
2. Copy the encrypted file to the server2 host over the existing SSH path. Keep the desktop copy. It is the cutover witness, separate from the 14 dailies.
3. `pg_restore` into `antiphon-server2-postgres`, which is empty apart from the cluster created in phase 0. Exit check: `pg_restore` exit 0. A non-zero exit is a stop: leave AppHost down, leave the desktop container up, restore nothing else, and start AppHost on the old string after confirming the user-secret was not changed.

No replication slot is created (D-1).

### Phase 3. Verify

Run inside each container with `docker exec`, never with a host connection string.

Exit check, all of these match between source and target:

- every user table: `count(*)`
- every sequence: name, `last_value`, `is_called`
- every extension: `extname`, `extversion`, including `pg_stat_statements` when the source has it
- `__EFMigrationsHistory`: row count and the ordered list of `MigrationId`
- key tables: `__EFMigrationsHistory` plus the relations named in `{{LARGEST_TABLES}}` (top five by size). For a relation at or under 32 MiB, an ordered checksum of the primary-key projection. For a larger relation, count, minimum primary key, and maximum primary key. Transcript text is not printed and is not copied into the log

Stop gate: any mismatch. Do not repoint. The desktop container is still the untouched writer. Start AppHost on the old string.

### Phase 4. Repoint and restart on the rc tip

1. `docker compose -f docker-compose.dev.yml stop postgres`. Exit check: container status is exited, volume `antiphon_pgdata` still listed. Stop gate: the command offered to remove the volume. Do not continue.
2. Operator sets AppHost user-secrets key `ConnectionStrings:DefaultConnection` for id `aspire-antiphon-apphost` (D-7). Exit check: the key is present. The value is not displayed.
3. From the main checkout, on the rc tip recorded in phase 0: `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <that tip>`. Exit check: exit 0, `GET /health` 200, `GET /api/version` SHA equals that tip. Exit 5 keeps the restart lock; do not start a second copy. Stop gate: exit 3 (worktree) or a SHA mismatch. Go to phase 6 if the user-secret was already changed.

### Phase 5. Post-checks

All from the desktop, against the restarted AppHost.

| Check | Pass |
|---|---|
| `GET /health` | 200 |
| `GET /api/version` | SHA equals the rc tip from phase 0, `land-v2` advertised |
| Pool flags | D-8, and `NoResetOnClose` still true |
| server2 `pg_stat_activity` | at least one client from the desktop Tailscale address on database `antiphon` |
| desktop container | still stopped; if it is started for a moment to look, no application client |
| One dispatch | a real task create reaches a runner and is visible on the board. Use an ordinary low-risk card the operator names in the window log. Do not invent a production write for its own sake beyond that one task |
| One land | that task, or another the operator names that is already eligible, lands through `/land/v2` and the receipt is the usual one |
| SignalR | `POST /hubs/antiphon/negotiate` answers. An auth challenge is a pass. A connection error is a fail |
| Hangfire | `Hangfire:ServerEnabled` is true, so startup re-registers the recurring jobs. `GET /hangfire` with the operator credential (`scripts/hangfire-dashboard.ps1`) shows them. In-memory jobs that were queued at shutdown are gone; that is expected, not a fail |

Then run the C3 monitor for 30 minutes (D-11). Exit 0 ends the window. Exit 2 goes to phase 6.

The old volume stays. Nobody removes it in this plan.

### Phase 6. Rollback

1. `docker compose -f docker-compose.dev.yml start postgres`. Exit check: healthcheck passes. The volume was not written after the dump if phase 1 and phase 2 held.
2. Operator removes the AppHost user-secret key `ConnectionStrings:DefaultConnection` so the tracked localhost string is back.
3. `scripts/restart-apphost.ps1 -ExpectedServerSha <same rc tip>` from the main checkout.
4. Repeat phase 5's health, version, pool, and one pipeline sample. `pg_stat_activity` on the desktop container shows the AppHost client. server2 shows no desktop client. Stop the server2 listener if the operator wants the tailnet port closed; do not delete its volume until the operator says the new copy is abandoned.

If any writer committed to the new database after phase 4, those rows are not on the desktop volume. Phase 6 then stops for an operator choice: drop those rows by returning to the desktop volume, or dump them back. The script does not choose. That is why phase 1 has to be empty of lands and phase 2 has to see no other sessions.

## Downtime

See D-16. The number used in the operator window log is the recomputed `T_down`, not the 30-minute envelope, once the drill has run. Until `dbfacts.txt` is read, the only honest estimate is that envelope's precondition: a few hundred MiB on the September 2026 evidence would land inside 30 minutes, and this plan will not book the window on that evidence alone.

What the users of the board experience: API, land, and dispatch errors for `T_down`. Runner sessions stay up and retry. UI polls fail because the server is down, not because Postgres is slow. After phase 4, those same calls pay `{{DESKTOP_TO_SERVER2_TAILNET_MS}}` per command.

## Interim-state risks

The desktop server will pay one round trip per command for as long as stage D has not landed. CARD-1403 can shorten the number of commands. It is not required to start, and it may land before or after the window.

What hurts first:

1. Receipt scans, the CARD-0698 / 2026-10-08 path. They are already 45-81 ms of execution and about 2.5 calls per second, each returning on the order of a thousand text rows. Each call gains a full `{{DESKTOP_TO_SERVER2_TAILNET_MS}}`, and the row bytes ride that same path. This is the first place a 30-minute watch will see the pipeline sample move.
2. Dispatch and land. Those are many small indexed writes that are milliseconds on localhost and become round-trip-bound. They are the operator-visible delay behind D-11's failure count. CARD-1403 targets this class of chattiness.
3. CARD-0701's cached UI polls do not multiply database round trips while the cache is warm. They are the wrong signal for a rollback.
4. SignalR fan-out is in-process after the write that publishes the event. The negotiate check proves the hub is up. It does not measure database cost.
5. Hangfire does not use this database. The risk is the missed in-memory fire during `T_down`, not a slow job store.

Config that reduces round trips immediately: none beyond what is already on. Keep pooling and `No Reset On Close`. Do not flip `Max Auto Prepare` or multiplexing in the window (D-8). Further reduction is CARD-1403.

Rollback trigger: D-11. The old volume is the rollback copy. It stays unmounted from any writer after phase 4 until phase 6 starts it.

## Human-only steps

Agents and the cutover script do not perform these.

| Step | Why it stays human |
|---|---|
| Choose, store, and rotate passwords | Custody. The new server2 password is required before the listener starts. Desktop rotation is a separate decision, required only if 17280 is published again |
| `docker volume rm` of `antiphon_pgdata` or of `antiphon-server2-postgres_pgdata` | The card forbids volume deletion without an explicit operator approval. The script has no such command |
| Publish Postgres on `0.0.0.0`, on the LAN, or through Cloudflare | Operator decision. D-4 binds the tailnet address only |
| OneDrive sign-in | The job writes a local folder. It does not log in |
| Enable the Windmill schedule | Registration creates it disabled. Enabling is the same kind of operator step as the release-gate schedules |
| Pass `-ConfirmWindow` | Agrees the downtime and the D-11 thresholds for that day |
| Decide phase 6 when the new database has rows the old volume lacks | The script stops and asks |

Stopping AppHost inside an agreed window is `-StopOnly` plus the later `restart-apphost.ps1`. It is not a surprise restart. Orchestrator autonomy for ordinary restarts does not extend to this window.

## Stage B snapshot job

This section is the design B1 implements. It is independent of the move and should be filed in parallel with CARD-1403.

| Item | Design |
|---|---|
| Script | `scripts/postgres-snapshot.ps1`. ASCII-only. Default `-WhatIf` prints the dump, encrypt, and prune steps and runs no `docker`. `-Execute` is the real run |
| Dump | `docker exec` into the current source container, `pg_dump -Fc --no-owner --no-acl`. While the database is on the desktop, the container is `antiphon-postgres`. After C, `-Source` selects the server2 host over SSH and the dump stays next to the data |
| Encrypt | `age -r <recipient file>`. Missing `age`, missing recipient, or a recipient path inside `{{ONEDRIVE_ANTIPHON_DIR}}` fails closed with no output file |
| Destination | `{{ONEDRIVE_ANTIPHON_DIR}}\antiphon-postgres\antiphon-<utc>-YYYYMMDDTHHMMSSZ.dump.age` |
| Retention | 14 `*.dump.age` files in that directory. Prune only after the new file is fully written and `age` has been asked to decrypt the header (no plaintext dumped). Fewer than one file left is a failed prune, not a success |
| Status | `snapshot-status.json` beside the dumps, containing the UTC time, the script exit, and the byte size. No connection string, no key, no row contents |
| Schedule | `scripts/windmill/antiphon-postgres-snapshot.json` and `.schedule.json`, path `u/lndcobra/antiphon_postgres_snapshot`, desktop worker, daily `0 30 1 * * *` Europe/London. Created disabled |
| Register | `scripts/register-postgres-snapshot.ps1`, preview by default, `-Apply` registers, `-EnableSchedule` is the human enable |
| Alert | Non-zero script exit is a failed Windmill job. B1 checks whether that workspace already notifies on job error and records the answer. This card does not add a chat sink. A success file older than 26 hours is the second signal, read from `snapshot-status.json` |
| Drill | Scratch project on the server2 host, `pg_restore`, phase 3 queries, delete only the scratch volume. One recorded drill is the phase 0 witness |

`dev-backup.ps1` stays the manual plaintext tool. The scheduled path does not call it.

## Slices

File these as separate Backlog cards. This plan does not file them. Each card runs its own pipeline. Production data movement stays inside C2's `-Execute -ConfirmWindow`, which is the operator window, not a Code-stage side effect.

### B1. Daily encrypted snapshot

Scope: `scripts/postgres-snapshot.ps1`, `scripts/test-postgres-snapshot.ps1`, `scripts/register-postgres-snapshot.ps1`, `scripts/windmill/antiphon-postgres-snapshot.json`, `scripts/windmill/antiphon-postgres-snapshot.schedule.json`, a short paragraph in `scripts/windmill/README.md`.

Tests, named for test-design to close:

- `-WhatIf` exits 0, prints the destination pattern, and does not invoke `docker`.
- Missing `age` or a recipient path inside the destination directory exits non-zero and leaves no file.
- Prune keeps 14 and never runs when the new file was not written.
- The Windmill schedule payload is created with `enabled` false.

The scratch restore on server2 is C1's drill using this script's output. B1's own tests use a fake `docker` and a temp directory.

### C1. server2 Postgres service and the restore drill

Scope: `docker-compose.server2-postgres.yml`, `scripts/deploy-server2-postgres.ps1` (`-WhatIf` default; `-Execute` only on the server2 host), `scripts/test-server2-postgres-compose.ps1`, and a replacement sentence in `docs/docker-stack.md` that this host service exists beside the runner and is not the nested daemon and not Windmill.

Tests:

- The compose file requires `ANTIPHON_POSTGRES_BIND_ADDRESS` and `ANTIPHON_POSTGRES_IMAGE`.
- The port string cannot parse as `0.0.0.0` or as an omitted address.
- Port 5433 does not appear. Image text `alpine` does not appear.
- `mem_limit`, `cpus`, `unless-stopped`, the `pg_isready` healthcheck, and the `pg_stat_statements` preload are present.
- Project name is `antiphon-server2-postgres`. Network name is `antiphon-server2`.
- `-WhatIf` of the deploy script prints the host-daemon command and does not call `docker`.

The live drill (phase 0 steps 5-7) is the operator rehearsal this card's runbook section names. It is not a CI test. CI does not get the server2 host socket.

### C2. Cutover script with `-WhatIf` and `-StopOnly`

Scope: `scripts/cutover-postgres-server2.ps1`, `scripts/test-cutover-postgres-server2.ps1`, and `-StopOnly` on `scripts/restart-apphost.ps1` plus the existing AppHost script test that covers it.

Tests:

- Default and `-WhatIf` exit 0, list phases 0 through 6, and do not POST, do not call `docker`, and do not call `dotnet user-secrets set`.
- `-Execute` without `-ConfirmWindow` exits non-zero and does none of those either.
- The script source has no `volume rm`, no `docker volume rm`, and no `-AllowWorktree`.
- `-StopOnly -WhatIf` on the restart script does not POST and does not reach the relaunch. The real `-StopOnly` test is the one the AppHost script tests already use for process stubs: graceful POST, then exit before `dev-aspire.ps1`.
- Given a fixture `landPending` count other than zero, the quiesce phase refuses before any stop.

### C3. Post-cutover monitor

Scope: `scripts/postgres-cutover-monitor.ps1`, `scripts/test-postgres-cutover-monitor.ps1`. The baseline file is gitignored, same as other generated evidence.

Tests, pure functions over fixture samples, no live HTTP:

- Median above both 1 second and 3 times the baseline median exits 2 and prints `ROLLBACK`.
- Median above 3 times a tiny baseline but under 1 second exits 0 (the floor in D-11).
- Failure count more than 2 above baseline exits 2.
- The script source does not call `restart-apphost.ps1`, `docker`, or `user-secrets`.

The 30-minute live watch is phase 5, operator-run.

## Proposed checkpoints

Test-design closes this list. These rows are the proposal, not the Code-stage closed list. Lane for every row: the script-test host, via `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <row> -- pwsh -NoProfile -File <script>`. No `Antiphon.Tests` project, no production database, no server2 host socket, no AppHost restart.

| Id | Script | Proves |
|---|---|---|
| CP-1 | `scripts/test-postgres-snapshot.ps1` | B1 `-WhatIf`, fail-closed encrypt, prune |
| CP-2 | `scripts/test-server2-postgres-compose.ps1` | C1 compose contract and deploy `-WhatIf` |
| CP-3 | `scripts/test-cutover-postgres-server2.ps1` | C2 `-WhatIf`, refuse without `-ConfirmWindow`, no volume deletion |
| CP-4 | the existing `restart-apphost` script test, `-StopOnly` cases | graceful stop does not relaunch |
| CP-5 | `scripts/test-postgres-cutover-monitor.ps1` | D-11 thresholds |

## Out of scope

- Moving AppHost, the server, or the land worker (stage D, CARD-1221).
- CARD-1403's call-site batching, except the statement that it is not a gate.
- Deleting `antiphon_pgdata`.
- Changing the desktop dev password.
- Publishing any port that is not the tailnet bind in D-4.
- A Hangfire backup, a WAL archive, or logical replication inside the D-16 envelope.
