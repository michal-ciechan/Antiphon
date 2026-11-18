# Postgres cutover prep result (2026-11-18): GO for the window

Debug task `807203ad`, desktop, 17:20Z–17:30Z. Base master `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.
Run sheet: `docs/investigations/2026-11-18-postgres-cutover-runsheet.md` at `bbace963977ad66520f6732ce1ea933de12d62ed` (task `030cf8e7`).

**Verdict: GO.** Blockers B-1 to B-4 are cleared, S-1 is applied, R-4/R-5/R-6 passed, both snapshot schedules are disabled, and the `-WhatIf` preview from the detached worktree exits 0 with token `c8b4d2c8`. The run sheet's checklist items 1–8 and 10 are now met; item 9 (drain, no locks/marker) and item 11 (operator present) are for T-8/T+0.

No secret was printed. I did not touch the desktop production Postgres, the AppHost, the runners or any desktop service. I did not run the cutover script without `-WhatIf`.

## Paths

| Item | Path |
|---|---|
| Detached worktree (option 1) | `C:\Antiphon\cutover-997f9b8d9` at `997f9b8d9ffc4ba870d2fd56caf7c03781591624` (clean). It has `scripts\cutover-postgres-server2.ps1` (SHA-256 `bb0e48ba0785141e94c5c32bb15580404bb4d00d7807d588eda00a4667319b56`, equal to the run sheet) and `scripts\lib\dev-postgres-compose.ps1` |
| Main checkout | `C:\src\Antiphon` still at `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (checked after the worktree add) |
| Cutover directory | `C:\Users\lndco\antiphon-cutover` (ACL: inheritance removed, only `DESKTOP-KTLKPIF\lndco` Full, inherited by the files) |
| age recipient (public) | `C:\Users\lndco\antiphon-cutover\cutover.agepub` (63 B) |
| age identity (private) | `C:\Users\lndco\antiphon-cutover\cutover.agekey` (184 B, never printed) |
| Drill record | `C:\Users\lndco\antiphon-cutover\drill-record.json` |
| Connection-string file | `C:\Users\lndco\antiphon-cutover\conn.txt` (137 B, one line, contains `No Reset On Close=true`; created because the sheet's R-2 requires it before T-0 and phase 0 / `Assert-CutoverOperatorInputs` refuse without it) |
| Dump directory | `C:\Users\lndco\antiphon-cutover\dump` (empty) |
| Snapshot reference | `C:\Users\lndco\antiphon-backups\antiphon-20261118T075002Z.dump` (exists, 728,483,286 B) |

Deletion after the window (run sheet section 2): keep `conn.txt`, `cutover.agekey` and `cutover.agepub` until the 30-minute monitor passes and the operator closes the rollback option; then the orchestrator runs `Remove-Item C:\Users\lndco\antiphon-cutover\conn.txt, C:\Users\lndco\antiphon-cutover\cutover.agekey -Force`. Remove the worktree with `git -C C:\src\Antiphon worktree remove C:\Antiphon\cutover-997f9b8d9` after the rollback decision period.

## The window command

Run it from a pwsh whose `PATH` includes the new `age` (see deviation D-1). The first line refreshes `PATH` in an already-open shell and must print the WinGet `age.exe` path:

```powershell
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User'); (Get-Command age).Source; age --version
pwsh -NoProfile -File C:\Antiphon\cutover-997f9b8d9\scripts\cutover-postgres-server2.ps1 -Execute -ConfirmWindow -ConfirmToken c8b4d2c8 -Repository C:\src\Antiphon -RcTip e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f -SnapshotReference C:\Users\lndco\antiphon-backups\antiphon-20261118T075002Z.dump -DrillRecord C:\Users\lndco\antiphon-cutover\drill-record.json -ConnectionSecretFile C:\Users\lndco\antiphon-cutover\conn.txt -DumpDirectory C:\Users\lndco\antiphon-cutover\dump -AgeRecipientFile C:\Users\lndco\antiphon-cutover\cutover.agepub -AgeIdentityFile C:\Users\lndco\antiphon-cutover\cutover.agekey -Server2SshHost mc@server2 -Server2ComposeDir /home/mc/antiphon-server2/postgres -TailnetAddress 100.93.77.126 -TailnetOnlyConfirmed -HoldAgent <orchestrator agent id> -HoldReason 'postgres cutover' -HoldUntil <T+2h ISO-8601 UTC>
```

- `-HoldAgent`: the orchestrator substitutes its own agent id. The keep-six row `Antiphon-Orchestrator` is `a392cbc4-0fc0-4603-b4d0-5198d1929718` (the id I used in the preview). The task record does not name its caller, so confirm it.
- `-HoldUntil`: T+2h, for example `2026-11-18T20:00:00Z` for an 18:00Z start.
- `-AcceptDrillTime` is **not** needed: `dumpRestoreSec + copySec` = 151 + 10 = 161 s, which is at most 600.
- **Token `c8b4d2c8`** is valid on machine `DESKTOP-KTLKPIF` for **2026-11-18 and 2026-11-19 UTC**, with these script bytes and `-Repository C:\src\Antiphon`. It equals the run sheet's token. Re-read it from the T-5 preview anyway.

### `-WhatIf` output (17:25Z, from `C:\Antiphon\cutover-997f9b8d9`, full parameter set above with `-HoldAgent a392cbc4-…` and `-HoldUntil 2026-11-18T20:00:00Z`, exit 0)

```
PHASE 0 Pre-flight
  EXIT CHECK 0: verified snapshot reference exists, passed drill record, latency median at or under 20 ms, and the drill time gate.
  STOP GATE 0: refuse without a verified snapshot reference and a passed drill record, when latency is above 20 ms, when the drill misses the time gate, or when the new listener is reachable off the tailnet. No production writer has moved.
PHASE 1 Quiesce
  EXIT CHECK 1: maintenance mode is entered before the hold and StopOnly, dispatch hold is recorded, landPending is empty, repository preview has no uncertain child, and StopOnly leaves AppHost ports free while 17204 and antiphon-postgres stay up.
  STOP GATE 1: landPending is not empty, the repository preview shows an uncertain child, StopOnly would relaunch, or port 17204 or 17280 was touched. A pre-existing logs/apphost.down-on-purpose marker refuses before this run enters maintenance. Leave the old database in place.
PHASE 2 Final dump and restore
  EXIT CHECK 2: pg_dump -Fc sees no other session, and pg_restore exits 0.
  STOP GATE 2: another session appears, or pg_restore exits non-zero. Do not dump under writers. Leave the desktop container up.
PHASE 3 Verify
  EXIT CHECK 3: count(*) per user table, sequences, extensions, migration ids, and key-table minimum and maximum primary keys match.
  STOP GATE 3: any mismatch. Do not repoint.
PHASE 4 Repoint and restart
  EXIT CHECK 4: desktop postgres is stopped, antiphon_pgdata remains listed, the user-secrets key is set from stdin with its value not displayed, maintenance is lifted, and restart-apphost.ps1 -ExpectedServerSha exits 0 with ANTIPHON_SKIP_DEV_POSTGRES=1 so it does not start the desktop postgres again.
  STOP GATE 4: compose offers to remove the data directory, restart exit is 3, or the SHA mismatches. Exit 5 keeps the restart lock. Go to phase 6 if the secret was already changed.
PHASE 5 Post-checks
  EXIT CHECK 5: health is 200, the version SHA equals the rc tip and advertises land-v2, pool flags match, server2 shows a desktop client, and the desktop antiphon-postgres is still exited.
  STOP GATE 5: a post-check fails. The operator runs phase 6. The script does not restart itself again. A running or unreadable desktop antiphon-postgres fails closed with the manual stop command instead; that is not a reason for phase 6.
PHASE 6 Rollback
  EXIT CHECK 6: desktop postgres is started again, per-table high-water marks and content checksums are compared, and AppHost is restarted with -ExpectedServerSha against the untouched desktop volume. Maintenance is lifted after that restart. Once the secret is removed, the backup snapshot-target file is reset to desktop.
  STOP GATE 6: if the new database has extra or changed rows the old desktop volume lacks, the script stops unless -AcceptDataLossRollback was passed. A failed desktop read stops the rollback. A failed server2 read stops it unless -AcceptDataLossRollback was passed, which rolls back without the comparison and leaves every server2 write behind. Neither data directory is removed.
WHATIF: does not POST, does not call docker, and does not call dotnet user-secrets set.
REFUSE: without a verified snapshot reference and a passed drill record the window does not start.
CONFIRM TOKEN c8b4d2c8 (machine DESKTOP-KTLKPIF, 2026-11-18 UTC, this script, -Repository 'C:\src\Antiphon'): pass -Execute -ConfirmWindow -ConfirmToken c8b4d2c8 with the same -Repository today or tomorrow UTC.
```

`-WhatIf` reads no input file (run sheet section 3), so the preview alone does not prove the inputs. I checked them directly: the drill record parses with `passed=True latency=1.93 dumpRestoreSec=151 copySec=10`, every path exists, and `conn.txt` is one line.

## B-1: age

| Host | What was installed | Version | Location |
|---|---|---|---|
| Desktop | `winget install --id FiloSottile.age --exact --scope user` (package `FiloSottile.age` 1.3.2). winget added the aliases `age`, `age-keygen`, `age-inspect` and `age-plugin-batchpass` | `v1.3.2` | `C:\Users\lndco\AppData\Local\Microsoft\WinGet\Links\age.exe` (User `PATH`) |
| server2 | Only the `age` binary from `age-v1.3.2-linux-amd64.tar.gz`, `install -o root -g root -m 0755` | `v1.3.2` (static ELF x86-64) | `/usr/local/bin/age` (on the non-interactive ssh `PATH`; `ssh -o BatchMode=yes mc@server2 'age --version'` gives `v1.3.2`) |

- The server2 tarball SHA-256 was verified with `sha256sum -c` against `cbe24006683f8eb669266162894b9a522a1af52f2665fbc63a4bb032ed26ac10`. That is the GitHub release-asset digest for v1.3.2 (`gh api repos/FiloSottile/age/releases/latest`); see D-2. The temporary directory `/tmp/age-install.*` was removed.
- **Keys (R-3):** `age-keygen -o …\cutover.agekey`, then `age-keygen -y -o …\cutover.agepub …\cutover.agekey`. Recipient: `age1y3nvsw7fj27mrnkp7rxnyfj8uxpj44vf3nu8vz7es4yzsrgyzpqszkdc3m` (public).
- **R-4 dummy round trip:** 1 MiB random file → `age -R cutover.agepub` → `scp` to `/home/mc/antiphon-server2/postgres/c1974-r4-t.age` → `Get-Content -Raw cutover.agekey | ssh mc@server2 "cd … && age -d -i - ./c1974-r4-t.age | sha256sum; rm -f …"`. The SHA-256 matched (`af8e98d8…dc87511`), and the remote file was removed.
- **R-4 real shape:** I created a desktop container `c1974-712a436f5e9f6f0b` (`postgres@sha256:95206741…`, trust auth, no port, anonymous volume) with a 5,000-row table. I ran `docker exec … pg_dump -Fc --no-owner --no-acl` piped into `age -R` through the **same `System.Diagnostics.Process` pipe shape as `Invoke-CutoverPipeDumpToAge`** (both exited 0; 110,818 B encrypted), then `scp`'d it to the compose dir. On server2, the container `c1975-712a436f5e9f6f0b` (the production container's image id `sha256:8a4b0458…`) ran `age -d -i - ./file | docker exec -i c1975-… pg_restore -U antiphon -d antiphon --no-owner --no-acl`, with the identity fed on ssh stdin exactly as in `Invoke-CutoverCopyAndRestore`. pg_restore exited 0. `count:md5(string_agg)` was `5000:de35c785c27e1a075458ea20b3d6af9b` on both sides. Both containers were removed by exact name with `docker rm -f -v`, no `c197*` container is left on either host, and the remote `.age` file is gone. This also proves the non-seekable `pg_restore`-from-pipe path that the run sheet listed as not rehearsed.

## B-2: ssh

`ssh -o BatchMode=yes mc@server2` works from the desktop (`whoami` = `mc`, `sudo -n true` OK). The window command passes **`-Server2SshHost mc@server2`**. The script default `server2` stays unusable.

## B-3: checkout option 1

`git -C C:\src\Antiphon worktree add --detach C:\Antiphon\cutover-997f9b8d9 997f9b8d9ffc4ba870d2fd56caf7c03781591624`. The worktree's HEAD is `997f9b8d9ffc…`, and `git status` is clean. The main checkout HEAD is still `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`. Both `scripts/cutover-postgres-server2.ps1` and `scripts/lib/dev-postgres-compose.ps1` are present in the worktree. Option 1 costs what the run sheet says: phase 5 ends with exit 2 and the `RECOVERY:` line, and then the operator runs `docker compose -f "C:\src\Antiphon\docker-compose.dev.yml" stop postgres`.

## B-4: drill record

`C:\Users\lndco\antiphon-cutover\drill-record.json` (UTF-8, no BOM) is the draft `C:\Users\lndco\antiphon-drill\postgres-restore-drill-20261118.json` (task `49232218`) with two fields added under `notes`: `operatorSignOff` (operator accepted the drill, 2026-11-18) and `draft` (source path). Top-level shape: `passed=true`, `latencyMedianMs=1.93`, `dumpRestoreSec=151`, `copySec=10`. The draft is unchanged.

## S-1 and R-6 on `antiphon-server2-postgres-postgres-1`

Empty-target proof (read-only, `docker exec … psql -U antiphon` over the local socket) came **before** any change:

- 0 relations (tables, partitioned tables, views, materialized views, sequences) outside `pg_catalog`, `information_schema` and `pg_toast*` in database `antiphon`.
- `pg_stat_user_tables` = 0, and there were 0 other sessions.
- Databases: `postgres`, `antiphon`, `template1`, `template0`.
- **R-6:** `cd /home/mc/antiphon-server2/postgres && docker compose -p antiphon-server2-postgres exec -T postgres psql … public count` → `0`, rc 0, on **Compose v2.18.1**. So `compose -p … exec` works on the host's version.

Then I ran `ALTER SYSTEM SET` on `shared_buffers='1GB'`, `effective_cache_size='4GB'`, `maintenance_work_mem='512MB'` and `max_wal_size='4GB'`, followed by exactly one `docker restart antiphon-server2-postgres-postgres-1`.

| Setting | Before | `SHOW` after |
|---|---|---|
| shared_buffers | 163848kB (160 MB) | **1GB** |
| effective_cache_size | 5242888kB | **4GB** |
| maintenance_work_mem | 65536kB | **512MB** |
| max_wal_size | 1024MB | **4GB** |
| work_mem | 4096kB | 4MB (unchanged) |
| max_connections | 100 | 100 (unchanged) |
| shared_preload_libraries | pg_stat_statements | pg_stat_statements (unchanged) |

- `pending_restart` count is 0. `pg_isready` accepts connections. Docker health is `healthy`. Started at `2026-11-18T17:23:46Z`.
- `HostConfig` before and after is identical: `cpuset=0,12 shares=2048 mem=8589934592 shm=1073741824 nanocpus=0`. The port binding is still only `100.93.77.126:5432`.
- Revert if ever needed: `ALTER SYSTEM RESET <name>` for each of the four, then one restart.

## R-5: vault password check

In one pwsh process, `BW_SESSION` was loaded from `~/.bw-session` into the environment (not printed). `bw status` was `unlocked`. I put `bw get password aad8b5d8-1826-483b-b4b2-b50600fb2a33` into `PGPASSWORD`, passed by name to `docker run --rm -e PGPASSWORD postgres@sha256:95206741… psql -h 100.93.77.126 -p 5432 -U antiphon -d antiphon -Atc "select 1; show server_version; select current_user"`. Both environment variables were removed in `finally`.

**Result: `VAULT CHECK OK server_version=16.14 (Debian 16.14-1.pgdg13+1) current_user=antiphon`.** This also resolves the run sheet's UNOBSERVED item: a Docker Desktop container reaches the tailnet IP. The password matched `^[A-Za-z0-9._~-]+$`, so `conn.txt` needed no Npgsql quoting. It was written in the same process (the R-2 recipe).

## Windmill schedules (run sheet section 4, order 1)

I used the windmill skill: a temporary superadmin token labelled `claude-temp-807203ad` (30-minute expiry), deleted afterwards (0 remain), and `POST /api/w/mc/schedules/setenabled/<path>` with `{"enabled":false}` (HTTP 200).

| Schedule | Cron (Europe/London) | Before | Now |
|---|---|---|---|
| `u/lndcobra/antiphon_postgres_snapshot` | `0 50 7 * * *` | enabled | **disabled** |
| `u/lndcobra/antiphon_postgres_snapshot_stale` | `0 0 8 * * *` | enabled | **disabled** |

- **Re-enable** only after the backup-retarget prerequisites P-2 and P-3 and the W-3 flip (run sheet section 4): run `scripts/register-postgres-snapshot.ps1 -Apply -EnableSchedule`. Alternatively, with a temporary token per the windmill skill, `curl -s -H "Host: server2.tail62cf02.ts.net" -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" -X POST -d '{"enabled":true}' http://localhost/api/w/mc/schedules/setenabled/<path>` for each path. Until then, a daily manual server2 dump (post-check 8) replaces the backup.
- `u/lndcobra/antiphon_nightly_tests` (00:30 London) was **not** touched. A window starting about 18:00Z, plus its 30-minute monitor, does not overlap 00:00–02:00 London (London is UTC+0 in November). My read-only `schedules/get` of that path returned a non-JSON body, so its current state is UNOBSERVED. Disable it only if the window slips past about 23:30Z.

## Deviations

- **D-1 (action needed): `age` resolves only in a new shell.** winget put the `Links` directory on the **User** `PATH`. Any pwsh (or orchestrator session) that started before 17:21Z does not see it. In that case phase 2 (`Invoke-CutoverPipeDumpToAge` starts `age` by name) fails **after StopOnly**. Use a new shell, or run the `PATH` refresh line in the window command first, and check that `Get-Command age` prints the WinGet path.
- **D-2: the "official release checksum".** age v1.3.2 publishes no `SHA256SUMS` file; it publishes `.proof` files (transparency-log proofs) and GitHub's per-asset `sha256` digest. I verified against the GitHub release-asset digest. I did not verify the `.proof` (that would need the `age` source tooling).
- **D-3: the desktop pulled `postgres@sha256:95206741…`.** The image was not present locally; the pinned image was pulled by `docker run` for the R-4 source container. R-5 uses the same image. Nothing else was pulled or removed.
- **D-4:** On Windows, `age-keygen` warned `writing secret key to a world-readable file`. That is a POSIX-mode heuristic. The file's real ACL is `lndco:(I)(F)` only (inheritance removed on the directory).
- **D-5:** The R-4 dummy and real-shape files were named `c1974-…` and written to the compose dir `/home/mc/antiphon-server2/postgres/` (the run sheet's R-4 location), then removed. The directory listing afterwards matches the one before.
- **D-6:** `-HoldAgent` in the preview used the `Antiphon-Orchestrator` keep-six id `a392cbc4-…`. The token does not depend on it. The orchestrator must pass its own id.
- **D-7:** The server2 production container showed `Up 33 minutes` before my restart (StartedAt `16:49:09Z`, RestartCount 0). That is earlier than this task, most likely the drill task's work. I did not investigate further.

## Not done, by design

- No `-Execute`, no T-15 baseline monitor (`postgres-cutover-monitor.ps1 -Mode Baseline`), and no drain or lock check. Those belong to the orchestrator at T-15/T-8.
- I did not touch the desktop `antiphon-postgres` or `antiphon_pgdata`, the AppHost, the runners, the scheduled tasks or `ANTIPHON_SKIP_DEV_POSTGRES`.
