# Postgres restore drill and server2 sizing (2026-11-18)

Plan task `c88affea`. Operational rehearsal for the database-only move. No source change.

Master read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (origin/master, 2026-11-18T00:48:47Z). This worktree HEAD is that commit. No fetch was required.

Live Windows qualification `f2de5b30` (rc/2026-11-26) is in progress on the desktop. Nothing in this plan runs until that run has finished. This plan task ran no SQL, no Docker, no restart, and no deploy.

## Decisions

### D-1. Use the existing 2026-11-17 plain custom dump. Do not take a new one.

The file is `pg_dump -Fc` (custom archive), unencrypted. `scripts/postgres-snapshot.ps1:12-16` and `:1710-1712` write `-Fc` with no age step. `snapshot-status.json` says `mode=plain` and `encryption=none` (`:1381`). That word means unencrypted, not `pg_dump -Fp`.

Directory `C:\Users\lndco\antiphon-backups` (`:80`). Name `antiphon-YYYYMMDDTHHMMSSZ.dump` (`:79`). The operator names the 2026-11-17 07:50Z file: about 708 MB, 142 tables. The 142 is the script's TABLE DATA count (`Get-SnapshotPlainTableCount`, `:1335-1341`). The exact file name and byte length are UNOBSERVED until the directory is listed. `snapshot-status.json` is one overwritten file (`:1383-1388`), so a later day's job may have replaced it. Do not read the live database to identify the file.

Rejected: `postgres-snapshot.ps1 -Execute` and any new `pg_dump`. Both read the desktop cluster. Rejected: the encrypted B1 `.dump.age` path printed by `scripts/deploy-server2-postgres.ps1:18-21` and `docs/docker-stack.md:2076-2093`. That is a different artifact and a decrypt step this file does not need.

### D-2. Restore once into the CARD-1406 drill compose, then twice more into probe containers.

The operational restore uses `docker-compose.server2-postgres-drill.yml` (blob `cace91c5a7a94ec4cccf1c55e588982a05114f98`), project `antiphon-server2-postgres-drill`, volume `antiphon-server2-postgres-drill_pgdata`, publish `127.0.0.1:5434` only (`docker-compose.server2-postgres-drill.yml:5-44`).

The fingerprint gate cannot use that container. `Test-CutoverProbeContainerName` accepts only `c1974-` or `c1975-` plus 16 hex digits, and rejects a name containing `antiphon` (`scripts/cutover-postgres-server2.ps1:548-553`). The drill container name is `antiphon-server2-postgres-drill-postgres-1`. The gate therefore uses two extra `docker run` containers on the same image, same three `-c` flags, no host port, anonymous volumes, removed with `-v` after a mount-name check.

Rejected: one restore compared with a second restore on the desktop. That loads the desktop. Rejected: pointing the Antiphon server or a throwaway Aspire host at the drill database. The server process writes (dispatch, Hangfire, hosted services). A read-only count comparison does not need it. The cutover probes are the comparison.

### D-3. Run the probe entry points, not phase 3 key min/max.

`-VerificationProbe sequences` runs `Invoke-CutoverVerifySequences` (`:612-659`), which reads `last_value` and `is_called` from the sequence relation (`:606-609`). That is the F-1 check (CARD-1974).

`-VerificationProbe rollback-rows` runs `Test-CutoverNewHasExtraRows` (`:718-744`) and prints the content fingerprint (`:780-782`). That is the F-2 check (CARD-1975). The fingerprint is `count(*)` plus an order-independent `md5` over `row_to_json` (`:702-706`).

Do not run `Invoke-CutoverVerify` (`:673-700`) against this dump. Its key-table query still takes `min` and `max` of the primary key (`:697`). CARD-1979: PostgreSQL 16 has no `max(uuid)`, and 118 entities use uuid keys (`docs/superpowers/reviews/2026-11-17-card-1974-1975-review-r2.md:36`). A failure there would be the known gap, not a bad restore.

### D-4. Same PostgreSQL 16.14 Debian image. No upgrade in this move.

Desktop image, as named by the operator and not re-inspected here: tag `postgres:16`, image id `sha256:f40a5645a21f34c966c46a283e6b7fd5f4a7d4fc1768a5842f34b38423e90c8f`, server 16.14, package Debian `16.14-1.pgdg13+1`.

`docker-compose.dev.yml:17` is the floating tag `postgres:16`. Neither compose file sets `shared_buffers`, `work_mem`, `max_parallel_workers_per_gather`, or `max_connections` (no matches under `docker-compose*.yml`). Both server2 compose files set only `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, and `pg_stat_statements.max=5000` (`docker-compose.dev.yml:18-25`, `docker-compose.server2-postgres.yml:12-19`, drill file `:10-17`).

The deploy script accepts only `postgres:16@sha256:<64 hex>` or `postgres@sha256:<64 hex>`. Any other reference is `image-floating`. `alpine` anywhere in the reference is `image-alpine` (`scripts/deploy-server2-postgres.ps1:71-77`). A bare tag `postgres:16.14` is refused. The registry RepoDigest for image id `f40a5645…` is UNOBSERVED in this checkout. An image id is not a registry manifest digest. The drill starts only after desktop `docker image inspect` shows that id and a `postgres@sha256:` RepoDigest. The env value is `postgres:16@sha256:` plus that RepoDigest.

The Debian tag that names this patch is `postgres:16.14`. The alpine tag is `postgres:16.14-alpine`. `pgdg13` is the package revision inside the Debian image (`16.14-1.pgdg13+1`), not a Docker tag recorded in this repo. Whether today's `postgres:16.14` manifest still equals image id `f40a5645…` is UNOBSERVED. Do not pull `postgres:16`, `postgres:16.14`, or `postgres:16.16` to find out.

Images that are not this drill:

| Image | Where | Difference |
|---|---|---|
| `postgres:16` floating | `docker-compose.dev.yml:17`; witness `scripts/test-cutover-postgres-verify.ps1:124` | Moves when pulled. The witness is a tiny script test, not this dump. |
| `postgres:16.16` | CARD-1974/1975 review round 2 (`docs/superpowers/reviews/2026-11-17-card-1974-1975-review-r2.md:7,15,24`) | Patch 16.16, not 16.14. Same major. The fingerprint timing below was measured there. |
| `postgres:16-alpine` | `Antiphon.Tests` Testcontainers (`docs/testing-and-build.md` integration paragraph); one local binary was 16.15 (`docs/superpowers/reviews/2026-11-17-card-1888-review-r2.md:21`) | musl, not Debian pgdg. Deploy script refuses it. glibc and musl getopt differ (`docs/superpowers/reviews/2026-11-17-card-1920-plan-review.md:83`). |

The move does not upgrade. Throwaway drill and probe containers use the pinned 16.14 digest too. The major-version upgrade is the follow-up section at the end.

### D-5. Sizing changes wait until after the drill.

The drill clusters keep the compose command block as committed, so restore time is an unset-GUC cluster. Recommendations in the sizing section are for a later settings change. They are not applied here.

### D-6. The quiet `pg_stat_statements` pair is the orchestrator's, after `f2de5b30`.

One pair, ten minutes apart, read-only, no reset. Specified below. Not this plan task, and not while the qualification run is live.

### D-7. Latency gate uses the 2026-11-17 measurement.

Server2-to-desktop TCP medians were 1.87, 1.93, and 1.60 ms (`docs/investigations/2026-11-17-postgres-to-server2-move-status.md:107-109`). The drill record stores `latencyMedianMs` 1.93, the highest of those medians. Top-7 extra wait at 2 ms is 65 s/h (`:125`). No new connection to desktop port 17280. Desktop-origin RTT is UNOBSERVED (`:118`).

## Ground truth

| The brief assumes | The tree shows |
|---|---|
| A nightly file `antiphon-<UTC>.dump`, possibly plain or custom | Custom `-Fc`, unencrypted, under `C:\Users\lndco\antiphon-backups`. 708 MB, 142 tables, and the exact name are the operator's figures. UNOBSERVED on disk from this runner. |
| CARD-1406 drill compose is the restore target | Yes. Loopback `127.0.0.1:5434`, volume name pinned to `antiphon-server2-postgres-drill_pgdata`. Production volume pin is a different file (`docker-compose.server2-postgres.yml:45-46`). |
| Cutover verify functions can compare two restores | Yes, via `-VerificationProbe sequences` and `-VerificationProbe rollback-rows`, only against `c1974-`/`c1975-` containers. The probe entry runs before `-WhatIf` and before the window (`scripts/cutover-postgres-server2.ps1:1082-1084`). |
| F-1/F-2 are on master (CARD-1974/1975, Merge 264) | On this SHA the sequence read uses the sequence relation (`:613`) and the rollback gate hashes row JSON (`:702-706`). Phase 3 key `min`/`max` remains (CARD-1979). |
| Desktop and server2 compose settings are the sizing source | Neither file sets `shared_buffers`, `work_mem`, `max_parallel_workers_per_gather`, or `max_connections`. Live values are UNOBSERVED. Do not query the production cluster for them. |
| Server holds about 20 connections; desktop bursts to 2 parallel workers | 14-16 app connections and `max_connections` 100 on 2026-09-25 (`docs/investigations/2026-09-25-card-0698-antiphon-postgres-burns-5-6-cores-transcriptentries-polled-by-full-seq-scan-36x-s.md:44-46`). One SELECT with two parallel workers in the 2026-11-17 sample (`docs/investigations/2026-11-17-transcript-read-attribution.md:168`). Current counts are UNOBSERVED. |
| 5 s transcript reads need `work_mem` | The about 5 s figure is the session-row lock hold for a ~414,000-row wide read (`docs/investigations/2026-11-17-transcript-read-attribution.md:104-105`). That is a sequential read. `work_mem` does not shorten it. |
| CARD-1888/1969 are on the running AppHost since 01:16Z | Stated by the brief. This plan did not call `/api/version`. UNOBSERVED here. |
| Host free space about 175 GiB | Operator figure in the standing authority. The 2026-11-17 runner view was 104G free on the LV (`status note:147`). The Docker data-root was UNOBSERVED then (`:147`) and is UNOBSERVED now. The drill reads `df` on the host before it starts. |

## Who runs what

The phone-home runner does not mount the host Docker socket. Nested dockerd is not the host daemon (`docs/docker-stack.md` CARD-1406 section, and the status note). Host `docker` is the only docker in this drill.

After `f2de5b30` has finished:

1. The orchestrator, on the desktop, runs the statement pair, lists the backup directory, hashes the chosen file, and `scp`s it to host name `server2` (the cutover default, `scripts/cutover-postgres-server2.ps1:58`). No IP is pinned. Which path SSH takes is UNOBSERVED.
2. The operator, on the server2 host, runs compose, restore, analyze, counts, the two probe containers, and teardown. The orchestrator may run those commands only in a shell whose `docker info` is the host daemon. A shell inside the runner container is the wrong daemon.

`GET /api/runner-defaults` and `GET /api/session-runners` were not called. Hitting the desktop API during the live run adds desktop CPU. Fleet placement is UNOBSERVED. This drill does not take `-Runner` and does not take `-Platform`.

## Image pin, before any container starts

On the desktop, after the qualification run, inspect only. Do not `docker pull`. Do not `docker exec` into `antiphon-postgres`.

```powershell
docker image inspect postgres:16 --format "{{.Id}}"
docker image inspect postgres:16 --format "{{json .RepoDigests}}"
```

Stop unless the id is `sha256:f40a5645a21f34c966c46a283e6b7fd5f4a7d4fc1768a5842f34b38423e90c8f` and `.RepoDigests` contains `postgres@sha256:` plus 64 lowercase hex digits. Call that digest `REPO`.

On the server2 host, write an untracked env file outside the repo and outside the dump directory, mode `0600`. Keys only:

```text
ANTIPHON_POSTGRES_IMAGE=postgres:16@sha256:REPO
POSTGRES_PASSWORD=<new throwaway, not the committed desktop password>
```

Do not print the file. Do not commit it. The drill compose reads `ANTIPHON_POSTGRES_IMAGE` and `POSTGRES_PASSWORD` (`docker-compose.server2-postgres-drill.yml:9,24`). It does not read a bind address.

Confirm the compose bytes before `up`. Blob of `docker-compose.server2-postgres-drill.yml` at this master is `cace91c5a7a94ec4cccf1c55e588982a05114f98`. Production compose blob `afaaf9929a3ae082aa44719a2db0eaa29d018018` is not used.

## Manual drill

Stop before the first container when any of these is true: the qualification run is still going; host `df` on the Docker data-root shows under 30 GiB free; `docker ps` already has a container whose mounts include `antiphon_pgdata` or `antiphon-server2-postgres_pgdata` that this procedure would touch; project `antiphon-server2-postgres` would be the `up` target. The 30 GiB floor covers two restored data directories, the 708 MB file, and the fingerprint sort spill measured at about 0.75 times relation size (`review r2:24`). It is a floor, not a measurement of free space.

Record load average at the start. CPU isolation (operator decision 3) is a cutover prerequisite. It is not a drill pass. The drill file's `cpus: 4.0` and `mem_limit: 4g` (`:19-20`) are a ceiling, not a reservation (`status note:148`).

### M-1. Identify and copy

On the desktop:

```powershell
Get-ChildItem -LiteralPath 'C:\Users\lndco\antiphon-backups' -Filter 'antiphon-20261117T075*.dump'
```

Stop unless exactly one file matches. Do not choose a `20261118` file. Do not choose a `.dump.age` file. Record the exact name and length. Stop when the length is under 600000000 bytes or over 900000000 bytes. That band only rejects a file that cannot be the stated 708 MB dump. Inside the band, record the exact length and continue.

```powershell
Get-FileHash -LiteralPath $src.FullName -Algorithm SHA256
```

On the server2 host:

```bash
mkdir -p -m 0700 /var/tmp/antiphon-server2-postgres-drill
```

On the desktop, time the copy. `copySec` is the elapsed whole seconds.

```powershell
$sw = [Diagnostics.Stopwatch]::StartNew()
scp -- $src.FullName 'server2:/var/tmp/antiphon-server2-postgres-drill/antiphon-20261117T075000Z.dump'
$sw.Stop()
```

Use the real file name in the remote path. On the host, `sha256sum` that path. The digests must be identical. Leave the desktop file in place.

### M-2. Restore into the drill compose

Host checkout or a copy of the drill file whose blob is `cace91c5a7a94ec4cccf1c55e588982a05114f98`.

```bash
docker compose --env-file "$ENV_FILE" -f docker-compose.server2-postgres-drill.yml -p antiphon-server2-postgres-drill up -d
```

Wait until `pg_isready` inside that container succeeds. Copy the archive into the scratch container's `/tmp`, which is not the data volume. `dumpRestoreSec` for the later drill record is the `pg_restore` elapsed whole seconds. No new dump is added into that field. The cutover script adds `dumpRestoreSec` and `copySec` (`scripts/cutover-postgres-server2.ps1:208-220`).

```bash
docker cp /var/tmp/antiphon-server2-postgres-drill/antiphon-20261117T075000Z.dump \
  antiphon-server2-postgres-drill-postgres-1:/tmp/antiphon-drill.dump
docker exec antiphon-server2-postgres-drill-postgres-1 pg_restore --list /tmp/antiphon-drill.dump
```

Count lines matching `TABLE DATA` the way `Get-SnapshotPlainTableCount` does. Pass value is 142.

Time `pg_restore`:

```bash
docker exec antiphon-server2-postgres-drill-postgres-1 \
  pg_restore -U antiphon -d antiphon --no-owner --no-acl /tmp/antiphon-drill.dump
docker exec antiphon-server2-postgres-drill-postgres-1 rm -f /tmp/antiphon-drill.dump
```

Pass on exit 0. Then time `ANALYZE` (updates `pg_statistic` on this scratch cluster only):

```bash
docker exec antiphon-server2-postgres-drill-postgres-1 \
  psql -U antiphon -d antiphon -v ON_ERROR_STOP=1 -c 'ANALYZE'
```

### M-3. Exact counts on the scratch cluster

Read-only. No `Text` column. No transcript bodies.

```sql
SELECT count(*) FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'r';
```

```sql
SELECT c.relname, pg_total_relation_size(c.oid)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'r'
ORDER BY pg_total_relation_size(c.oid) DESC
LIMIT 10;
```

For each of those ten names, and for each of `TranscriptEntries`, `AgentTasks`, `SessionQueuedMessages`, `AgentTaskLandNotifications`, `AgentTaskEvents` (`scripts/cutover-postgres-server2.ps1:78-84`) if it is not already in the ten:

```sql
SELECT count(*) FROM public."Name";
```

Quote the catalog name. Stop that name when it is not a plain identifier. Record the integers. `n_live_tup` is not a count.

Extensions and migrations, still on this scratch cluster, as text lists only:

```sql
SELECT extname || ' ' || extversion FROM pg_extension ORDER BY 1;
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;
```

Tear this project down before starting the pair, so only one restored data directory is live at a time until the pair needs both.

```bash
docker compose --env-file "$ENV_FILE" -f docker-compose.server2-postgres-drill.yml -p antiphon-server2-postgres-drill down
docker volume rm antiphon-server2-postgres-drill_pgdata
```

`down` then `volume rm` only for the name `antiphon-server2-postgres-drill_pgdata` (`scripts/deploy-server2-postgres.ps1:80-87`). Stop if the name to remove is anything else.

### M-4. Two probe restores

Create two names `c1974-` + 16 hex and `c1975-` + 16 hex. No `antiphon` in the name. No `-p`. Anonymous volume. Same image digest. Same three server flags as the drill file. `cpus` 4 and memory 4g, matching the drill ceiling.

```bash
docker run -d --name "$NAME" \
  --cpus=4 --memory=4g \
  -e POSTGRES_USER=antiphon -e POSTGRES_DB=antiphon -e POSTGRES_PASSWORD \
  --mount type=volume,destination=/var/lib/postgresql/data \
  "$ANTIPHON_POSTGRES_IMAGE" \
  postgres -c shared_preload_libraries=pg_stat_statements \
  -c pg_stat_statements.track=top -c pg_stat_statements.max=5000
```

`POSTGRES_PASSWORD` comes from the env file in the host shell. It is not an argument printed in the command log. After `pg_isready`, `docker cp` the same archive to `/tmp/antiphon-drill.dump` in each container and `pg_restore -U antiphon -d antiphon --no-owner --no-acl /tmp/antiphon-drill.dump`. Both must exit 0. Remove `/tmp/antiphon-drill.dump` in each container after the probes, before `docker rm`. No `ANALYZE` is required for the probe. Do not restore into project `antiphon-server2-postgres`.

From a host checkout of this script (blob `32463bd30a66e98b951cfba7c9fc18109bd60731`):

```powershell
pwsh -NoProfile -File scripts/cutover-postgres-server2.ps1 -VerificationProbe sequences -ProbeSourceContainer $Src -ProbeTargetContainer $Tgt
pwsh -NoProfile -File scripts/cutover-postgres-server2.ps1 -VerificationProbe rollback-rows -ProbeSourceContainer $Src -ProbeTargetContainer $Tgt
```

Pass: both exit 0. The first prints `PHASE 3 match: sequences`, `PHASE 3 sequence fingerprint:`, and `PHASE 3 sequence reads:` greater than 0. The second prints `ROLLBACK ROWS clean`, `ROLLBACK CONTENT fingerprint:`, and `ROLLBACK CONTENT compared:` greater than 0. Record the two fingerprint lines and the compared count. Do not record row text. The probe refuses `-Execute`, `-Rollback`, and `-ConfirmWindow` together with itself (`:748-749`).

### M-5. A real change must refuse

On the target container only, after both probes passed:

```sql
UPDATE public."TranscriptEntries"
SET "Timestamp" = "Timestamp" + interval '1 microsecond'
WHERE ctid = (SELECT ctid FROM public."TranscriptEntries" LIMIT 1);
```

A one-microsecond `timestamptz` change changes `row_to_json` (`review r2:16`). Re-run only `-VerificationProbe rollback-rows`. Pass: exit non-zero and the text `REFUSED: stop gate 6: the new database has rows the old desktop volume lacks.` Do not pass `-AcceptDataLossRollback`. If the `UPDATE` errors, the refusal was not shown. Stop and report the error class only, with no row text.

### M-6. Tear down

Before `docker rm -v`, inspect mounts. Refuse the remove when a mount name or destination contains `antiphon_pgdata`, `antiphon-server2-postgres_pgdata`, or `pgdata` as a volume name other than the anonymous volume just created. The witness uses the same production-name refuse (`scripts/test-cutover-postgres-verify.ps1:75-76`).

```bash
docker rm -v "$Src" "$Tgt"
rm -f /var/tmp/antiphon-server2-postgres-drill/antiphon-20261117T075000Z.dump
```

The desktop dump stays. The production project stays untouched. The env file is removed by the operator without being printed.

### Drill record

Write a JSON file outside the repo. The cutover script reads four fields (`:199-220` and the witness shape at `scripts/test-cutover-postgres-server2.ps1:776-780`):

```json
{
  "passed": true,
  "latencyMedianMs": 1.93,
  "dumpRestoreSec": 0,
  "copySec": 0
}
```

Fill the two times from M-1 and M-2. `passed` is true only when M-1 through M-6 all passed. Extra keys are for the operator's notes (image id, TABLE DATA count, public heap count, both fingerprints). The script ignores them. `dumpRestoreSec` plus `copySec` above 600 needs `-AcceptDrillTime` at the real window. Above 1800 the script refuses (`:216-220`). This drill does not start that window.

## Pass and fail

| Check | Pass | Stop |
|---|---|---|
| File | One `antiphon-20261117T075*.dump`, length in the 600000000-900000000 band | Any other choice, or a new dump |
| Copy | SHA-256 matches, `copySec` recorded | Hash mismatch |
| List | TABLE DATA count 142 | Any other count |
| Restore | `pg_restore` exit 0 | Non-zero |
| Analyze | `ANALYZE` exit 0 | Non-zero |
| Counts | Integers recorded for the key tables and the ten largest | A query error, or printing `Text` |
| Sequences | Probe exit 0, reads greater than 0 | Exit non-zero, or zero reads |
| Fingerprint | Probe exit 0, `ROLLBACK ROWS clean` | Exit non-zero on the identical pair |
| Update | Second fingerprint probe exits non-zero with the extra-rows sentence | Exit 0, or a different refusal |
| Volumes | Only `antiphon-server2-postgres-drill_pgdata` and the two anonymous volumes are removed | Any other volume name |
| Image | Pinned RepoDigest of id `f40a5645…`, server major 16 | `postgres:16.16`, alpine, or a floating tag |
| Desktop cluster | Container `antiphon-postgres` and volume `antiphon_pgdata` unchanged | Any command against them except the statement pair below |

Phase 3 `min`/`max` is not a check (D-3).

## Sizing

Live GUCs are UNOBSERVED. Do not read `pg_settings` on the desktop cluster for this plan. The drill does not change them.

| GUC | Compose | What is known | Target for a later settings change |
|---|---|---|---|
| `shared_buffers` | unset | Database 3,018 MB on 2026-11-17 (`status note:149`). Server2 service `mem_limit: 4g` (`docker-compose.server2-postgres.yml:21`). | 1 GB. Inside the 4g ceiling, larger than a cold sequential read of the ~5 s transcript shape wants from a small cache. Recommendation, not a measured optimum. |
| `work_mem` | unset | The ~5 s mean is a wide sequential read (`attribution:104-105`). The fingerprint sort spilled 1096552 kB at default `work_mem` on `postgres:16.16` (`review r2:24`). | Leave the image default for the move so server2 matches the unset desktop compose. Raise it only if the quiet delta shows `temp_blks_written` growing on the ~5 s statement's queryid. |
| `max_parallel_workers_per_gather` | unset | One sampled SELECT had two parallel workers (`attribution:168`). Effective desktop value UNOBSERVED. | Set `2` on the production service when that settings change lands, so a 24-core host stays at the observed burst. Not edited in the drill file. |
| `max_connections` | unset | Historically 100, with 14-16 app connections (0698 `:44-46`, 2026-09-25). "About 20" is the brief's figure and is UNOBSERVED now. | Leave the image default. The quiet pair records one `pg_stat_activity` count. Do not set the ceiling to 20. |

`effective_cache_size` is unset and UNOBSERVED. No target.

## pg_stat_statements pair

The orchestrator runs this once, after `f2de5b30` has finished and the desktop is quiet. Two snapshots, 600 seconds apart. No third snapshot. No `pg_stat_statements_reset`. No `docker stats` loop.

Do not use `scripts/postgres-jit-window.ps1 -Execute`. Its select list omits `query` (`:73-88`), the default user is `postgres` (`:21`), and `-Execute` samples `docker stats` for the whole window (`:116-125`). The container role is `antiphon` (`docker-compose.dev.yml:29`).

```powershell
docker exec -i antiphon-postgres psql -U antiphon -d antiphon -X -A -t -v ON_ERROR_STOP=1 -c $sql
```

`$sql` is this text, both times:

```sql
SELECT json_build_object(
  'recordedAt', clock_timestamp(),
  'postmasterStartedAt', pg_postmaster_start_time(),
  'activity', (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()),
  'statements', COALESCE((SELECT json_agg(row_to_json(s)) FROM (
    SELECT queryid::text AS queryid,
           calls,
           total_exec_time,
           total_plan_time,
           rows,
           shared_blks_hit,
           shared_blks_read,
           temp_blks_written,
           left(query, 180) AS query_prefix
    FROM pg_stat_statements
    WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
      AND toplevel
  ) s), '[]'::json)
);
```

Write the two JSON documents outside the repo. If the first command fails, stop. Do not retry. Diff `calls` and `total_exec_time` by `queryid`. Report activity count, the top deltas, and the 180-character prefix. Keep the files. Do not commit them.

This is the one production SQL this program allows, and only then.

## Latency gate

No new probe of `100.79.51.37:17280` or of port 17280. The drill record's `latencyMedianMs` is 1.93 from the 2026-11-17 samples. The cutover script refuses an empty value or a value above 20 (`:203-206`). 1.93 passes that gate.

At 2 ms the top seven statements add about 65 s of waiting per hour (`status note:122-125`). That is the acceptance figure for a database-only move. All-command extra wait at the same RTT is 1,034-1,272 s/h (`:125`). A path back to the October 127 ms median is the residual risk (`:139`). This drill does not measure that path. Desktop-origin RTT stays UNOBSERVED until someone times TCP from the desktop to `server2` port 22 after the qualification run. That optional timing is not a drill pass and does not open Postgres.

## Performance budget

Standards: `docs/test-performance-standards.md` R1-R5. No TUnit row runs. There is no measured p95 for this dump on server2. Every ceiling below is provisional. A phase that exceeds 30 minutes stops the drill and is a Debug task (R1), not a retry.

Shared setup: one drill cluster for restore, analyze, and counts, then removed. Two probe containers share the copied file and exist together only for M-4 and M-5. The desktop statement pair is a separate run with no container setup.

Historical anchors, not this file:

| Anchor | Measured | Source |
|---|---|---|
| Desktop dump 2026-11-10 | 78.1 s, 530,395,357 bytes | status note `:41`. Not repeated. |
| Desktop `postgres:16` restore of that dump | 93.5 s, 139 tables | status note `:41`. Desktop, smaller file. |
| Fingerprint, one side, synthetic 1,488,617,472 bytes on 16.16 | 17,322.030 ms, external merge, Disk 1096552 kB | review r2 `:24` |
| Both sides, scaled by that review to 3 GB | about 70 s | review r2 `:7`. Synthetic shape, not this dump. |
| Witness `scripts/test-cutover-postgres-verify.ps1` | 175.553 s, passed=10 failed=0 | receipt `20261117-200248-fb7f` at `fdd4cd6f0` (`review r2:26`). Not re-run. |

Scaled restore for a 708 MB custom file against the 93.5 s / 530,395,357 byte anchor is about 125 s. That scale is not a p95.

| Phase | Provisional central | Stop and report (about 2x) | Kind |
|---|---|---|---|
| Statement snapshot, each side | UNOBSERVED | 30 s | production read of the stats view |
| Gap | 600 s fixed | — | wall wait, not a timeout |
| `scp` of the dump | UNOBSERVED | 180 s | LAN copy of about 700 MB. Over 180 s is still a successful copy if the hash matches; record it. |
| `pg_restore` | 125 s scaled | 250 s | server2 drill cluster |
| `ANALYZE` | UNOBSERVED | 240 s | scratch cluster |
| Exact `count(*)` set | UNOBSERVED | 600 s | scratch cluster, after analyze |
| Sequence probe | UNOBSERVED | 120 s | both probe containers |
| Rollback-rows probe, both sides | 70 s scaled | 180 s | both probe containers. The script has no `statement_timeout` (CARD-1980, review r2 `:24`). |
| Second probe after the update | same shape, one side changed | 180 s | target is dirty |
| Drill wall | under 30 min if the scaled figures hold | 30 min | R1 |

`copySec` plus `dumpRestoreSec` is what the window script will add. The provisional centrals sum to well under 600 s. If the measured sum exceeds 600, the drill can still pass and the window needs `-AcceptDrillTime`. If it exceeds 1800, the window script will refuse and the operator books a longer window rather than trimming the drill.

Build slot, queue wait, and checkpoint total do not apply. No `dotnet` build. No checkpoint import.

## Checkpoints

No TUnit filter. Do not import this table into the checkpoint runner. A parentheses-and-star class filter is not applicable. The drill is not a test.

| Id | Kind | Command | What it proves | What it is not |
|---|---|---|---|---|
| S-1 | script test, already landed, not run by this plan | `pwsh -NoProfile -File scripts/test-cutover-postgres-verify.ps1` | Tiny `postgres:16` pair: sequence match, content match, in-place update refusal. Last receipt 175.553 s, 10 passed. | The 708 MB file. Desktop Postgres. Server2. |
| S-2 | script test, already landed, not run by this plan | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` | `image-alpine` and `image-floating` refuse; drill volume name is pinned. | A restore. |
| S-3 | script test, already landed, not run by this plan | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | Window gates, including the drill-record fields, against fakes. | A database. |
| M-1..M-6 | manual drill | Host procedure above | Copy, restore, analyze, counts, matching fingerprints, update refusal. | A checkpoint row. A CI test. |

Lane: none. No `-Runner`. No `-Platform`.

## Verification design

Test-design is not a separate stage. The operational checks are M-1 through M-6 and the pass table. The script witnesses S-1 through S-3 already exist on this master and stay as they are. Review reads this plan. It does not start the drill and it does not run S-1 unless it chooses to, on a machine that is not the desktop production host.

## Slices

No code slice. No migration. No public DTO, route, operation label, or remote-script change.

| Slice | Files | Tests | Owner |
|---|---|---|---|
| S0 | none | statement pair, exact SQL above | orchestrator, after the qualification run |
| S1 | untracked env file on the server2 host | image id and RepoDigest check | operator |
| S2 | none in git | M-1 through M-3 | operator on the host daemon |
| S3 | none in git | M-4 through M-6 | operator on the host daemon |
| S4 | a new card, from the follow-up section | none now | orchestrator files the card; nobody upgrades Postgres here |

## Existing tests

None affected. Impacted-classes table (CARD-1782): none.

`scripts/cutover-postgres-server2.ps1`, both compose files, and `scripts/postgres-snapshot.ps1` stay at the blobs named above.

## What this plan does not do

It does not deploy project `antiphon-server2-postgres`. It does not start the cutover window. It does not retarget the daily backup. It does not edit a compose file to add CPU pinning. It does not run `Invoke-CutoverVerify`. It does not start an Aspire host. It does not open the production volume.

## Follow-up: upgrade off 16.14

File a separate card after the database-only move has been stable. Default stability bar, stated so the card can be filed without another decision: seven successful daily backups taken from the server2 cluster. The move window does not include the upgrade.

Scope of that later card: a new cluster at the then-current PostgreSQL major, either `pg_upgrade` into a new volume or a dump-restore. The 16.14 volume stays until the new cluster passes the same two probes (`sequences` and `rollback-rows`) on a scratch copy. Rollback of that later window is to point the server back at the 16.14 volume and leave both volumes in place. Own downtime, own drill record, own rollback. Not `postgres:16.16` slipped into this move, and not `postgres:16-alpine`.

## Performance notes for Review

Provisional ceilings are labelled. The only production SQL is the orchestrator's pair, and only after `f2de5b30`. The fingerprint cost used here is the 16.16 synthetic measurement, scaled, and the plan says so.
