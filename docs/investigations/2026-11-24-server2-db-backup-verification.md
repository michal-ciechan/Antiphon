# 2026-11-24: server2 production database backup verification (before desktop teardown)

Task 2fb54ef1 (Debug). Operator, 2026-11-24: "verify server2 backups, and once confirmed take another backup if missing, and then teardown the database on desktop". This note covers only the verification and the fresh backup. Nothing on the desktop container or volume was changed. The live server2 database was only read, except for one sanctioned `pg_dump`.

**Verdict: TEARDOWN-GO** for the desktop leftover `antiphon-postgres` / `antiphon_pgdata`, with one condition. The **daily backup is not running**: the Windmill schedule has been disabled since the 2026-11-18 cutover. That is a separate operator item (see "Open item"). It does not block the teardown.

## 1. What production uses

| Fact | Value | How |
|---|---|---|
| Production server | `Antiphon.Server` pid 82984 (started 2026-11-22 20:48), restarted during this task as pid 101588 at 2026-11-24 09:19:05 (`/api/version` 6ed4cb58c) | `Get-Process`, `GET :17202/api/version` |
| Server connections | 10 established TCP connections to `100.93.77.126:5432`, all owned by the server pid | `Get-NetTCPConnection -RemotePort 5432` |
| Connection-string source | AppHost user secret `aspire-antiphon-apphost`, `ConnectionStrings:DefaultConnection`, host `100.93.77.126` port `5432` (only host and port read; the password was not read) | secrets.json keys |
| Desktop Tailscale IP | `100.79.51.37` | `tailscale ip -4` |
| server2 container | `antiphon-server2-postgres-postgres-1`, compose project `antiphon-server2-postgres`, working dir `/home/mc/antiphon-server2/postgres`, restart `unless-stopped`, healthy, started 2026-11-18T17:23:46Z | `docker inspect` on server2 |
| Image | `postgres:16@sha256:95206741…74b0b` (image id `sha256:8a4b0458…`), PostgreSQL 16.14 (Debian pgdg13) | `select version()` |
| Volume | `antiphon-server2-postgres_pgdata` at `/var/lib/postgresql/data` | `docker inspect` |
| Publish | `100.93.77.126:5432` (tailnet only). Windmill's Postgres is on 5433. | `docker ps` |
| Database | `antiphon`, 4,845,394,967 bytes (4621 MB) | `pg_database_size` |
| Application sessions | every `antiphon` backend came from `100.79.51.37`, the desktop. The only other backend was this task's psql. | `pg_stat_activity` |
| server2 disk | `/`: 705G, 183G free (73% used) | `df -h` |

The production database is the one on server2.

## 2. Backup mechanisms found

| Mechanism | Where it runs | Schedule | State | Newest output |
|---|---|---|---|---|
| Windmill `u/lndcobra/antiphon_postgres_snapshot` (CARD-1831) | desktop worker over the SSH bridge: `C:\Users\lndco\antiphon-snapshot-runner\invoke-postgres-snapshot.ps1` | `0 50 7 * * *` Europe/London | **disabled** (`schedule.enabled = f`) | last run 2026-11-18 07:50Z success (242 s). That run dumped the *desktop* DB before the cutover. |
| Windmill `u/lndcobra/antiphon_postgres_snapshot_stale` | desktop worker | `0 0 8 * * *` | **disabled** | last run 2026-11-18 08:00Z success |
| Manual post-cutover dump | desktop | none | one-off | `antiphon-server2-20261118T175127Z.dump`, 736,469,405 B, local only, not in OneDrive |
| pg_dump cron on server2 | none found | n/a | n/a | n/a |

Windmill run history (from `v2_job_completed` in the Windmill DB): 2026-11-10 11:36 failure, then success every day from 2026-11-10 12:19 to 2026-11-18 07:50. There have been no runs since. The 2026-11-18 runbook (`docs/investigations/2026-11-18-postgres-move-runbook.md:216,231`) disabled both schedules at T-15 and re-enabled them only "once it targets server2 (G-4)". That re-enable never happened. The runner copy is also stale:

- `C:\Users\lndco\antiphon-snapshot-runner\*` files are dated 2026-11-10. They predate the `-Target server2` retarget.
- `snapshot-target` is missing.
- The Docker context `antiphon-server2` did not exist until this task created it (see section 3).

If the schedule were re-enabled as it stands, the job would back up the stale desktop copy (runbook G-1/G-4).

Newest backup before this task: 2026-11-18 (about 6 days old), so **not verified** under the 26 h rule.

Local folder `C:\Users\lndco\antiphon-backups` (user-only ACL, outside OneDrive) keeps 14 dumps. The OneDrive copy is `C:\Users\lndco\OneDrive\Backups\antiphon-postgres` and keeps 7. It is the off-host copy that CARD-1401 and CARD-1831 specify. The dumps are plain `pg_dump -Fc` and unencrypted, by operator decision 2026-11-10.

## 3. Fresh backup taken now, through the sanctioned mechanism

Prerequisite step: the README's operator prerequisite was missing, so this task created the Docker context. The context is local client configuration and holds no secret. It uses the existing `mc@server2` SSH key.

```text
docker context create antiphon-server2 --description "CARD-1831 snapshot source (ssh-only)" --docker host=ssh://mc@server2
docker --context antiphon-server2 version   -> 24.0.2
```

The dump used exactly the argument set the job wrapper passes (`scripts/windmill/invoke-postgres-snapshot.ps1`), run from checkout 6ed4cb58c:

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/postgres-snapshot.ps1 -Plain -Execute -OneDriveCopy -StagingPath C:\Users\lndco\antiphon-snapshot-staging -Target server2
ONEDRIVE COPY oneDrive=ok bytes=905456635
SNAPSHOT ok exit=0 reason=ok bytes=905456635 kept=12 utc=20261124T090703Z      (390 s wall)
```

The first attempt, without `-StagingPath`, refused with `reason=staging-parent`, as the README documents. Nothing was written.

| Receipt | Value |
|---|---|
| File | `C:\Users\lndco\antiphon-backups\antiphon-20261124T090703Z.dump` |
| Size | 905,456,635 bytes |
| sha256 | `36ad51c138a100cf834d4663a3e86922f02520a8076508056381e3b55c41b89f` |
| Off-host copy | `C:\Users\lndco\OneDrive\Backups\antiphon-postgres\antiphon-20261124T090703Z.dump`, same sha256. Windows cloud-filter `StorageProviderState=2` (in sync), the same as the older synced dumps. |
| snapshot-status.json | `exit 0, tables 142, mode plain, oneDrive ok, target server2, serverVersion 16.14` |
| Retention side effect | OneDrive pruned its oldest dump (`antiphon-20261112T075002Z.dump`; the local copy is kept). No local dump was pruned. |

## 4. Restore proof (scratch on server2)

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/postgres-snapshot.ps1 -Plain -RestoreDrill -Execute -KeepScratch -StagingPath C:\Users\lndco\antiphon-snapshot-staging -Target server2
SNAPSHOT ok exit=0 reason=ok drill=1 bytes=905456635 tables=142 seconds=330
```

The drill restored into the labelled scratch container `antiphon-postgres-drill` (label `antiphon.snapshot-drill=1`, network none, image = the production image id). The scratch volume was `antiphon-postgres-drill-pgdata` and the database `antiphon_drill`, with `pg_restore --exit-on-error`.

Comparison with live server2 used read-only transactions. The cutoff is the dump time, 2026-11-24T09:07:03Z.

| Table | Scratch rows | Live rows | Live rows created ≤ cutoff | Max created (scratch = live) |
|---|---:|---:|---:|---|
| AgentTasks | 32,997 | 32,999 | 32,997 | 2026-11-24 09:04:13Z |
| Cards | 2,268 | 2,268 | 2,268 | 2026-11-23 18:54:13Z |
| Boards | 99 | 99 | 99 | 2026-11-10 |
| CardRevisions | 15,276 | 15,276 | 15,276 | 2026-11-23 12:15:37Z |
| CardComments | 108 | 108 | 108 | 2026-11-23 12:15:37Z |
| Projects | 82 | 82 | 82 | 2026-11-10 |
| AgentSessions | 10,434 | 10,434 | 10,434 | 2026-11-24 09:04:18Z |
| AgentTaskLandings | 1,676 | 1,676 | 1,676 | 2026-11-23 18:54:00Z |
| SessionQueuedMessages | 71,100 | 71,106 | 71,100 | 2026-11-24 09:04:43Z |
| TranscriptEntries | 3,748,263 | 3,749,802 | 3,748,263 | 2026-11-24 09:07:01Z |
| AgentTaskEvents | 987,108 | 987,113 | (no CreatedAt) | n/a |

Every row created before the cutoff matched. Live also had 153 tables against 142 in the scratch, and 241 migrations against 240. The extra migration is `20261122173647_AddReviewEngine`, which created 11 `Review*` tables with 0 rows. The production server restarted at 09:19:05Z and applied it 12 minutes after the dump. Every live backend's `backend_start` is 09:19Z or later. A pre-migration dump stays valid because EF applies the migration on the next start.

Teardown used the exact names, after checking the drill label on both objects: `docker rm -f antiphon-postgres-drill` and `docker volume rm antiphon-postgres-drill-pgdata`. Afterwards, no `drill` container or volume remains. Production was `Up 5 days (healthy)` before and after.

## 5. Desktop leftover compared with server2 (read-only)

Desktop container `antiphon-postgres`:

| Fact | Value |
|---|---|
| Image | `postgres:16`, PostgreSQL 16.14 |
| Port | `0.0.0.0:17280` |
| Compose project | `antiphon` |
| Volume | `antiphon_pgdata` |
| Database size | 3,319,028,759 B |
| Container last started | 2026-11-19 14:24Z |
| Client connections | none |

| Table | Desktop rows | Desktop max created | server2 |
|---|---:|---|---|
| AgentTasks | 30,050 | 2026-11-18 17:27:31Z | 32,999 |
| Cards | 2,202 | 2026-11-18 14:32Z (updated 17:28:17Z) | 2,268 |
| CardRevisions | 14,766 | 2026-11-18 17:28:17Z | 15,276 |
| AgentSessions | 9,957 | 2026-11-18 17:20:18Z | 10,434 |
| SessionQueuedMessages | 67,109 | 2026-11-18 17:29:00Z | 71,106 |
| TranscriptEntries | 1,871,495 | 2026-11-18 17:28:51Z | 3,749,802 |
| Migrations | 238 (newest `20261114063858_AddSessionTranscriptSummaries`) | | 241. Every desktop migration is present on server2. |
| Tables | 142 | | 153. Every desktop table is present on server2. |

The desktop's newest write in any key table is 2026-11-18 17:29:00Z, before the 17:37Z database switch. Nothing on the desktop is newer than server2.

### Primary-key diff: all 125 tables with an `Id` column

Ids were exported read-only from both sides (`COPY (select "Id") TO STDOUT`). 115 tables have no desktop-only Id. Ten do. Each desktop-only Id was then checked against the post-cutover server2 dump `antiphon-server2-20261118T175127Z.dump` (`pg_restore -a -t <table> -f -`, no database):

| Table | Desktop-only Ids | In the 17:51Z post-cutover dump (later deleted on server2) | Absent from that dump: what they are |
|---|---:|---:|---|
| WorktreeResidueRunCandidates | 27,574 | 27,574 | none |
| HostLoadSamples | 96,840 | 96,791 | 49 samples from 2026-11-11 17:46-17:50Z |
| HostOccupancySamples | 24,414 | 24,345 | 69 samples from 2026-11-04 17:28-17:50Z |
| AgentIncidents | 2,554 | 2,443 | 111 incidents created 2026-10-19 to 2026-11-15 |
| Alerts | 755 | 739 | 16 alerts from 2026-10-19 |
| SessionQueuedMessages | 208 | 202 | 6 messages from 2026-10-19, all cancelled |
| AgentSessions | 54 | 54 | none |
| RemoteControlModalEpisodes | 16 | 16 | none |
| CardRelationships | 1 | 1 | none |
| SessionRuntimeActions | 1 | 0 | 1 transient retry row (see below) |

The remaining desktop-only rows are aged telemetry, incidents, alerts and cancelled queue rows that retention pruned on server2 between the restore and 17:51Z. The one exception is a runtime-action retry row:

- Id `b750250b-0b77-49bd-9e2c-f3552266cb35`, created 2026-11-18 17:21:29Z
- trigger `transcript`, phase 8, 16 attempts
- session `b2f1a869-…`, which exists on server2 with 29 runtime actions of its own

This row is a work-queue entry, not durable data. All 16 tables without an `Id` column have server2 counts greater than or equal to the desktop counts.

**Nothing durable exists only on the desktop.**

## 6. What depends on the desktop container

AppHost start does **not** need it:

- `Antiphon.AppHost/Program.cs:19-25` only calls `AddConnectionString("DefaultConnection")`, and the user secret points that at server2.
- `ANTIPHON_SKIP_DEV_POSTGRES=1` is set at User scope. With it, `dev-aspire.ps1:88-105,224-226` and `scripts/autostart-apphost.ps1:98-111` neither start nor wait for the container.
- No test needs it. Every integration test uses Testcontainers (`TestDbFixture`, `postgres:16-alpine`; `docs/testing-and-build.md:205`). Test references to `antiphon_pgdata` and 17280 are string-contract assertions only.
- Nightly (`scripts/nightly-run.ps1`) and the watchdog (`scripts/watchdog-apphost.ps1`) have no reference to it.
- Windmill: only the two disabled snapshot schedules refer to it. `antiphon_build_junk_cleanup`, `antiphon_codex_residue_cleanup` and `antiphon_github_sync` do not.
- Desktop scheduled tasks: `\Antiphon AppHost` and `\Antiphon AppHost Watchdog` only. Neither touches Postgres directly.

| Dependency | file:line | After teardown | Optional? |
|---|---|---|---|
| Compose service `postgres`, `container_name: antiphon-postgres`, port 17280, volume `pgdata` | `docker-compose.dev.yml:16-41`, `:78` | A `docker compose up -d` *without* the skip flag would create a new empty container and volume. That is harmless to production but confusing. | Keep for fresh-clone dev (`docs/bootstrap.md`); do not delete in the teardown task |
| Redpanda in the same compose project `antiphon` (`antiphon-redpanda`, volume `antiphon_redpanda`) | `docker-compose.dev.yml:48-79` | Must survive. Never use `docker compose down -v`, which `install-autostart.ps1:119` suggests. | n/a: keep |
| Health check "Postgres container" | `verify-dev-stack.ps1:107-111`, `:171-176` | **FAIL**. The check ignores the skip flag, so `verify-dev-stack.ps1 -SkipBrowser` exits 1. | Needs a skip-aware fix (follow-up) |
| Bootstrap checks `docker / postgres`, `docker / volume`, `database` (TCP 17280) | `scripts/bootstrap-check.ps1:386-451` | **FAIL** on three rows; ignores the skip flag | Follow-up fix |
| `deploy-local.ps1` reads `__EFMigrationsHistory` from `antiphon-postgres` | `scripts/deploy-local.ps1:79-86` | Throws. Today it reads the stale desktop copy, which is already wrong. | Legacy path; follow-up |
| Helper defaults `PgContainer='antiphon-postgres'` | `scripts/reap-zombie-agents.ps1:67`, `scripts/reap-orphaned-pty-hosts.ps1:59`, `scripts/pg-qps.ps1:44`, `scripts/measure-session-state-cache.ps1:21` | Fail loudly. Today they **silently read the stale copy**. | Manual tools; follow-up to point at server2 |
| Snapshot job runner copy (2026-11-10, desktop-hard-wired) | `C:\Users\lndco\antiphon-snapshot-runner\` (no `snapshot-target`) | If re-enabled unchanged it fails (better than today's stale backup) | Must be refreshed before the schedule is re-enabled |
| Default connection strings `localhost:17280` | `Antiphon.AppHost/appsettings.json:10`, `server/appsettings.json:3`, `appsettings.json.example:3`, `dev-start.ps1:76` | Overridden by the user secret in production. Standalone `dev-start.ps1` mode would fail to connect. | Dev defaults; keep |
| `install-autostart.ps1` starts compose without the skip flag | `scripts/install-autostart.ps1:93-161` | Re-running it would recreate an empty `antiphon-postgres` | Note only |
| Messages only | `scripts/restart-apphost.ps1:156,284` ("preserving … antiphon-postgres") | None | Cosmetic |
| IDE link | `.claude/settings.json:19` (`http://localhost:17280`) | Dead link | Cosmetic |
| Refuse lists (protect the name) | `scripts/c590-remote.sh:9427`, `src/Antiphon.SessionRunner/RunnerRepositoryMaintenance.cs:320`, `scripts/server2-repository-mirrors.ps1:54`, `scripts/restart-linux-server.ps1:68` | None | Keep |

## Teardown list the follow-up task may remove

1. Container `antiphon-postgres` (desktop daemon, compose project `antiphon`, service `postgres`): `docker stop antiphon-postgres; docker rm antiphon-postgres`.
2. Volume `antiphon_pgdata` (exact name; 3.3 GB database): `docker volume rm antiphon_pgdata`. Never `docker compose down -v`, because that would also delete `antiphon_redpanda`.
3. Keep `ANTIPHON_SKIP_DEV_POSTGRES=1` (User scope) so no restart recreates it.
4. Do **not** edit `docker-compose.dev.yml`. Fresh-clone dev still needs it, and the skip flag already keeps it down here. No scheduled task or Windmill item has to be removed.

Recommended follow-ups (not teardown blockers):

- (a) Make `verify-dev-stack.ps1:107-111` and `scripts/bootstrap-check.ps1:386-451` honour `ANTIPHON_SKIP_DEV_POSTGRES`, or the standard stack verification goes red after the teardown.
- (b) Repoint or retire the helper defaults (`reap-*`, `pg-qps`, `measure-session-state-cache`, `deploy-local`).

## Open item: the daily backup is off

Re-enabling it is an operator step (runbook `:324`, Windmill schedule enable). It takes four steps:

1. Refresh the four runner files from current master and re-pin `SOURCE-SHA`, as described in `scripts/windmill/README.md`.
2. Run `Set-SnapshotTarget server2` from the runner copy. It must print `SNAPSHOT TARGET server2`.
3. The Docker context `antiphon-server2` now exists. Check it with `docker --context antiphon-server2 version`.
4. Enable `u/lndcobra/antiphon_postgres_snapshot` and `_stale`.

Until then, the only verified restore point is `antiphon-20261124T090703Z.dump`, plus the unverified manual dump from 2026-11-18 17:51Z.
