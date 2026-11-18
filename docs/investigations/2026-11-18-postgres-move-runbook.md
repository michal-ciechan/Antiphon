# Postgres move to server2: the runbook as it exists today (2026-11-18)

Investigate task `3b15e001`. Read-only. Observations 2026-11-18 05:55–06:30Z from the server2 runner container.
Master read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (origin/master; it carries the CARD-1974/1975 cutover fixes `c1844188b`, `5a3d05c9c`, `5adc19a7f`).
Inputs: `docs/investigations/2026-11-17-postgres-to-server2-move-status.md` (a84df3ad), the CARD-1401 plan `docs/superpowers/plans/2026-10-21-card-1401-postgres-to-server2-plan.md`, the CARD-1844 investigation `docs/investigations/2026-11-12-card-1844-plan-move-the-apphost-postgres-from-the-desktop-to-server2.md`, `docs/docker-stack.md` (CARD-1406 section), the CARD-1974/1975 Review r2, and the scripts and compose files cited below.

What I ran:

- No SQL against any database. No desktop Docker command. No restart, no deploy, no volume change, no secret read.
- TCP connects only. One of them went to the desktop's published Postgres port and carried no protocol bytes.
- Anonymous Docker Hub registry reads, to resolve the image tags to digests.
- The `-WhatIf` previews of both scripts. One `-Execute` with no env file, which refused before any docker call (exit 2, `env-missing`).
- One throwaway compose project (`c3b15-probe`, tmpfs data dir, no volume) on the runner's **nested** daemon. It checked one compose behaviour, and I removed it afterwards.

The desktop image facts in the version section come from the brief. I did not observe them myself.

## Verdict

**The runbook can be run, but four confirmed defects must be fixed (or worked around by hand) before the 60-minute window.** None of them blocks deploying the empty cluster or running the restore drill today.

1. **The restart in phase 4 starts the old desktop Postgres again.**
   - `restart-apphost.ps1` runs `dev-aspire.ps1` (`scripts/restart-apphost.ps1:107`). That script runs `docker compose -f docker-compose.dev.yml up -d` every time (`dev-aspire.ps1:80-83`); so does `scripts/autostart-apphost.ps1:88`.
   - So the cutover script stops `antiphon-postgres` (`scripts/cutover-postgres-server2.ps1:1022-1041`), and its own restart one line later (`:1044`) brings it back up. The volume is unchanged.
   - This breaks the plan's fail-closed rule D-15 (plan `:186-190`): a missed override now silently keeps using the old database.
   - Phase 5 still catches that case, through its "server2 shows a desktop client" check (`:1069-1071`).
   - It also leaves a stale but healthy `antiphon-postgres` running. From then on, the daily 07:50 backup "succeeds" against frozen data (see the next defect).
2. **The daily backup does not follow the database.**
   - The Windmill wrapper hard-codes `-Plain -Execute -OneDriveCopy` (`scripts/windmill/invoke-postgres-snapshot.ps1:67-69`). The snapshot script defaults to `-Container antiphon-postgres` (`scripts/postgres-snapshot.ps1:54`) and refuses a non-local Docker endpoint unless `-AllowRemoteDocker` is passed (`:740-756`).
   - Retargeting needs a code change plus a new `SOURCE-SHA` pin (`invoke-postgres-snapshot.ps1:20-25`). Until then the job must be disabled in the window, or it will back up the stale desktop copy.
3. **The scripted rollback cannot run when the server2 database is down.**
   - `Invoke-CutoverRollback` compares content fingerprints on both sides before anything else (`:872-875`). A failed target read returns `$null`, and that refuses with no override: `-AcceptDataLossRollback` is consulted only when the result is `$true` (`:876`).
   - The main reason to roll back, server2 Postgres being unreachable, therefore needs the **manual** rollback in §A.6.
   - After any live traffic the fingerprints always differ, so a post-traffic rollback always needs `-AcceptDataLossRollback`, and loses those writes.
4. **Image pin: `postgres:16` today is 16.16, not 16.14.**
   - The registry's current `16.14` build is not the desktop image ID. The deploy script also rejects the readable form `postgres:16.14@sha256:…` (see the version section).
   - The pin must be taken from the desktop's RepoDigest, or else from the `16.14` index digest written as `postgres:16@sha256:…`.

Everything else in the gap list is hardening, documentation, or a host prerequisite that is still unobserved.

## Version rule (operator 2026-11-18): same PostgreSQL, no upgrade

Registry reads, 2026-11-18 ~06:05Z (`registry-1.docker.io`, `library/postgres`):

| Tag | Index digest (what `@sha256:` pins) | amd64 config digest (= local image ID) | `PG_VERSION` in image env | Built |
|---|---|---|---|---|
| `16.14` and `16.14-trixie` (same) | `sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` | `sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55` | `16.14-1.pgdg13+1` | 2026-08-05 |
| `16.14-bookworm` | `sha256:64154d0babcb1741988719e703419af0382b19953706149f9872fbd0f438efa8` | `sha256:b255e1e7…` | `16.14-1.pgdg12+1` (**not** the desktop: pgdg12 = Debian 12) | 2026-08-05 |
| `16` and `16.16` (same) | `sha256:dd2bf3ce2dc83a81674cf7f217fcfd8a5009a4b325dda5b843bfee82a06cff6f` | `sha256:6b7038c6…` | `16.16-1.pgdg13+1` | 2026-11-12 |
| `16.15` | `sha256:f9a7b9eb…` | — | 16.15 | — |
| `16-alpine` | `sha256:81e77a48…` | — | 16.x on musl | — |

- **The exact tag that gives 16.14 on pgdg13 is `postgres:16.14`, which is the same image as `postgres:16.14-trixie`.** Debian 13 "trixie" is pgdg13. `16.14-bookworm` is pgdg12 and does not match.
- **The desktop image ID in the brief, `sha256:f40a5645…`, matches no current amd64 config digest for these tags.** (It does not match the index digest either, which is what Docker Desktop's containerd store would show.) The desktop therefore pulled a different build of 16.14 than the one the `16.14` tag points to now.
  - That build's provenance is **UNOBSERVED**.
  - It reports the same `PG_VERSION` string, so the PostgreSQL package is identical. The base-layer differences are **UNOBSERVED**.
  - To resolve it, run `docker image inspect postgres:16 --format "{{json .RepoDigests}}"` on the desktop. That is a metadata read with no DB load.
- **Pin, in order of preference:**
  1. `ANTIPHON_POSTGRES_IMAGE=postgres@sha256:<desktop RepoDigest>` (bit-identical, if it is still pullable).
  2. Otherwise `ANTIPHON_POSTGRES_IMAGE=postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`.
- **Do not write `postgres:16.14@sha256:…`.** `Test-Server2PostgresImage` accepts only `^postgres:16@sha256:` or `^postgres@sha256:` (`scripts/deploy-server2-postgres.ps1:71-78`). I dot-sourced the function and measured it:
  - `postgres:16@sha256:9520…` → accepted
  - `postgres@sha256:9520…` → accepted
  - `postgres:16.14@sha256:9520…` → `image-floating`
  - `postgres:16.14-trixie@sha256:9520…` → `image-floating`
- **A plain `docker pull postgres:16` today gives 16.16.**
  - Unpinned places that would float: `docker-compose.dev.yml:17`, `docker-compose.yml:104`, the snapshot drill image `scripts/postgres-snapshot.ps1:70`, and the cutover witness `scripts/test-cutover-postgres-verify.ps1:564-566`. The Review r2 measured its rows on `postgres:16.16`.
  - The desktop keeps 16.14 only because it has not pulled again. A `docker compose pull` there would upgrade it silently.
- **How the review and test images differ from production:**
  - `postgres:16.16`: two minor releases newer. Same on-disk format; bug and security fixes. `pg_stat_statements` stays 1.10 within major 16 (the desktop read 1.10 on 2026-10-21, plan `:229`). Witness results carry over except for minor-specific fixes.
  - `postgres:16-alpine` (`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:27`, `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:30`, `SmokeTests.cs:28`): **musl libc instead of glibc**. Locale and collation behaviour for `en_US.utf8` text differs, so text `ORDER BY` and text index order can differ from production. The plan already forbids alpine in production (D-6, plan `:60-64`); the deploy script refuses it (`image-alpine`).
- **Settings. Both the desktop and the server2 compose set only:**
  - `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`
  - Desktop: `docker-compose.dev.yml:18-25`. Server2: `docker-compose.server2-postgres.yml:12-19`. Drill: `docker-compose.server2-postgres-drill.yml:10-17`.
  - Neither sets `shared_buffers`, `max_connections` or `shm_size`, so both get the image and initdb defaults: `shared_buffers=128MB`, `max_connections=100`, `/dev/shm` 64 MB (Docker default), `dynamic_shared_memory_type=posix`.
  - **Deliberate difference:** server2 has `mem_limit: 4g` and `cpus: 4.0` (`:21-22`); the desktop has no limit.
  - **UNOBSERVED:** any `ALTER SYSTEM` (`postgresql.auto.conf`) or `ALTER DATABASE … SET` on the desktop cluster. Neither travels in a `pg_dump -Fc` without `--create`. The repo contains no `ALTER SYSTEM`/`ALTER DATABASE`.
  - JIT is turned off by the application on every connection (`-c jit=off`, `server/Infrastructure/Data/ApplicationPostgresConnection.cs:18`, applied at `server/Program.cs:128`), so it needs no cluster setting.
- **The later major upgrade is filed as CARD-1988** (Backlog): "Upgrade server2 Postgres from 16.14 to the latest major after the move is stable". It needs its own plan (pg_upgrade or dump-restore), its own window and its own rollback, and it must not start before the move is stable.

## A. Run-book

Hosts:

- **D** = the desktop main checkout `C:\src\Antiphon` (pwsh 7).
- **S** = the server2 **host** shell as `mc` (ssh `mc@server2`), never the runner container. The deploy script refuses inside a container (`/.dockerenv`, `scripts/deploy-server2-postgres.ps1:310`).

`<…>` marks operator values. Never print them.

### A.1 Deploy the empty cluster (operator; safe during the live run; touches only server2)

1. **S, host preflight.** Read-only.

   ```bash
   docker info -f '{{.Name}} {{.ServerVersion}} {{.DockerRootDir}}'
   df -h "$(docker info -f '{{.DockerRootDir}}')"     # gate: >= 20 GiB free (operator decision 2)
   docker compose version                             # -p <project> exec without a file was proven on v2.32.4 only
   docker ps --format '{{.Names}}\t{{.Ports}}'; docker volume ls | grep -i postgres
   ss -ltnp | grep -E ':(5432|5433|5434)\b'            # 5432 must be free; 5433 is Windmill
   tailscale ip -4                                     # must print exactly one line (expect 100.93.77.126)
   command -v pwsh age                                 # pwsh: deploy -Execute; age >= 1.1: cutover phase 2 decrypts with `age -d -i -`
   nproc; lscpu | grep -E 'NUMA node[01]'
   ```

   - Runner-side readings at 05:55Z: overlay `/` 705G total, **250G available (64%)**; 24 CPUs (2 × X5690, 2 NUMA nodes, SMT pairs `n,n+12`, e.g. cpu10/cpu22 = pkg 1 core 10). The host data-root itself is **UNOBSERVED**.
2. **S, stage the files.** Copy `docker-compose.server2-postgres.yml` and `docker-compose.server2-postgres-drill.yml` from the main checkout at a recorded master SHA into `/home/mc/antiphon-server2/postgres/`. That directory name is a proposal (mode 0700); the host has no tracked checkout (**UNOBSERVED**). Record `sha256sum`.
   - **CPU isolation (operator decision 3)** is not in the tracked compose (`docker-compose.server2-postgres.yml:21-22` is a CFS ceiling only). It is an **override file** in that directory (gap G-5):

     ```yaml
     # docker-compose.server2-postgres.cpuset.yml (proposal; not tracked yet)
     services:
       postgres:
         cpuset: "10,22,11,23"   # two physical cores, one per NUMA node; or "10,22,8,20" to stay on node 1
     ```

   - A `cpuset` only *pins* Postgres. It does not *reserve* the cores: the runner compose has no `cpuset`/`cpus` at all (no match in `docker-compose.server2-runner.yml`). True reservation also needs the runner's `cpuset` to exclude those CPUs. That is a runner redeploy, which is the orchestrator's rolling phase.
   - The deploy script's compose check accepts added `cpuset:`/`shm_size:` lines (measured: `Get-Server2PostgresComposeRefusal` returned no refusal). It does not read `-f` override files, though: `-Execute` runs only `-f <one file>` (`:327`). Run the printed command by hand with both `-f` options, or merge the key into the tracked file through a Code card.
3. **S, env file** (operator; custody).
   - Path: `/home/mc/antiphon-server2/postgres.env`, `chmod 600`, outside any repo. The script refuses `env-inside-repo` and `env-mode` (`:401-408`).
   - Three keys:
     - `ANTIPHON_POSTGRES_BIND_ADDRESS=<tailscale ip -4>`
     - `ANTIPHON_POSTGRES_IMAGE=<pin from the version section>`
     - `POSTGRES_PASSWORD=<new, random, not antiphon_dev>`; the script refuses the dev password (`:425-429`).
   - Generate the password into the file, e.g. `umask 077; printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" >> postgres.env`. Never echo it.
4. **S, preview:** `pwsh -NoProfile -File scripts/deploy-server2-postgres.ps1 -WhatIf -EnvFile /home/mc/antiphon-server2/postgres.env -ComposeFile /home/mc/antiphon-server2/postgres/docker-compose.server2-postgres.yml`.
   - Expected (measured here without an env file): `docker compose --env-file … -p antiphon-server2-postgres up -d`, then `SERVER2-POSTGRES plan exit=0 reason=ok`.
5. **S, execute:** the same command with `-Execute` instead of `-WhatIf`.
   - Requires `pwsh` on the host, the host lane, and a bind equal to `tailscale ip -4` (`:444-459`).
   - Expected `SERVER2-POSTGRES ok exit=0 reason=ok`.
   - Without host pwsh, run the printed `docker compose --env-file … -f … -p antiphon-server2-postgres up -d` by hand. With the cpuset override, add the second `-f`.
6. **D or runner, reachability:** a TCP connect to `<bind>:5432` must now succeed (today `ECONNREFUSED` on both `100.93.77.126:5432` and `192.168.4.34:5432`, 05:55Z). LAN `192.168.4.34:5432` must stay refused (tailnet-only bind).

### A.2 Verify the empty cluster (operator on S; orchestrator can read the results)

```bash
C=antiphon-server2-postgres-postgres-1
docker inspect -f '{{.Config.Image}} {{.Image}} cpuset={{.HostConfig.CpusetCpus}} nanocpus={{.HostConfig.NanoCpus}} mem={{.HostConfig.Memory}} shm={{.HostConfig.ShmSize}} restart={{.HostConfig.RestartPolicy.Name}} health={{.State.Health.Status}}' $C
docker exec $C psql -U antiphon -d antiphon -Atc "select version()"            # expect PostgreSQL 16.14 (Debian 16.14-1.pgdg13+1)
docker exec $C psql -U antiphon -d antiphon -Atc "select name||'='||setting||coalesce(unit,'') from pg_settings where name in ('server_version','shared_buffers','max_connections','shared_preload_libraries','pg_stat_statements.track','pg_stat_statements.max','dynamic_shared_memory_type','jit','TimeZone','password_encryption','ssl','listen_addresses') order by 1"
docker exec $C psql -U antiphon -d antiphon -Atc "select name, default_version from pg_available_extensions where name='pg_stat_statements'"   # 1.10; the extension itself arrives with the restore
docker exec $C psql -U antiphon -d antiphon -Atc "select datcollate, datctype, datlocprovider, encoding from pg_database where datname='antiphon'"
docker exec $C df -h /dev/shm                                                   # 64M unless shm_size was set
docker exec $C tail -n 3 /var/lib/postgresql/data/pg_hba.conf                   # entrypoint appends: host all all all scram-sha-256
```

Expected:

- `shared_buffers=16384` × 8kB (128 MB) and `max_connections=100`.
- `password_encryption=scram-sha-256`, `ssl=off`.
- Auth is scram over any source address. The network boundary is only the tailnet bind; TLS is deliberately off (plan D-4, `:42-52`).

After the live run ends, read the same `pg_settings` query (plus `select name, setting, source from pg_settings where source not in ('default','override') order by 1`) once from `docker exec antiphon-postgres` on the desktop, and diff the two. That is the only way to catch a desktop `ALTER SYSTEM`. It is **not** run by this task.

### A.3 Restore drill from the nightly dump (operator; after the live run f2de5b30 for the copy, any time for the server2 side)

The nightly file is `C:\Users\lndco\antiphon-backups\antiphon-<yyyyMMddTHHmmssZ>.dump`: plain `pg_dump -Fc --no-owner --no-acl`, unencrypted (`scripts/postgres-snapshot.ps1:12-15`, `:79-80`, `:793`). A copy is in `C:\Users\lndco\OneDrive\Backups\antiphon-postgres`. The brief gives 708 MB on 2026-11-17 07:50Z; I did not observe it.

**D (copy, timed):**

```powershell
$f = Get-ChildItem C:\Users\lndco\antiphon-backups -Filter 'antiphon-*.dump' | Sort-Object Name -Descending | Select-Object -First 1
(Get-FileHash $f.FullName -Algorithm SHA256).Hash; $f.Length
ssh mc@server2 "mkdir -p -m 0700 /var/tmp/antiphon-server2-postgres-drill"
$copy = Measure-Command { scp $f.FullName "mc@server2:/var/tmp/antiphon-server2-postgres-drill/$($f.Name)" }; "copy-elapsed-sec=$([int]$copy.TotalSeconds)"
```

**S (restore into scratch only, timed):**

```bash
cd /home/mc/antiphon-server2/postgres
D=/var/tmp/antiphon-server2-postgres-drill; F=$(ls -1t $D/antiphon-*.dump | head -n1); chmod 600 "$F"; sha256sum "$F"
docker compose --env-file ../postgres.env -f docker-compose.server2-postgres-drill.yml -p antiphon-server2-postgres-drill up -d --wait
C=antiphon-server2-postgres-drill-postgres-1
t0=$(date +%s); docker exec -i $C pg_restore -U antiphon -d antiphon --no-owner --no-acl < "$F"; echo rc=$? restore-elapsed-sec=$(( $(date +%s)-t0 ))
docker exec $C psql -U antiphon -d antiphon -Atc "select pg_database_size('antiphon')"
docker exec $C psql -U antiphon -d antiphon -Atc "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='public' and c.relkind='r'"
docker exec $C psql -U antiphon -d antiphon -Atc "select extname||' '||extversion from pg_extension order by 1"
docker exec $C psql -U antiphon -d antiphon -Atc 'select "MigrationId" from "__EFMigrationsHistory" order by 1' | tail -n 3
# exact counts, not n_live_tup (CARD-1406 R3 advisory): one count(*) per key table, no row text
for t in TranscriptEntries AgentTasks SessionQueuedMessages AgentTaskLandNotifications AgentTaskEvents; do docker exec $C psql -U antiphon -d antiphon -Atc "select '$t', count(*) from \"$t\""; done
shred -u "$F"
docker compose --env-file ../postgres.env -f docker-compose.server2-postgres-drill.yml -p antiphon-server2-postgres-drill down
docker volume rm antiphon-server2-postgres-drill_pgdata    # the scratch volume only; never antiphon-server2-postgres_pgdata
```

- The drill compose needs only `ANTIPHON_POSTGRES_IMAGE` and `POSTGRES_PASSWORD`. The extra bind key is ignored. It publishes `127.0.0.1:5434` only (`docker-compose.server2-postgres-drill.yml:26`).
- It runs beside the production cluster, so the data root needs room for **two** copies (~3.2 GB each today, plus WAL during restore).

**Drill record.** Phase 0 requires a JSON file (`scripts/cutover-postgres-server2.ps1:192-222`). No script writes it (gap G-3). Write by hand, on D:

```json
{"passed": true, "latencyMedianMs": <median desktop->server2 tailnet connect, ms>, "dumpRestoreSec": <dump s + restore s>, "copySec": <copy s>, "source": "<dump file name + sha256>", "measuredAtUtc": "<iso>"}
```

- `latencyMedianMs` comes from `pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 -Mode PreWindow -Execute -Server2Host <bind> -LanHost 192.168.4.34` on D. The default abort is above 20 ms (`:99`).
- `dumpSec` is not in this drill, because the nightly dump was not timed by me. Take it from the 07:50 Windmill run duration (**UNOBSERVED** here), or estimate it from 2026-11-10 (78.1 s for 2.10 GB). Phase 0 refuses `dumpRestoreSec + copySec > 600` without `-AcceptDrillTime`, and refuses above 1800 outright (`:215-221`).

**What this drill does not rehearse** (gap G-3):

- Phase 2 pipes `pg_dump` into `age` (`:793-840`). The dump is therefore written to a non-seekable stream, has no data offsets, and is restored through `age -d -i - | docker compose … exec -T postgres pg_restore` (`:848`).
- The nightly file was written with `-f` (seekable).
- Rehearse the exact pipe once, with any age key pair, to prove serial restore from a non-seekable custom archive, and that host `age` accepts `-i -`.

### A.4 The 60-minute cutover window (operator runs it; times from script start T)

Prerequisites, all before T-15:

- The live run f2de5b30 has ended.
- A.1–A.3 are passed and the drill record is written.
- The desktop `ssh -o BatchMode=yes server2 true` succeeds. `Server2SshHost` defaults to `server2` (`:58`), which the plan resolves to `100.93.77.126` (plan `:26`).
- age recipient and identity files exist on D.
- **Every one-line SHA below is the running rc tip** (`GET /api/version`).

| When | Step | Command (D unless noted) | Gate / expected |
|---|---|---|---|
| T-15 | Freeze inputs | Write `C:\Users\lndco\antiphon-cutover\conn.txt`: one line, `Host=<bind>;Port=5432;Database=antiphon;Username=antiphon;Password=<same as env file>;No Reset On Close=true` | The operator types it. Never echoed. **Must keep `No Reset On Close=true`** or phase 5 fails (`:1066-1068`) |
| T-15 | Backup job | Operator: **disable** Windmill `u/lndcobra/antiphon_postgres_snapshot` (and `_stale`) until G-4 lands | Otherwise 07:50 dumps the stale desktop copy |
| T-12 | Baseline | `pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 -Mode Baseline -Execute -ProjectId <id> -BaselinePath <file>` | 20 samples; this is the D-11 baseline |
| T-8 | Drain | Wait for `GET /api/agent-tasks?projectId=<id>&landPending=true` to return 0 items. Stop new dispatch | Phase 1 refuses otherwise (`:959-966`) |
| T-5 | Preview + token | `pwsh -NoProfile -File scripts/cutover-postgres-server2.ps1 -WhatIf -Repository C:\src\Antiphon` | Prints phases 0–6 and `CONFIRM TOKEN <8hex>` (measured here; the token binds machine, UTC day, script SHA and `-Repository`, `:106-118`) |
| T+0 | Run | `pwsh -NoProfile -File scripts/cutover-postgres-server2.ps1 -Execute -ConfirmWindow -ConfirmToken <8hex> -Repository C:\src\Antiphon -RcTip <40-hex> -ProjectId <id> -SnapshotReference <nightly dump path> -DrillRecord <json> -ConnectionSecretFile <conn.txt> -DumpDirectory <dir> -AgeRecipientFile <r> -AgeIdentityFile <i> -Server2ComposeDir /home/mc/antiphon-server2/postgres -TailnetAddress <bind> -TailnetOnlyConfirmed -HoldAgent <orchestrator> -HoldReason 'postgres cutover' -HoldUntil <iso>` | — |
| T+0–1 | Phase 0 | (script) | Drill record, `/api/version` = rc tip + `land-v2`, pool flags (Pooling true, NoResetOnClose true, MaxAutoPrepare 0, Multiplexing false), TCP connect to `<bind>:5432` (`:899-939`) |
| T+1–5 | Phase 1 | (script) | Maintenance marker, keep-six hold, landPending 0, `recover-repository-children.ps1` clean, `restart-apphost.ps1 -StopOnly` exit 0 (`:941-984`). Hangfire stops with the server. Its InMemory storage (`server/Infrastructure/Agents/HangfireConfiguration.cs:190-193`; no `Hangfire:Storage` in `server/appsettings.json:188-191`) loses queued jobs. Recurring jobs re-register at start (plan `:212`). The runner (17204) and its sessions stay up |
| T+5–8 | Phase 2a | (script) `pg_dump -Fc` piped into `age` on D | No other backend on `antiphon` (`:986`); otherwise it restarts on the old string and refuses |
| T+8–10 | Phase 2b | (script) `scp` to `<Server2ComposeDir>`, then `ssh` with `age -d -i - \| docker compose -p antiphon-server2-postgres exec -T postgres pg_restore` | `pg_restore` exit 0 (`:842-862`). The target must be **empty**: a retry needs G-7 |
| T+10–16 | Phase 2b restore | (server2) | Estimate 3–6 min on 4 CPU-quota X5690 (see §D) |
| T+16–24 | Phase 3 | (script) | Table names, `count(*)` per table, sequences (`last_value`/`is_called` from each relation, CARD-1974, `:606-660`), extensions, migrations, key-table count/min/max (`:673-700`). One `ssh` per target query |
| T+24–32 | Phase 4 | (script) `docker compose stop postgres`, check `antiphon_pgdata` listed, container `exited`, `dotnet user-secrets set` via stdin JSON (`--id aspire-antiphon-apphost`, key `ConnectionStrings:DefaultConnection`, `:508-546`), `restart-apphost.ps1 -ExpectedServerSha <rc>` | Exit 0. **Defect 1: this restart re-ups `antiphon-postgres`** |
| T+32–34 | Phase 5 | (script) | `/health` 200, version unchanged, NoResetOnClose, server2 `pg_stat_activity` has a client with `client_addr` (`:1053-1072`). Prints `OPERATOR: confirm one dispatch, one land, Hangfire, and the 30 minute monitor` |
| T+34 | Manual | `docker compose -f docker-compose.dev.yml stop postgres` | Puts D-15 back by hand until G-1 lands. Never `rm`, never `down -v` |
| T+34–45 | Post-checks | §A.5 | — |
| T+45–55 | Backup | Re-enable the job only once it targets server2 (G-4). Otherwise leave it disabled, and take one manual server2 dump (§A.5 item 6) | Decision 6 |
| T+55–60 | Buffer | — | A rollback started now overruns the window by ~10–15 min (§D) |

### A.5 Post-cutover verification (orchestrator can run all of it; read-only HTTP)

1. `GET /health` → 200. `GET /api/version` → the rc tip SHA and `land-v2`.
2. `GET /api/diagnostics/session-state` → `pool.NoResetOnClose=true`, uptime reset. Check the AppHost log line for the database endpoint, if CARD-1844 S1 is in the running rc. S1 is **unlanded** (2026-11-17 note, `:53`), so today there is no such line.
3. Hangfire: `GET http://localhost:17202/hangfire` (dashboard, `server/Program.cs:1320`) → 1 server, recurring jobs listed, no failed jobs in the first 10 min.
4. Task dispatch canary:
   - Dispatch one cheap delegate (`delegate.ps1`, Investigate or Docs, server2 runner) on a throwaway question.
   - Confirm it reaches Working with transcript UserPrompt evidence, and settles.
   - Confirm one land completes if one is queued.
   - The monitor counts failed tasks and lands in its window.
5. Monitor: `scripts/postgres-cutover-monitor.ps1 -Mode Watch -Execute -ProjectId <id> -BaselinePath <file>` at +10, +20 and +30 min.
   - `ROLLBACK` (exit 2) when the pipeline median is above both 1 s and 3× baseline, or failures exceed baseline by more than 2 (plan D-11, `:123-157`).
6. Backup on the new source, one manual run:
   - On S: `docker exec antiphon-server2-postgres-postgres-1 pg_dump -Fc --no-owner --no-acl -U antiphon -d antiphon -f /tmp/m.dump && docker exec antiphon-server2-postgres-postgres-1 pg_restore --list /tmp/m.dump | grep -c 'TABLE DATA'`
   - Then copy it off-host. Retarget the daily job (G-4).
7. Desktop: `docker ps --filter name=antiphon-postgres` shows Exited, and `docker volume ls` still lists `antiphon_pgdata`.

### A.6 Rollback

**Scripted** (server2 DB reachable):

`cutover-postgres-server2.ps1 -Rollback -Execute -ConfirmWindow -ConfirmToken <8hex>` plus **all** the window inputs. `Assert-CutoverOperatorInputs` runs first (`:865`), and that includes the age and secret files.

It:

- starts desktop Postgres (`:870`);
- compares content fingerprints, refusing on any difference unless `-AcceptDataLossRollback` is passed (`:872-881`);
- removes the user-secret (`:885`);
- restarts with `-ExpectedServerSha` (`:886`);
- lifts maintenance.

Gate 6 costs a full read of every table on both sides. Review r2 measured 17.3 s on a 1.49 GB table, on postgres:16.16.

**Manual** (server2 DB down: the scripted path refuses, defect 3). On D, main checkout, all from the operator:

```powershell
pwsh -NoProfile -File scripts/set-apphost-maintenance.ps1 -EnterIfAbsent
docker compose -f docker-compose.dev.yml start postgres          # same container, same antiphon_pgdata
dotnet user-secrets remove ConnectionStrings:DefaultConnection --id aspire-antiphon-apphost --project Antiphon.AppHost/Antiphon.AppHost.csproj
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <rc tip>
pwsh -NoProfile -File scripts/set-apphost-maintenance.ps1 -Clear
pwsh -NoProfile -File scripts/keep-six.ps1 -ClearHold -Agent <hold agent>
```

- Both volumes are kept. Writes made on server2 after the cutover are **not** on the desktop: no reverse-migration script exists (G-8).
- The tracked AppHost string is `localhost:17280` with the dev password (`Antiphon.AppHost/appsettings.json:10`), so removing the secret restores the old target.

## B. Gap list

**Blocking before the window**

| # | Gap | Where | One-line fix |
|---|---|---|---|
| G-1 | Phase 4's restart re-ups desktop Postgres. D-15 fail-closed is lost, the stale copy stays live, and backups and scripts read it | `dev-aspire.ps1:80-83`, `scripts/autostart-apphost.ps1:88`, `scripts/cutover-postgres-server2.ps1:1044`, plan `:186-190` | Skip `up -d` of `postgres` when the AppHost secret points off-host (or a marker says moved). Bring up `redpanda` only |
| G-2 | `docker pull postgres:16` = 16.16 today. The desktop image ID `f40a5645…` is not the current `16.14` build. The deploy regex refuses `postgres:16.14@sha256:` | `scripts/deploy-server2-postgres.ps1:75-76`, `docker-compose.dev.yml:17`, `docs/docker-stack.md:2064` | Read the desktop RepoDigest and pin it. Allow `postgres:16(\.\d+)?(-trixie)?@sha256:` so the env file states 16.14 |
| G-3 | No drill-record producer. The `-RestoreDrill` plan uses the age B1 snapshot, not the nightly plain dump. No drill rehearses phase 2's piped, non-seekable dump or `age -d -i -` | `scripts/deploy-server2-postgres.ps1:250-298`, `docs/docker-stack.md:2076-2093`, `scripts/cutover-postgres-server2.ps1:192-222,793-862` | Add a drill mode that times copy and restore of a given dump and writes the phase-0 JSON. Rehearse the pipe once |
| G-4 | The daily backup is hard-wired to the desktop container. After the cutover it fails, or backs up stale data (G-1) | `scripts/windmill/invoke-postgres-snapshot.ps1:67-69`, `scripts/postgres-snapshot.ps1:54,69,740-756`, `scripts/windmill/antiphon-postgres-snapshot.schedule.json` (`tag: desktop`) | Pass `-Container antiphon-server2-postgres-postgres-1` with `DOCKER_HOST=ssh://mc@server2` and `-AllowRemoteDocker`, or run it on a server2 worker, and re-pin `SOURCE-SHA` |
| G-5 | No CPU isolation: `cpus: 4.0` is a ceiling. The runner has no `cpuset` | `docker-compose.server2-postgres.yml:21-22`, `docker-compose.server2-runner.yml` (no cpu keys) | Add `cpuset:` to the Postgres compose and an excluding `cpuset:` to the runner (operator decision 3) |
| G-6 | Scripted rollback refuses when the target read fails, which is exactly the "server2 is down" case | `scripts/cutover-postgres-server2.ps1:872-876` | Let `-AcceptDataLossRollback` also cover an unreadable target, or document the manual rollback (§A.6) in `docker-stack.md` |

**Hardening and documentation**

| # | Gap | Where | One-line fix |
|---|---|---|---|
| G-7 | No reset path for the server2 database after a failed phase 2 or 3. `pg_restore` into a non-empty `antiphon` fails the retry | `scripts/cutover-postgres-server2.ps1:848`, `scripts/deploy-server2-postgres.ps1` (no reset verb) | Document `docker exec … psql -d postgres -c 'DROP DATABASE antiphon WITH (FORCE); CREATE DATABASE antiphon'` as the operator retry step, or add a guarded `-ResetEmptyTarget` |
| G-8 | No reverse migration (server2 → desktop) for a rollback after traffic | `scripts/cutover-postgres-server2.ps1:864-896` | Plan a reverse dump/restore into a new desktop volume for after-traffic rollback |
| G-9 | Host prerequisites unobserved: `pwsh`, `age` ≥ 1.1 (`-i -`), compose version (`-p` exec without a file was proven on v2.32.4 only), desktop ssh alias `server2` with key auth, data-root free space | `scripts/deploy-server2-postgres.ps1:444-459`, `scripts/cutover-postgres-server2.ps1:573,848` | Add them to the A.1 preflight. Record versions in the window log |
| G-10 | Tailnet-IP bind at boot: if `tailscaled` is not up when `dockerd` restarts the container, the publish fails and the container stays down (standard Docker behaviour for a missing bind address; **UNOBSERVED** on this host) | `docker-compose.server2-postgres.yml:28` | Order `docker.service` after `tailscaled.service` (systemd drop-in), and add the reboot case to the drill |
| G-11 | `-Execute` does not check data-root free space. It only lists it as `unread` in the drill plan | `scripts/deploy-server2-postgres.ps1:267,444-472` | Refuse below 20 GiB free on `DockerRootDir` |
| G-12 | The cutover witness pulls floating `postgres:16` (16.16) | `scripts/test-cutover-postgres-verify.ps1:564-566` | Use the production pin |
| G-13 | The 20-minute phase-3 cost: one `ssh` spawn per target query (`count(*)` per table, two per sequence) | `scripts/cutover-postgres-server2.ps1:573-575,673-700` | Measure in the drill. Batch target queries into one psql session later |
| G-14 | Desktop health tooling still treats `antiphon-postgres`/17280 as the production DB (PASS against a stale copy, or FAIL after the move) | `verify-dev-stack.ps1:107-111`, `scripts/bootstrap-check.ps1:386-451`, `scripts/deploy-local.ps1:79`, `scripts/reap-zombie-agents.ps1:67` | Land CARD-1844 S2 (effective-endpoint check) before or with the window |
| G-15 | `antiphon_messaging` database (Messaging service, `Host=localhost;Port=5432`) placement **UNOBSERVED**. If it lives in the desktop cluster, `pg_dump -d antiphon` leaves it behind and phase 4 stops it | `src/Antiphon.Messaging.Service/appsettings.json:11`, `scripts/cutover-postgres-server2.ps1:802` | At T-15 list databases on the desktop cluster (`\l`). Decide whether to move it |
| G-16 | `-SnapshotReference` only checks that a file exists, not that it is verified | `scripts/cutover-postgres-server2.ps1:193-195` | Require a `pg_restore --list` pass or a snapshot-status match |
| G-17 | Stale docs: the size basis is 1,265,417,239 B (2026-10-21; now ~3.0 GB, +130 MB/day). The 30-min envelope is void above 2 GiB. "CARD-1406 in Backlog" is wrong (it is InProgress) | `docs/docker-stack.md:2074,1322`, plan `:196-210` | Refresh it with the drill numbers |
| G-18 | Secret custody for the server2 cluster password and the cutover connection-string file is not in the credentials owner doc | `docs/agent-credentials.md` (no Postgres entry), `docs/docker-stack.md:1531` | Add one row: env-file path, `conn.txt` lifetime (delete after phase 4), and the user-secrets store as the only durable copy |
| G-19 | `/dev/shm` stays 64 MB on both hosts. A parallel query can fail with "could not resize shared memory segment" (not observed on the desktop) | `docker-compose.server2-postgres.yml` (no `shm_size`), `docker-compose.dev.yml` (none) | Keep it equal for the move. Consider `shm_size: 256mb` with the major upgrade (CARD-1988) |

## C. Who can run what

| Step | Orchestrator (autonomous) | Operator (required) | Why |
|---|---|---|---|
| `-WhatIf` previews of both scripts, registry digest reads, TCP probes, monitor `PreWindow`/`Baseline`/`Watch` | yes | — | Read-only. The scripts are in the main checkout |
| Code cards for G-1…G-19 (server2 Code/Review) | yes | — | Repo work |
| Host preflight A.1.1 (reads) | possible over `ssh mc@server2` (`deploy-server2.ps1` already uses it, `:116`) | default | Host shell; no secret involved |
| CPU isolation choice, runner `cpuset` | runner redeploy is an orchestrator rolling phase | decision is the operator's (approved in principle, decision 3) | — |
| Env file, new password, image pin entry | — | **yes** | Secret custody (plan human-only table `:409-421`) |
| `deploy-server2-postgres.ps1 -Execute` | — | **yes** (it reads the env file; host pwsh) | Approved (decision 2). It needs the secret-bearing file |
| Restore drill copy + restore | timing reads only | **yes** | It handles an unencrypted production dump |
| Drill record JSON | can draft it from the operator's numbers | signs it off | — |
| Windmill schedule disable/enable | — | **yes** | Plan `:419` |
| `conn.txt` (AppHost connection string), age keys | — | **yes** | Secret |
| Cutover `-Execute -ConfirmWindow -ConfirmToken` | — | **yes** | Human-only (plan `:420`). Orchestrator restart autonomy does not extend to this window (plan `:423`) |
| Manual `docker compose stop postgres` after phase 4 (G-1 workaround) | — | **yes** (desktop Docker, production) | — |
| Post-checks A.5 (health, version, Hangfire, delegate canary, monitor) | yes | — | Read-only plus one ordinary dispatch |
| Rollback (scripted or manual) | — | **yes** | Secret and data-loss decision |
| Host trust registry `/home/mc/antiphon-server2/retirement-origins.json` | — | operator only | **Not used by any Postgres move step.** It gates only retire-temp (`docs/docker-stack.md:130`) |

## D. Time estimate per step

| Step | Estimate | Basis |
|---|---|---|
| A.1 preflight + env file + image pull + up | 20–30 min (pull ~1–3 min) | Unmeasured. One image, about 150 MB compressed |
| A.2 verify | 5 min | — |
| A.3 copy 708 MB desktop → server2 | 0.5–2 min at 6–25 MB/s; 12 min at 1 MB/s | Tailnet throughput **UNOBSERVED** (plan `:202`). The TCP RTT is 1–2 ms (2026-11-17 note `:107-112`) |
| A.3 restore on server2 | 3–6 min | The 2026-11-10 desktop drill took 93.5 s for a 530 MB dump of 2.10 GB. Scaled to 708 MB, about 125 s on the desktop. server2 is a 2010-era X5690 under a 4-CPU quota, with serial pg_restore: est. ×1.5–2.5 |
| A.3 checks + teardown | 5 min | — |
| Window phase 0–1 | 3–5 min | StopOnly = graceful `POST /api/operator/shutdown` wait plus the port free (plan `:99-104`) |
| Phase 2 dump + age | 2–3 min | 78.1 s for 2.10 GB on 2026-11-10, ×1.5 for ~3.1 GB |
| Phase 2 copy + restore | 4–8 min | As in the drill |
| Phase 3 | 4–8 min | ~140 tables × 2 `count(*)`, plus sequences ×2, each one ssh or `docker exec` spawn. **UNOBSERVED**, measure in the drill (G-13) |
| Phase 4 | 5–8 min | An AppHost restart with a build; SHA gate |
| Phase 5 + canary | 10 min | — |
| **Window total** | **~30–45 min of 60** | The 2026-11-17 note estimated 20–40 |
| Rollback (scripted) | 10–15 min | Gate 6 reads all tables on both sides (17.3 s per 1.49 GB measured by Review r2), plus a restart |
| Monitor | 30 min after phase 5 | D-11 |

## Remaining uncertainties

- The desktop image RepoDigest, and which 16.14 build `f40a5645…` is.
- Any desktop `ALTER SYSTEM` or database-level setting.
- Whether `antiphon_messaging` lives in the desktop cluster.
- The server2 host data-root and free space.
- Host `pwsh`, `age` and compose versions.
- Tailnet throughput.
- Phase 3 duration.
- The nightly dump's dump time.
- Docker-after-tailscale ordering at boot.
- Whether `docker compose -p … exec` without a file works on the host's compose version. It does on v2.32.4 (measured in the runner's nested daemon: `exec-ok`, exit 0, from a directory with no compose file and from one with only a non-default-named file).

## Not done, noted

- Fix ideas are in the gap list, one line each. Nothing was implemented.
- Filed CARD-1988 (Backlog) for the later major upgrade, per the version rule.
