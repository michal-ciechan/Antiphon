# CARD-1401 plan: move production Postgres to server2 first

Date: 2026-10-21. Plan task `c4e4f277` on `feat/card-task-c4e4f277`, revised by plan task `a831723c` on `feat/card-task-a831723c`.
Card: CARD-1401 (`38b2bce2-2c19-4002-91f3-af9076a13cf0`), board Antiphon.
Investigation: `docs/investigations/2026-10-21-card-1401-postgres-server2-and-snapshots.md` at `f94464fff0ef430e2a0ab142d84b6c7fb4fe0127` on `feat/card-task-760095f1` (not in this branch).
Desktop measurement: task `5414c8f6`, 2026-10-21 14:53 BST, carried in this revision's brief. This session did not move data, restart a host, open a database session, or read a password or connection string.

The operator decision stands: stage C moves the database to server2 before the control plane. The interim state is accepted: AppHost and the server stay on the desktop, Postgres moves to server2. The interim round trip is the desktop-to-server2 path measured below, about 3.8 ms on the LAN and 6.7 ms on Tailscale. The 72-127 ms figure is the reverse path and is not this cost. Stage A (CARD-1403) and stage B (daily snapshots) run in parallel with preparing C. They are not gates for that preparation. The cutover window itself waits for one verified snapshot and one scratch restore. Stage D (AppHost, server, and land worker move to server2) is out of scope except that C must leave it possible. CARD-1221 remains the Linux deployment contract.

Slice cards filed with this revision, all Backlog on Antiphon: B1 CARD-1405 (`807e41f4-a15a-4cb3-ad0e-99f5e1372297`), C1 CARD-1406 (`7bc88cc8-776b-4acc-8d62-842dbfd8cd28`), C2 CARD-1407 (`391c5685-b9fe-4f3b-97d8-75502b2d4ef9`), C3 CARD-1408 (`f674d073-f94a-4ffa-841d-5bb2ccbc54d2`). Each description names CARD-1401 and CARD-1403.

## Decisions

### D-1. One quiesced `pg_dump -Fc`, then `pg_restore`

The cutover copy uses the same custom-format dump as the daily snapshot. The desktop server is stopped first, so the dump is a quiet point, not a live catch-up.

Rejected: logical replication. A publication and a slot shorten the final pause only if the database is large. They keep a second writer path open across the tailnet until promotion. Use them only if the size gate in D-16 fails.

Rejected: base backup plus WAL archiving. The card asks for a daily snapshot and a cutover, not point-in-time recovery.

### D-2. The interim state is the plan, not a veto

The investigation's "stop unless desktop-to-server2 is under about 10 ms, or the fat receipt scans are gone" is superseded by the operator decision on 2026-10-21. CARD-1403 stays parallel. It is not a precondition of the move.

The path the desktop server will pay is desktop to server2. Task `5414c8f6` measured it at 2026-10-21 14:53 BST as a TCP handshake median to port 22: 3.83 ms on LAN `192.168.4.34`, 6.72 ms on Tailscale `100.93.77.126`. The ssh alias `server2` resolves to `100.93.77.126`. Those addresses are evidence of the path that was timed. They are not the compose bind. D-4 still reads `tailscale ip -4` on the host at pre-flight into an untracked env file.

The 72-127 ms figure is the other direction: server2 to the published desktop Postgres port, three attempts, around 2026-10-21 13:40Z (min 72 ms, median 127 ms). It is not the interim cost. A desktop loopback TCP connect to `:17280` was 5.24 ms. That number is handshake cost through the published port, not the query round trip on a pooled connection. Pooling already pays a handshake once per physical connection. Each later command pays about one Tailscale round trip, 6.7 ms, on top of server execution.

The window uses 6.7 ms as the expected per-command cost. A fresh reading near that median is not a reason to abort. A Tailscale handshake median above 20 ms, about three times 6.72 ms, is a pre-window stop (D-11). The operator accepted this measured band. The reverse-path band is not a substitute for it. D-4 stays tailnet-only: on this sample the LAN handshake is about 3 ms faster, which does not reopen that decision.

### D-3. A host compose service beside the runner, on the host daemon

New project name `antiphon-server2-postgres`. New file `docker-compose.server2-postgres.yml`. The Docker volume is `antiphon-server2-postgres_pgdata`. It is not `antiphon_pgdata`, not the runner's `dind-data`, and not Windmill's database volume.

Rejected: a container on the nested dockerd inside `antiphon-runner`. That store dies when the runner is recreated, and the host daemon never sees those containers (`docker-compose.server2-runner.yml` header).

Rejected: reuse of Windmill's database or its published port. An earlier 2026-10-21 sample found host TCP 5433 open on the server2 Tailscale address only, matching the recorded Windmill database publish. The 14:53 BST desktop pass found every server2 port except 22 timing out, so nothing was published toward the desktop at that moment. Pre-flight still lists the host's own listeners. 5433 stays Windmill's port and is not reused.

The service is started with the host Docker socket, from a directory on the server2 host that is a sibling of the Windmill compose directory. The 2026-09-17 note put Windmill at `/home/mc/docker/windmill`. Pre-flight re-lists that parent and records it as `{{SERVER2_COMPOSE_PARENT}}`. The proposed directory is `{{SERVER2_COMPOSE_PARENT}}/antiphon-postgres`. The runner container must not be the process that creates this service.

### D-4. Tailnet-only plaintext, no committed address

Postgres listens on the server2 Tailscale IPv4 only. The compose port binding is `${ANTIPHON_POSTGRES_BIND_ADDRESS:?}:5432:5432`. Compose refuses to start when the variable is empty, so a forgotten file cannot fall through to `0.0.0.0`. The address is read on the host at pre-flight (`tailscale ip -4`) into an untracked env file mode `0600`. It is not committed. The investigation's measured addresses stay evidence in the ground-truth table; they are not the config.

Transport encryption is the tailnet (WireGuard). Postgres itself stays plaintext. Nothing in the repo sets `SslMode` today, and the desktop listener is plaintext.

Rejected: Cloudflare or any public port. The current tunnel is HTTPS to the control plane, not a TCP database port.

Rejected: publishing on the LAN interface. The measured LAN handshake is 3.83 ms against 6.72 ms on Tailscale. That gap is about 3 ms and does not reopen this decision. A wider publish is a separate operator decision (human-only).

Rejected: provisioning a server certificate and turning on Npgsql TLS in this cutover. It adds a second secret and a second failure mode on the day the host changes. The tailnet bind is the transport control.

### D-5. Port 5432 on that address

From the desktop at 14:53 BST, server2 ports other than 22 timed out, including 5432, toward both the Tailscale address and the LAN address. An earlier same-day sample had 5432 refused rather than timing out. Pre-flight checks the host's own listeners before the bind is added. A timeout seen from the desktop is not a substitute for that host reading.

Rejected: the desktop's 17280. Rejected: 5433, which is Windmill's publish. If 5432 is no longer refused, the window stops. The plan does not hunt for a spare port on the day.

### D-6. Same image major as the desktop container, digest-pinned, with the same preload

Desktop compose uses `postgres:16`, not Alpine (`docker-compose.dev.yml`). Tests use `postgres:16-alpine`; this service does not. At pre-flight, record the running desktop image id and digest into `ANTIPHON_POSTGRES_IMAGE`. The compose file requires that variable. Do not let server2 float on a newer `postgres:16` pull than the desktop container.

The service command matches the desktop preload: `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`. Migration `20260925120000_EnablePgStatStatements` creates the extension. A restore of a database that has the extension fails closed without the library. The live desktop container has it loaded: pg_stat_statements 1.10, max 5000, track=top, statistics reset 2026-10-21 07:37Z. The running server is PostgreSQL 16.14. The 2026-09-25 reading that the library was not loaded is stale. The target preloads the same settings. The verify phase compares extension name and version to the source (1.10 on this measurement) and records a mismatch as a stop, not a surprise to fix mid-window. The image pin is still the desktop image digest, not the floating `postgres:16` tag.

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

What actually hurts is one round trip per command at about 6.7 ms, plus the bytes of a large result. Pooling avoids a new TCP handshake per command. The 5.24 ms loopback connect and the 6.72 ms Tailscale connect are handshake costs, already paid when the pool opened the connection. Pooling does not remove the command round trip. No remaining connection-string flag does that.

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
- the remaining measurement gates filled (server2 Docker state, compose parent, dump size, drill times, `count(*)`)
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
- a fresh desktop-to-server2 Tailscale TCP-connect median is above 20 ms (about three times the 6.72 ms median). A median near 7 ms is the accepted band and is not an abort. The withdrawn line was 250 ms, twice the 127 ms reverse-path median, which would have allowed a 50 ms tailnet through
- the database grows past the D-16 size gate, or the drill misses the D-16 time gate
- pool flags differ from D-8
- host 5432 accepts a connection before this plan's bind (the 14:53 BST desktop pass saw a timeout, not a listener), or the Tailscale address cannot be read

Roll back immediately during or after phase 4:

- any post-check in phase 5 fails
- the desktop container, if started for inspection, shows an application client while AppHost is supposed to be on server2 (split brain)
- AppHost comes up healthy while server2 `pg_stat_activity` shows no desktop client. The process is then on some other database. Treat that as split brain

Roll back if, in the first 30 minutes after phase 5 passes, either:

- the median of a fresh 20 samples of the same pipeline URL is greater than both 1 second and 3 times the baseline median
- the count of new dispatch or land failures exceeds the baseline count by more than 2

The monitor prints `ROLLBACK` and exits 2. It does not stop a container, delete a volume, or restart AppHost. The operator runs phase 6.

The 1 second floor stays because 6.7 ms per command does not by itself make a pipeline sample slow. A chatty read that only picks up the accepted round trip must not roll the window back. The 3× rule is what catches a large-result transfer. The pins select already averages 1101 ms and about 169,000 rows on localhost, so tripling a baseline that includes it is a transfer failure, not the accepted round trip. The failure-count rule stays because a 6.7 ms path is not predicted to fail dispatch or land. Failures above the baseline are a different fault. See Interim-state risks for the statement arithmetic.

These thresholds are the stated defaults. The operator can tighten them in the window log before phase 1. Loosening them during the window is a new decision, not a script flag.

### D-12. Snapshots are encrypted custom dumps, 14 dailies, scheduled by Windmill

Format `pg_dump -Fc --no-owner --no-acl`, from inside the container that holds the data, so the dump does not cross the tailnet while the database still lives on the desktop. Encrypt with `age` to an operator recipient before the file reaches the sync folder. Retention is 14 successful dailies, the same count `dev-backup.ps1` uses. Prune only after the new encrypted file is durable. A failed run deletes its partial file and does not prune.

The scheduler is a new Windmill job on the desktop worker, the same shape as `u/lndcobra/antiphon_nightly_tests`. Schedule payload is created disabled. Enabling it is human-only.

Rejected: Hangfire. Storage is in-memory (`UseInMemoryStorage` in `server/Program.cs`). A desktop outage at the scheduled minute loses the fire. Root `docker-compose.yml` even sets `Hangfire__ServerEnabled` false for the Linux stack. The desktop `server/appsettings.json` has `Hangfire:ServerEnabled` true, which is why a restart must show the jobs again, and why Hangfire is still the wrong owner for an off-host copy.

Rejected: pointing `dev-backup.ps1` at OneDrive. That script is the repo-root `dev-backup.ps1`, not a file under `scripts\`. It runs `docker compose exec` of `pg_dump -U antiphon -d antiphon --no-owner --no-acl`, writes plain SQL, zips it to `<repo>/backups/*.sql.zip`, and keeps 14. It takes no OneDrive path. `dev-restore.ps1` drops the `antiphon` database in the production compose project. The drill must not call either script.

Rejected: `rclone` or a server2 OneDrive login. The desktop sync root is `C:\Users\lndco\OneDrive`, measured 2026-10-21 14:53 BST. It is a cloud-filter root with hydrated folders, so a file written there is a local write and the sync client uploads it. C: had 256 GiB free, which covers fourteen copies at the full database size (about 17 GiB) with room to spare. No Windmill backup job exists today. The scheduled job is new, created disabled.

The job runs on the host that holds the data. While that host is the desktop, it dumps locally and writes the encrypted file into `C:\Users\lndco\OneDrive\antiphon-postgres\`. The job creates that child directory. A missing sync root fails closed and writes nothing. The child not existing yet is not a failure. The ciphertext is written on C: outside the sync root and moved in, so the filter does not see a partial file. After the database moves, the same script runs on server2, next to the data, and copies the finished ciphertext to that same desktop folder over the SSH path that already exists. The OneDrive account stays on the desktop.

The operator holds the age private key outside the dump and outside the sync folder. A dump contains transcript text in the clear. API keys in the database are Data Protection ciphertext; the key ring is a filesystem path, not a row, so a restore does not restore those secrets (`docs/agent-credentials.md`).

### D-13. Resource limits default to 4 GiB and 4 CPUs, then follow the measurement

`mem_limit: 4g` and `cpus: 4.0` in the compose service. The 2026-09-25 container sat around 0.8 GiB and, during that incident, about four cores. The 2026-10-08 samples were far below that CPU. On 2026-10-21 14:53 BST the desktop container was 1.26 GiB (about 1,290 MiB) and 6-18% CPU. Both are under the raise-the-cap lines below, so the compose file keeps 4 GiB and 4 CPUs. That cap stops a runaway Postgres from taking the runner host. It is not a claim about server2 free memory. The runner's earlier view of about 103 GiB available and 24 CPUs is not the host Docker data-root reading. That reading stays a pre-flight gate.

Pre-flight: if a newer `docker stats` memory figure is above 2048 MiB, set the memory limit to twice that measurement, rounded up to the next GiB. If sustained CPU (not a one-second spike) is above 400%, set `cpus` to twice that core count. Record the chosen numbers in the window log. Do not commit a tighter cap from the 1.26 GiB / 6-18% sample. The 4 GiB / 4 CPU defaults are the ones that sample supports.

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

`T_quiesce` is the land drain plus `-StopOnly` (minutes, dominated by whatever land is in flight; the stop itself is the 20 s graceful wait plus the kill). `T_dump` and `T_restore` scale with database size and local disk, measured by the scratch drill as `{{DRILL_DUMP_RESTORE_SEC}}`. `T_copy` is the encrypted dump file crossing to server2 over the existing SSH path, measured by that same drill as `{{DRILL_COPY_SEC}}`. `T_verify` is one `count(*)` per user table, sequence readback, extension readback, and the key-table checks (phase 3). `T_restart` is a normal AppHost restart.

Size, measured: `pg_database_size('antiphon')` is 1,265,417,239 bytes, 1,207 MiB, 1.18 GiB. That is under the 2 GiB gate (2,147,483,648 bytes) by about 841 MiB. It is far under the 8 GiB line that would reopen logical replication. The size gate is met. It is no longer an open token.

Planning estimate for the data movement, not a timed run. `pg_dump -Fc` is single-threaded and the desktop container was at 6-18% CPU, so the dump is not starting from a saturated host. Band for `T_dump`: 2-8 minutes. `T_restore`: 4-12 minutes, the same order of work plus the heap rewrite and WAL. The supplied TranscriptEntries figures are 794 MB on the largest-relation list and 757 MB in the size summary, a 37 MB gap, so this estimate does not assume a large index build. `T_copy` at LAN gigabit: a file no larger than the database is about 10 seconds (1,265,417,239 bytes is 10.1 Gbit), and under 30 seconds with ordinary SSH overhead. The ssh alias `server2` resolves to the Tailscale address, so the LAN figure is the planning basis the byte count supports. It is not the path the script will time. Tailscale throughput is unmeasured. A 1.18 GiB file at 10 MB/s is about two minutes. At 1 MB/s it is about twenty minutes and would miss the envelope. That is why the drill still times the real copy.

Central estimate: dump 5 minutes, LAN copy under 1 minute, restore 8 minutes, about 15 minutes for those three. Add verify at 1-3 minutes (a sequential read of 1.2 GiB, `count(*)` per table still unmeasured), a quiesce that is only the 20 s stop, and a few minutes of AppHost restart, and central `T_down` is about 20 minutes. That sits inside the 30-minute envelope. The upper band of dump plus restore plus a LAN copy is about 20 minutes before quiesce and restart, so a slow drill can touch the envelope even though the central case does not.

What the drill must measure before the window is booked: dump elapsed time, restore elapsed time, copy elapsed time on the SSH path the script will use, the `-Fc` file size, the age ciphertext size, and `count(*)` per user table. Server2 disk free and the Docker data-root are a separate gate. They are not this estimate.

Automatic time gate, unchanged: `{{DRILL_DUMP_RESTORE_SEC}}` plus `{{DRILL_COPY_SEC}}` above 600 does not start the window. Recompute `T_down` from the drill and ask the operator to accept that number. A central result near 15 minutes will miss 600 seconds and still fit the 30-minute envelope if quiesce is a normal stop. Missing 600 seconds is that question. It is not the 8 GiB replication reopen. A drill whose dump plus restore plus copy exceeds 30 minutes on its own does not fit this envelope.

The September 2026 table sizes are not used. The byte count above replaces them.

Sessions on the runner stay up. The board is frozen while the server is down. Hangfire's in-memory queue is discarded; recurring jobs are registered again at startup. A fire that was due during the outage does not run later.

## Ground truth

| The card or the investigation says | What the tree and the 2026-10-21 measurement show |
|---|---|
| Production Postgres is the desktop Docker database | `docker-compose.dev.yml` project `antiphon`, service `postgres`, image `postgres:16`, container `antiphon-postgres`, host port `17280:5432` with no bind address, volume `pgdata` (project volume `antiphon_pgdata`), `restart: unless-stopped`, `pg_stat_statements` preload. AppHost does not start it |
| server2 has no standing Antiphon Postgres | `docker-compose.server2-runner.yml` project `antiphon-runner` is the phone-home runner only. `docs/docker-stack.md` states that (the sentence CARD-1221 cites has moved; it was at line 869 when this plan was written). From the desktop at 14:53 BST every server2 port except 22 timed out. An earlier same-day sample had 5432 refused and 5433 open on the Tailscale address only. Host `docker ps` and the Docker data-root are still unread |
| AppHost stays on the desktop for this card | `restart-apphost.ps1` relaunches `dev-aspire.ps1` from the main checkout. Product hosts track the rc tip. Current line recorded in `docs/release-gates.md`: `rc/2026-10-18` |
| Every query will pay the 72-127 ms | That sample is server2 to desktop TCP connect on port 17280, three attempts, around 2026-10-21 13:40Z. Desktop to server2, measured 14:53 BST as a port-22 handshake median, is 3.83 ms LAN and 6.72 ms Tailscale. Desktop loopback to `:17280` is 5.24 ms and is a handshake, not a pooled query. Cloudflare to the control plane was 173-220 ms to first byte and is the wrong path for Postgres |
| The hot path is transcript receipt scans | 2026-10-08 sample (`docs/investigations/2026-10-08-card-1121-receipt-scan-sample.md`): about 2.5 large SELECTs per second, on the order of 850-1,500 `Text` rows, execution already 45-81 ms. CARD-0698's multi-core scans were the 2026-09-25 mechanism; CPU was no longer in that state on 2026-10-08. The 2026-10-21 14:53 BST `pg_stat_statements` sample (reset 07:37Z) is the one D-11 uses: pins select 374 calls, mean 1101 ms, about 169k rows; EXISTS on TranscriptEntries 36,035 calls, mean 7.4 ms; TranscriptEntries reads 335 calls at mean 477 ms (~18k rows) and 4,461 calls at mean 26.5 ms (~2.5k rows); text LIKE 198 calls, mean 765 ms |
| CARD-0701 means the UI polls Postgres | Queue and working-state bits are cached in the server process. Agent list (about 5 s) and working badge (about 3 s) hit the server once the cache is warm. Residual receipt queries still hit Postgres. The 500 ms figure in that note is delivery-verification polling, not by itself a Postgres round trip |
| A connection-string tweak can remove the round trips | Pooling and `No Reset On Close` are already the production settings (D-8). Neither removes a per-command round trip. Batching is code on CARD-1403 |
| Hangfire can own the daily job | Desktop `Hangfire:ServerEnabled` is true, storage is in-memory, jobs are re-registered in process at startup. A stopped server misses the fire |
| A backup job already exists | Repo-root `dev-backup.ps1` is manual plain SQL (`pg_dump -U antiphon -d antiphon --no-owner --no-acl`), zipped to gitignored `backups/*.sql.zip`, keeping 14. It takes no OneDrive path. Nothing in the repo schedules it. No Windmill backup job exists today. The sync root is `C:\Users\lndco\OneDrive`, a cloud-filter root with hydrated folders. C: had 256 GiB free |
| The land queue is drained by the graceful stop | Shutdown drains launches (`OperatorShutdownCoordinator`). Pending lands live in `LandRequestedAt` and resume on the next boot. The list filter is `GET /api/agent-tasks?landPending=true` |
| The repository lease is a database row | `RepositoryMutationLease` is in-process. An uncertain child journal under the git common directory still fences admission after the process is gone. Preview `scripts/recover-repository-children.ps1`. Do not pass `-Execute` in this window |
| Current size, CPU, OneDrive path, and desktop RTT are known | Known as of 2026-10-21 14:53 BST. Database 1,265,417,239 bytes. Container 1.26 GiB, CPU 6-18%. Postgres 16.14. pg_stat_statements 1.10 loaded. Desktop-to-server2 handshake 3.83 ms LAN and 6.72 ms Tailscale. OneDrive root `C:\Users\lndco\OneDrive`. Still unread: server2 `docker ps`, volumes, disk free, Docker data-root, Windmill compose parent, dump elapsed time, dump file size, `count(*)` per table, and a scratch restore |

## Measurement gates

Filled from the 2026-10-21 14:53 BST desktop pass. None of these reads logged a password or a connection string. A later pre-flight may re-read them. It does not treat a filled row as unknown.

| Fact | Value | Where it is used |
|---|---|---|
| Container memory | 1.26 GiB (about 1,290 MiB) | D-13. Under 2,048 MiB, so the cap stays 4 GiB |
| Container CPU | 6-18% | D-13. Under 400%, so the cap stays 4 CPUs |
| Database size | 1,265,417,239 bytes (1,207 MiB) | D-16 size gate met |
| Largest relations | TranscriptEntries 794 MB, AgentTasks 97 MB, SessionQueuedMessages 86 MB, AgentTaskLandNotifications 67 MB, AgentTaskEvents 55 MB, Alerts 34 MB, WorktreeResidueRunCandidates 25 MB, HostOccupancySamples 14 MB, WorkspaceUseReservations 11 MB, AgentSessions 9 MB | Phase 3 top five. The ten sum to 1,192 MB, 94% of the 1,265 MB decimal database size. The same pass also states TranscriptEntries at 757 MB in the size summary. Both figures are recorded. Phase 3 uses one definition, `pg_total_relation_size`, taken on the day |
| Server version | PostgreSQL 16.14 | D-6 image pin is still the desktop digest |
| pg_stat_statements | 1.10 loaded, max 5000, track=top, reset 2026-10-21 07:37Z | D-6. Mismatch after restore is a stop |
| OneDrive | `C:\Users\lndco\OneDrive`, cloud-filter sync root, hydrated folders. C: 256 GiB free | D-12 and stage B. B1 fails closed if this root is missing. Sign-in stays human-only |
| Desktop to server2 | Tailscale handshake median 6.72 ms (`100.93.77.126`, ssh alias `server2`). LAN handshake median 3.83 ms (`192.168.4.34`) | D-2 and D-11. LAN is evidence only |
| Loopback connect | Desktop to `:17280`, 5.24 ms | Handshake cost. Not a pooled query round trip |

Still gates. Do not invent them. Database reads, when a later task takes them, are `docker exec` into the source container as the local superuser `dev-backup.ps1` already uses, printing only the requested column.

| Gate | Read | Window rule |
|---|---|---|
| `{{SERVER2_HOST_DOCKER}}` | on the server2 host, not inside the runner: `docker ps`, `docker volume ls`, `df` of the Docker data-root | Confirm there is still no Antiphon Postgres volume and that free space is many times 1,265,417,239 bytes. A desktop timeout on every port except 22 does not answer this. The runner saw 449 GiB free on its own LV. The Docker data-root may be a different mount |
| `{{SERVER2_COMPOSE_PARENT}}` | parent directory of the live Windmill compose | D-3 |
| Dump file size | byte size of the `-Fc` archive and of the age ciphertext | Confirms the copy estimate. Not known from the database byte count alone |
| `count(*)` per user table | phase 3, source and target | Verify cost and the row-count witness. Relation size is not a row count |
| `{{DRILL_DUMP_RESTORE_SEC}}`, `{{DRILL_COPY_SEC}}` | the scratch restore into `antiphon-server2-postgres-drill`, then remove only that scratch volume | D-16. The 600 s automatic gate and the 30-minute envelope both wait on this |

`{{ANTIPHON_PROJECT_ID}}` is the Antiphon project id from `GET /api/agent-tasks/pipeline?projectId=` once the caller already knows the board's project. The card's board id is `8988ca03-7414-47ad-b0b6-51556c701703`. The pipeline samples in D-11 use that project scope so the sample is not the whole fleet. Script-test checkpoints do not pass `-Runner` or `-Platform`. `GET /api/runner-defaults` (revision 2, empty kind defaults) and `GET /api/session-runners` were read so this plan would not pin a host for those rows.

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

1. Fill every remaining gate in the measurement table. The filled facts stay as recorded unless a re-read disagrees, in which case the window log stores the new figure and D-13 or D-16 is reapplied.
2. Confirm `GET /api/version` SHA equals the main checkout `HEAD` and that `HEAD` is the current rc tip.
3. Confirm pool flags (D-8).
4. On the server2 host, confirm 5432 is still refused on the Tailscale address and on the LAN address, and that 5433 is still the only published database port.
5. Place the env file and the new password (operator). Start the empty `antiphon-server2-postgres` service. Exit check: healthcheck passes, and a TCP connect from the desktop to that Tailscale address on 5432 succeeds. Stop gate: the port is reachable from anywhere other than the tailnet, or the bind is `0.0.0.0`.
6. Take the first encrypted snapshot (B1's command) and restore it into a scratch project `antiphon-server2-postgres-drill` with its own volume. Run phase 3's queries against scratch versus source. Exit check: queries match. Then remove only the scratch container and the scratch volume.
7. Record `{{DRILL_DUMP_RESTORE_SEC}}` and `{{DRILL_COPY_SEC}}`. Apply D-16.
8. Run the C3 monitor long enough to store the baseline format, without treating that rehearsal as the window baseline.

Stop gate for leaving phase 0: any remaining gate empty, drill queries mismatch, D-16 missed, or the new listener reachable off the tailnet. No production writer has moved, so the response is to fix the rehearsal, not to roll back a cutover.

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
- key tables: `__EFMigrationsHistory` plus the top five by the reported relation size: TranscriptEntries (794 MB), AgentTasks (97 MB), SessionQueuedMessages (86 MB), AgentTaskLandNotifications (67 MB), AgentTaskEvents (55 MB). All five are over 32 MiB, so the check is count, minimum primary key, and maximum primary key. A relation at or under 32 MiB, if a later size list promotes one, uses an ordered checksum of the primary-key projection. Transcript text is not printed and is not copied into the log. The 757 MB TranscriptEntries figure from the size summary is not a second checksum target

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

See D-16. The number used in the operator window log is the recomputed `T_down`, not the 30-minute envelope, once the drill has run. The planning estimate before that drill is about 20 minutes in the central case, from a 1,207 MiB database and a LAN-speed copy. The window is not booked on the estimate alone.

What the users of the board experience: API, land, and dispatch errors for `T_down`. Runner sessions stay up and retry. UI polls fail because the server is down, not because Postgres is slow. After phase 4, those same calls pay about 6.7 ms per command, plus the time to move large result sets.

## Interim-state risks

The desktop server will pay about 6.7 ms per command for as long as stage D has not landed, plus the time to ship large results over the tailnet. CARD-1403 can shorten the number of commands and the size of those results. It is not required to start, and it may land before or after the window. The 72-127 ms reverse path is not this bill.

The statements below are `pg_stat_statements` since the reset at 2026-10-21 07:37Z, read at 14:53 BST (6 hours 16 minutes). Added time assumes one extra 6.7 ms round trip per call and ignores row bytes, which the handshake median does not measure.

1. Session-state pins select over AgentSessions, with an AgentTasks existence check. 374 calls, mean 1101 ms, about 169,000 rows. Total execution about 412 s. The extra round trips are about 2.5 s, under 1% of that total. Shipping about 169,000 rows per call is the risk a 30-minute watch can see. If the pipeline sample includes this statement, the localhost baseline is already near or above the 1 second floor, and D-11's 3× rule is the one that can fire.
2. EXISTS on TranscriptEntries for one session after a timestamp. 36,035 calls, mean 7.4 ms, about 1.6 calls per second, about 267 s of execution. One extra round trip roughly doubles the mean (about 14 ms) and adds about 240 s across that same window. This is the statement the 6.7 ms path actually changes. It does not approach 1 second per call, so it will not by itself trip the 1 second floor.
3. TranscriptEntries sequence, kind, text, and timestamp reads. 335 calls, mean 477 ms, about 18,000 rows (about 160 s). The round trip is noise. The row bytes are not. 4,461 calls, mean 26.5 ms, about 2,500 rows (about 118 s, about 0.2 calls per second). The round trip adds about 25% (about 30 s). Text in those rows is the larger unknown.
4. TranscriptEntries text LIKE. 198 calls, mean 765 ms, about 151 s. The round trip is noise.
5. Dispatch and land. Small indexed writes that are milliseconds on a pooled localhost connection become about 6.7 ms each. That is slower. It is not, by itself, a reason those calls fail. D-11's failure count is for faults, not for this expected delay. CARD-1403 targets this class of chattiness.
6. CARD-0701's cached UI polls do not multiply database round trips while the cache is warm. They are the wrong signal for a rollback.
7. SignalR fan-out is in-process after the write that publishes the event. The negotiate check proves the hub is up. It does not measure database cost.
8. Hangfire does not use this database. The risk is the missed in-memory fire during `T_down`, not a slow job store.

The 2026-10-08 receipt-scan sample (about 2.5 large selects per second at 45-81 ms) is an older shape. It is not the ranking above. CPU on the desktop container was 6-18% at this reading, not the 2026-09-25 multi-core scan.

Config that reduces round trips immediately: none beyond what is already on. Keep pooling and `No Reset On Close`. Do not flip `Max Auto Prepare` or multiplexing in the window (D-8). Further reduction is CARD-1403.

Rollback trigger: D-11. A pre-window Tailscale handshake median above 20 ms aborts before the dump. After phase 5, the pipeline median has to clear both 1 second and 3× the localhost baseline, or the failure count has to exceed the baseline by more than 2. The accepted 6.7 ms round trip is below that pair of lines. The old volume is the rollback copy. It stays unmounted from any writer after phase 4 until phase 6 starts it.

## Human-only steps

Agents and the cutover script do not perform these.

| Step | Why it stays human |
|---|---|
| Choose, store, and rotate passwords | Custody. The new server2 password is required before the listener starts. Desktop rotation is a separate decision, required only if 17280 is published again |
| `docker volume rm` of `antiphon_pgdata` or of `antiphon-server2-postgres_pgdata` | The card forbids volume deletion without an explicit operator approval. The script has no such command |
| Publish Postgres on `0.0.0.0`, on the LAN, or through Cloudflare | Operator decision. D-4 binds the tailnet address only |
| OneDrive sign-in | The job writes `C:\Users\lndco\OneDrive\antiphon-postgres`, which it creates. It does not log in. Confirming the sync client is signed in stays human-only |
| Enable the Windmill schedule | Registration creates it disabled. Enabling is the same kind of operator step as the release-gate schedules |
| Pass `-ConfirmWindow` | Agrees the downtime and the D-11 thresholds for that day |
| Decide phase 6 when the new database has rows the old volume lacks | The script stops and asks |

Stopping AppHost inside an agreed window is `-StopOnly` plus the later `restart-apphost.ps1`. It is not a surprise restart. Orchestrator autonomy for ordinary restarts does not extend to this window.

## Stage B snapshot job

This section is the design B1 implements. It is independent of the move and should be filed in parallel with CARD-1403.

| Item | Design |
|---|---|
| Script | `scripts/postgres-snapshot.ps1`. ASCII-only. Default `-WhatIf` prints the dump, encrypt, and prune steps and runs no `docker`. `-Execute` is the real run |
| Dump | `docker exec` into the current source container, `pg_dump -Fc --no-owner --no-acl`. The job runs where the data is. While that is the desktop, the container is `antiphon-postgres`. After C, `-Source` selects the server2 host over SSH and the dump stays next to the data |
| Encrypt | `age -r <recipient file>`. Missing `age`, missing recipient, or a recipient path inside `C:\Users\lndco\OneDrive` fails closed with no output file |
| Destination | `C:\Users\lndco\OneDrive\antiphon-postgres\antiphon-<utc>-YYYYMMDDTHHMMSSZ.dump.age`. The sync root was measured. The job creates the child. Write the ciphertext on C: outside the sync root, then move it in |
| Free space | C: had 256 GiB free. Fourteen dumps at the full 1,207 MiB database size are about 17 GiB. The job still fails closed when the sync root is missing |
| Retention | 14 `*.dump.age` files in that directory. Prune only after the new file is fully written and `age` has been asked to decrypt the header (no plaintext dumped). Fewer than one file left is a failed prune, not a success |
| Status | `snapshot-status.json` beside the dumps, containing the UTC time, the script exit, and the byte size. No connection string, no key, no row contents |
| Schedule | `scripts/windmill/antiphon-postgres-snapshot.json` and `.schedule.json`, path `u/lndcobra/antiphon_postgres_snapshot`, desktop worker, daily `0 30 1 * * *` Europe/London. Created disabled |
| Register | `scripts/register-postgres-snapshot.ps1`, preview by default, `-Apply` registers, `-EnableSchedule` is the human enable |
| Alert | Non-zero script exit is a failed Windmill job. B1 checks whether that workspace already notifies on job error and records the answer. This card does not add a chat sink. A success file older than 26 hours is the second signal, read from `snapshot-status.json` |
| Drill | Scratch project on the server2 host, `pg_restore`, phase 3 queries, delete only the scratch volume. One recorded drill is the phase 0 witness |

`dev-backup.ps1` stays the manual plaintext tool. The scheduled path does not call it.

## Slices

Filed as Backlog cards on Antiphon. Each runs its own pipeline. Descriptions name CARD-1401 and CARD-1403. Production data movement stays inside C2's `-Execute -ConfirmWindow`, which is the operator window, not a Code-stage side effect.

### B1. Daily encrypted snapshot

Card: CARD-1405 (`807e41f4-a15a-4cb3-ad0e-99f5e1372297`).

Scope: `scripts/postgres-snapshot.ps1`, `scripts/test-postgres-snapshot.ps1`, `scripts/register-postgres-snapshot.ps1`, `scripts/windmill/antiphon-postgres-snapshot.json`, `scripts/windmill/antiphon-postgres-snapshot.schedule.json`, a short paragraph in `scripts/windmill/README.md`.

Tests, named for test-design to close:

- `-WhatIf` exits 0, prints the destination pattern, and does not invoke `docker`.
- Missing `age` or a recipient path inside the destination directory exits non-zero and leaves no file.
- Prune keeps 14 and never runs when the new file was not written.
- The Windmill schedule payload is created with `enabled` false.

The scratch restore on server2 is C1's drill using this script's output. B1's own tests use a fake `docker` and a temp directory.

### C1. server2 Postgres service and the restore drill

Card: CARD-1406 (`7bc88cc8-776b-4acc-8d62-842dbfd8cd28`).

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

Card: CARD-1407 (`391c5685-b9fe-4f3b-97d8-75502b2d4ef9`).

Scope: `scripts/cutover-postgres-server2.ps1`, `scripts/test-cutover-postgres-server2.ps1`, and `-StopOnly` on `scripts/restart-apphost.ps1` plus the existing AppHost script test that covers it.

Tests:

- Default and `-WhatIf` exit 0, list phases 0 through 6, and do not POST, do not call `docker`, and do not call `dotnet user-secrets set`.
- `-Execute` without `-ConfirmWindow` exits non-zero and does none of those either.
- The script source has no `volume rm`, no `docker volume rm`, and no `-AllowWorktree`.
- `-StopOnly -WhatIf` on the restart script does not POST and does not reach the relaunch. The real `-StopOnly` test is the one the AppHost script tests already use for process stubs: graceful POST, then exit before `dev-aspire.ps1`.
- Given a fixture `landPending` count other than zero, the quiesce phase refuses before any stop.

### C3. Post-cutover monitor

Card: CARD-1408 (`f674d073-f94a-4ffa-841d-5bb2ccbc54d2`).

Scope: `scripts/postgres-cutover-monitor.ps1`, `scripts/test-postgres-cutover-monitor.ps1`. The baseline file is gitignored, same as other generated evidence.

Tests, pure functions over fixture samples, no live HTTP:

- Median above both 1 second and 3 times the baseline median exits 2 and prints `ROLLBACK`.
- Median above 3 times a tiny baseline but under 1 second exits 0 (the floor in D-11).
- Failure count more than 2 above baseline exits 2.
- The script source does not call `restart-apphost.ps1`, `docker`, or `user-secrets`.

The 30-minute live watch is phase 5, operator-run.

## Proposed checkpoints

Test-design closes this list. These rows are the proposal, not the Code-stage closed list. Lane for every row: unpinned. Do not pass `-Runner` or `-Platform`. Driver: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <row> -- pwsh -NoProfile -File <script>`. No `Antiphon.Tests` project, no production database, no server2 host socket, no AppHost restart.

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
