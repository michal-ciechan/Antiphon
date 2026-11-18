# Postgres cutover to server2: the window run sheet (2026-11-18)

Investigate task `030cf8e7`. Read-only plus `-WhatIf`.

- Master read: `997f9b8d9ffc4ba870d2fd56caf7c03781591624`. The cutover script blob is `bf479b68c6f3e2bc21ce22508530e004fa9291a7`. Its CRLF working-tree SHA-256 on the desktop is `bb0e48ba0785141e94c5c32bb15580404bb4d00d7807d588eda00a4667319b56`.
- Running server (`GET /api/version`, 17:1xZ): `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`, with capabilities `land-v2` and `operator-shutdown-v1`. That is `rc/2026-11-27`, and the main checkout `C:\src\Antiphon` is detached there.
- Inputs: `scripts/cutover-postgres-server2.ps1` (all 1265 lines), `docs/investigations/2026-11-18-postgres-move-runbook.md`, `docs/superpowers/plans/2026-11-18-postgres-restore-drill-plan.md`, `docs/superpowers/plans/2026-11-18-postgres-backup-retarget-plan.md`, `docs/bootstrap.md`, `server/Program.cs`, `Antiphon.AppHost/*`, `scripts/windmill/*.schedule.json`.

What I ran:

- No SQL anywhere. No restart or stop, and no Windmill write.
- The connection-string file was not created. The vault was not touched. I did not read `~/.bw-session` or `postgres.env`.
- `docker inspect` of the server2 production container, `ls`/`sha256sum` of the compose directory, and `df`, `nproc`, `free`, `apt-cache` and `sudo -n true` on the server2 host, all as `mc`.
- `GET /api/version`, `GET /api/diagnostics/session-state` (pool block only) and `GET /api/projects`.
- The key **names** (no values) of the AppHost user-secrets store.
- `Get-ScheduledTask`, and the cutover script `-WhatIf`, twice.

## Verdict: NO-GO today until four blockers are cleared. All of them are pre-window and take about 30 minutes.

| # | Blocker (observed) | Effect if ignored | Clear it by |
|---|---|---|---|
| B-1 | **`age` is installed on neither host.** The desktop `age` is not on PATH (`which age`, `Get-Command age` both empty). On server2, `age: command not found`; the host is Ubuntu 18.04.1, `apt-cache policy age` shows no package, and `sudo -n true` succeeds | Phase 2 starts `age` (`cutover-postgres-server2.ps1:898`). That run comes **after** StopOnly, so the window aborts and AppHost is restarted on the old string. The outage costs about 10 minutes for nothing | Desktop: `winget install FiloSottile.age` (1.3.2 in the source), then use a **new** shell. Server2: install the static `age` v1.3.x linux-amd64 binary into `/usr/local/bin` with sudo. That directory is on the non-interactive ssh PATH (observed). Then make one key pair (R-3) and do the round-trip rehearsal (R-4) |
| B-2 | **`ssh server2` is refused.** BatchMode ssh as `lndco` gives `tailnet policy does not permit you to SSH as user "lndco"`. `ssh -o BatchMode=yes mc@server2` works. There is no `~/.ssh/config` | Phase 2 `scp`/`ssh` and every phase 3 and phase 5 target query use `-Server2SshHost`, which defaults to `server2` (`:66`). They would fail after StopOnly | Pass `-Server2SshHost mc@server2` (in the command below) |
| B-3 | **The main checkout's helper scripts lack runbook G-1.** `rc/2026-11-27` has no `scripts/lib/dev-postgres-compose.ps1`, and its `dev-aspire.ps1:82` runs `docker compose -f docker-compose.dev.yml up -d`. `-Repository` decides which `restart-apphost.ps1`/`dev-aspire.ps1` run and **what gets built**. Phase 0 and phase 4 both require the build to equal `-RcTip` (`:1064-1067`, `:1204`, `:1221`) | See "Which checkout", below | Choose option 1 or option 2 below |
| B-4 | **Prerequisites not yet produced:** the drill record (task `49232218`), the age keys, `conn.txt`, and a credential check of the vault password against the server2 cluster | Phase 0 refuses. A wrong password is found only by the phase 4 restart, which then needs a phase 6 rollback | R-1 to R-6 |

Not blockers, but they must be decided: the backup retarget cannot run in this window. Its P-2 and P-3 prerequisites are absent (see the schedules section). Also, the four server2 settings (S-1) need one restart of the empty cluster before T-0.

## Which checkout runs the window

The script hashes **itself** into the confirm token. All other paths come from `-Repository`: `restart-apphost.ps1`, `set-apphost-maintenance.ps1`, `keep-six.ps1`, `recover-repository-children.ps1`, `docker-compose.dev.yml` and the AppHost csproj (`:318, :357, :484-485, :534, :545, :1128`). I checked those helpers in `e229fb4d3`:

- `set-apphost-maintenance.ps1 -EnterIfAbsent/-Clear` is present.
- `keep-six.ps1 -Hold/-ClearHold` is present.
- `recover-repository-children.ps1 -Repository` is present.
- `restart-apphost.ps1 -StopOnly/-ExpectedServerSha` is present.
- **The G-1 opt-out is absent.**
- `e229fb4d3..997f9b8d9` has 139 commits and **no EF migration** (`git diff --name-only | grep -i migration` is empty). The server and runner code delta is 14 files: landing, temp sweep and scratch clone.

**Option 1 (recommended; code stays constant through the window).** The main checkout does not move.

- Run the master script file from a dedicated detached worktree, and pass `-Repository C:\src\Antiphon` with `-RcTip e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`.
- Every restart builds the running rc, so the SHA gates hold. Any phase 5 failure is then the database, not new code.
- Cost: G-1 is inert. The phase 4 restart re-ups the desktop `antiphon-postgres`, so phase 5 ends with exit 2 and `PHASE 5 failed: desktop antiphon-postgres is running ... not a reason for phase 6` (`:976-978`). The exit cleanup prints `RECOVERY: AppHost runs on server2 and passed the phase 5 mechanical checks` (`:443-444`).
- The operator then runs the printed `docker compose -f "C:\src\Antiphon\docker-compose.dev.yml" stop postgres`.
- Until the hosts move to an rc that contains `4a584e9f1`, every later AppHost restart (watchdog or logon) revives that stale copy. AppHost still uses server2 through the user-secret. Each time, stop the copy again, and cut the next rc from master.

```powershell
git -C C:\src\Antiphon worktree add --detach C:\Antiphon\cutover-997f9b8d9 997f9b8d9ffc4ba870d2fd56caf7c03781591624
# run everything below as: pwsh -NoProfile -File C:\Antiphon\cutover-997f9b8d9\scripts\cutover-postgres-server2.ps1 ...
# after the window (and after the rollback decision period): git -C C:\src\Antiphon worktree remove C:\Antiphon\cutover-997f9b8d9
```

**Option 2 (G-1 live; a code change rides with the DB move).**

- Cut `rc/<next>` from master `997f9b8d9` or later. Then run `git -C C:\src\Antiphon checkout --detach <tip>` and, at T-30, `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <tip>`.
- Check `GET /api/version` = `<tip>` + `land-v2`, then run the script from the main checkout with `-RcTip <tip>`. Nothing is restored afterwards.
- This adds about 8 minutes, plus an unqualified rc as a second variable.
- Do **not** detach the main checkout at master and then run with `-RcTip e229…`. The phase 4 restart would build master, fail the SHA gate with exit 5 **after the secret changed**, and force phase 6.

## Fixed inputs (derived, observed where marked)

| Parameter | Value |
|---|---|
| `-Repository` | `C:\src\Antiphon` (the token binds this exact string) |
| `-RcTip` | Option 1: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (observed running). Option 2: the new tip |
| `-ProjectId` | `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` (Antiphon, `GET /api/projects`) |
| `-SnapshotReference` | `C:\Users\lndco\antiphon-backups\antiphon-20261118T075002Z.dump` (728,483,286 B, observed). The script only checks that it exists (`:218`, runbook G-16) |
| `-DrillRecord` | `C:\Users\lndco\antiphon-cutover\drill-record.json`, from task `49232218`: `{"passed":true,"latencyMedianMs":1.93,"dumpRestoreSec":<n>,"copySec":<n>}` |
| `-ConnectionSecretFile` | `C:\Users\lndco\antiphon-cutover\conn.txt` (R-2) |
| `-DumpDirectory` | `C:\Users\lndco\antiphon-cutover\dump` (C: has 332 GB free; the dump is about 0.75 GB) |
| `-AgeRecipientFile` / `-AgeIdentityFile` | `C:\Users\lndco\antiphon-cutover\cutover.agepub` / `...\cutover.agekey` (R-3) |
| `-Server2SshHost` | `mc@server2` (B-2) |
| `-Server2ComposeDir` | `/home/mc/antiphon-server2/postgres`. Observed mode 0700. It is the container's `working_dir` label, and its `docker-compose.server2-postgres.yml` is byte-identical to master (sha256 `040ff8f8…`, LF) |
| `-TailnetAddress` + `-TailnetOnlyConfirmed` | `100.93.77.126`. The only `PortBindings` entry is `100.93.77.126:5432` (observed) |
| `-HoldAgent` / `-HoldReason` / `-HoldUntil` | the orchestrator's agent id / `'postgres cutover'` / T+2h ISO-8601 UTC (keep-six allows at most 7 days ahead) |
| `-AcceptDrillTime` | only if the drill's `dumpRestoreSec + copySec` is above 600 (the script refuses above 1800, `:241-246`) |

## (3) `-WhatIf` output (measured 2026-11-18 17:13Z, this desktop, exit 0)

`-WhatIf` reads no file, so I created nothing. The script never validates the window inputs in preview (`:1248-1251`). The full parameter set above, with placeholder paths, also parsed and exited 0 with the same token.

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

- The token `c8b4d2c8` is valid for 2026-11-18 and 2026-11-19 UTC with these script bytes and `-Repository C:\src\Antiphon`. Re-read it from the T-5 preview anyway.
- A worktree made by the same git (`core.autocrlf=true`) has the same bytes and therefore the same token. A different line-ending config changes it.

## (2) The connection-string file

- **Path:** `C:\Users\lndco\antiphon-cutover\conn.txt`.
- **Format:** exactly one line, no newline inside. The script trims it and refuses a multi-line or empty file (`:559-562`):
  `Host=100.93.77.126;Port=5432;Database=antiphon;Username=antiphon;Password=<vault>;No Reset On Close=true`
- `No Reset On Close=true` is mandatory: phase 0 and phase 5 check `NoResetOnClose` (`:1077-1085`, `:1226-1228`). Today's running pool reads `pooling true, noResetOnClose true, maxAutoPrepare 0, multiplexing false` (observed).
- Keep everything else as in the tracked string (`Antiphon.AppHost/appsettings.json:10`): the default pool size and the Npgsql timeouts (see S-3).
- **The script stores it** with `dotnet user-secrets set --id aspire-antiphon-apphost`. It sends the value as JSON over stdin, never as an argument, under key `ConnectionStrings:DefaultConnection` (`:565-569`).
- AppHost passes it to the server through `builder.AddConnectionString("DefaultConnection")` (`Antiphon.AppHost/Program.cs:25`). Today the store has **only** `AntiphonMessaging:BootstrapServers` (key names read, values not), so a rollback's `user-secrets remove` (`:551`) falls back to the tracked `localhost:17280` string.

**R-2: create it without printing the password** (the operator or orchestrator, in pwsh on the desktop, vault unlocked; reference the item by id):

```powershell
$dir = 'C:\Users\lndco\antiphon-cutover'
New-Item -ItemType Directory -Force -Path $dir, "$dir\dump" | Out-Null
icacls $dir /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" | Out-Null      # owner-only ACL, inherited by the files
$env:BW_SESSION = (Get-Content -Raw "$HOME\.bw-session").Trim()                 # never echo
$pw = (& bw get password aad8b5d8-1826-483b-b4b2-b50600fb2a33).Trim()
if ($pw -notmatch '^[A-Za-z0-9._~-]+$') { $pw = $null; throw 'password needs Npgsql quoting; stop and quote it as Password="...";' }
[IO.File]::WriteAllText("$dir\conn.txt", "Host=100.93.77.126;Port=5432;Database=antiphon;Username=antiphon;Password=$pw;No Reset On Close=true", [Text.UTF8Encoding]::new($false))
$pw = $null; Remove-Item Env:BW_SESSION
(Get-Item "$dir\conn.txt").Length                                                # a length only, never the content
```

- **R-5, credential check before T-0.** Today the password is first used only by the phase 4 restart. Check it with no secret on a command line, using a throwaway container of the pinned image and `PGPASSWORD` passed by name:

```powershell
$env:PGPASSWORD = <same bw get password, as above>
docker run --rm -e PGPASSWORD postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b psql -h 100.93.77.126 -U antiphon -d antiphon -Atc "select current_user"
Remove-Item Env:PGPASSWORD
```

  Expect `antiphon`. A Docker Desktop container reaching the tailnet IP is **UNOBSERVED**; if it cannot, run the same check from the server2 host against `100.93.77.126`.
- **Who deletes it, and when.**
  - Not right after phase 4. `-Rollback` runs `Assert-CutoverOperatorInputs`, which refuses when `conn.txt` or either age file is missing (`:489-503`, `:1003`).
  - Keep `conn.txt`, `cutover.agekey` and `cutover.agepub` until the 30-minute monitor passes and the operator closes the rollback option.
  - Then the orchestrator runs `Remove-Item C:\Users\lndco\antiphon-cutover\conn.txt, C:\Users\lndco\antiphon-cutover\cutover.agekey -Force`. The user-secrets store is then the only durable desktop copy. The encrypted witness `dump\antiphon-cutover-*.dump.age` (`:1170`) is kept until the first server2 backup is verified, and then deleted.

## (4) Things to stop or disable first, in order

| Order | Item | Action | Re-enable |
|---|---|---|---|
| 1 (T-30) | Windmill `u/lndcobra/antiphon_postgres_snapshot` (07:50 Europe/London) and `u/lndcobra/antiphon_postgres_snapshot_stale` (08:00) | **Disable both**, through the Windmill UI or the API `POST /api/w/<ws>/schedules/setenabled/<path>` with `{"enabled":false}` (windmill skill for auth). They must stay disabled after the window: the runner copy is the **pre-retarget** wrapper (no `snapshot-target` handling; `SOURCE-SHA` `8ec5a0e6…` ≠ master `43c7967a…`). There is no `snapshot-target` file and no `antiphon-server2` docker context (`docker context ls`: `default`, `desktop-linux`). So W-3 and W-4 cannot run, and the 07:50 job would dump the stale desktop copy | After the retarget prerequisites P-2 and P-3 and the W-3 flip, run `scripts/register-postgres-snapshot.ps1 -Apply -EnableSchedule` or the UI toggle. Until then: the manual server2 dump (post-check 8) |
| 2 (T-30) | Windmill `u/lndcobra/antiphon_nightly_tests` (00:30 London) | Disable only if the window or the 30-minute monitor overlaps 00:00–02:00 London. It runs builds on the desktop and does not use the 17280 database (`TestDbFixture` uses its own container) | UI toggle back to enabled |
| 3 | `antiphon_nightly_readiness` (every 30 min) and `antiphon_release_candidates` (already `enabled:false` in the payload) | Leave them. Readiness may log one failed probe while AppHost is down | — |
| 4 | Scheduled task **Antiphon AppHost Watchdog** (Ready) | **Do not touch it by hand.** Phase 1 runs `set-apphost-maintenance.ps1 -EnterIfAbsent` (marker first, then the task is disabled), and phase 4 or the exit cleanup runs `-Clear`. A marker made by hand beforehand makes the script refuse (`:321-322`, `:1061`). `C:\src\Antiphon\logs\apphost.down-on-purpose` is absent now (observed) | Automatic (`-Clear` re-enables the task, then removes the marker) |
| 5 | Scheduled task **Antiphon AppHost** (logon trigger) | Leave it: it fires only at logon | — |
| 6 | **Antiphon Session Runner** (Running) | Leave it. Stop gate 1 requires 17204 to stay up | — |
| 7 | Dispatch | The fleet must have **no** in-flight tasks on **any** board. All boards share this database; `landPending` is checked only for the Antiphon project id. The script records the keep-six hold itself | The script clears the hold on every exit (`:454-463`) |

## (1) The ordered command list (times from T = script start; D = desktop pwsh, S = `ssh mc@server2`)

| When | Who / where | Step | Precondition | Expected | Abort / rollback |
|---|---|---|---|---|---|
| T-60 | operator, S | **R-1 install age**: static `age` v1.3.x into `/usr/local/bin` (sudo). Check `ssh -o BatchMode=yes mc@server2 'age --version'` | B-1 | `v1.3.x` (≥ 1.1 is needed for `-i -`) | No window without it |
| T-60 | operator, D | `winget install FiloSottile.age`, then open a new pwsh and run `age --version` | — | `v1.3.2` | same |
| T-55 | operator, D | **R-3 keys**: `age-keygen -o C:\Users\lndco\antiphon-cutover\cutover.agekey`; then `age-keygen -y …\cutover.agekey > …\cutover.agepub` | R-2's directory with the owner-only ACL | 2 files. The identity is never printed | — |
| T-50 | orchestrator, D+S | **R-4 pipe rehearsal**: on D, `age -R cutover.agepub -o t.age <any 1 MB file>`, then `scp t.age mc@server2:/home/mc/antiphon-server2/postgres/`; then `Get-Content -Raw cutover.agekey \| ssh mc@server2 "cd /home/mc/antiphon-server2/postgres && age -d -i - ./t.age \| sha256sum; rm -f ./t.age"` | R-1, R-3 | The sha256 equals the source file's | Fix it before T-0 |
| T-50 | orchestrator, S | **R-6 empty-target and compose check** (read-only): `cd /home/mc/antiphon-server2/postgres && docker compose -p antiphon-server2-postgres exec -T postgres psql -U antiphon -d antiphon -Atc "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='public'"` | the drill used its own project (`-drill`, port 5434) | `0`. It also proves that `compose -p … exec` without the default file name works on the host's **Compose v2.18.1** (observed version; proven only on v2.32.4) | Non-zero: reset the target (runbook G-7) before T-0. An exec failure means phases 2, 3 and 5 cannot work; stop |
| T-45 | operator, S | **S-1 settings** (next section), then restart the empty cluster, then `SHOW` | empty target | values as set | Revert with `ALTER SYSTEM RESET` and a restart |
| T-40 | operator, D | **R-2 conn.txt**, then **R-5 credential check** | vault unlocked | `antiphon` | Fix it before T-0 |
| T-35 | operator, D | Write `drill-record.json` from task `49232218` | drill reported GO | `passed:true`, `latencyMedianMs ≤ 20`, times present | No window |
| T-30 | orchestrator | Disable the schedules (order 1–2). Option 2 only: cut the rc, detach main, `restart-apphost.ps1 -ExpectedServerSha <tip>`, check `/api/version` | — | — | — |
| T-15 | orchestrator, D | Option 1: create the detached worktree. Then `pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 -Mode Baseline -Execute -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f -BaselinePath C:\Users\lndco\antiphon-cutover\baseline.json` | — | 20 samples | — |
| T-8 | orchestrator | Drain: no Working, Queued or landPending tasks on any board (`GET /api/agent-tasks?projectId=…&landPending=true` → 0 items) | — | 0 | Wait |
| T-5 | operator, D | `pwsh -NoProfile -File <script> -WhatIf -Repository C:\src\Antiphon`. Read the `CONFIRM TOKEN` | — | exit 0, 8 hex | — |
| T+0 | **operator** (human-only, runbook §C), D | `pwsh -NoProfile -File <script> -Execute -ConfirmWindow -ConfirmToken <8hex> -Repository C:\src\Antiphon -RcTip <rc tip> -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f -SnapshotReference C:\Users\lndco\antiphon-backups\antiphon-20261118T075002Z.dump -DrillRecord C:\Users\lndco\antiphon-cutover\drill-record.json -ConnectionSecretFile C:\Users\lndco\antiphon-cutover\conn.txt -DumpDirectory C:\Users\lndco\antiphon-cutover\dump -AgeRecipientFile C:\Users\lndco\antiphon-cutover\cutover.agepub -AgeIdentityFile C:\Users\lndco\antiphon-cutover\cutover.agekey -Server2SshHost mc@server2 -Server2ComposeDir /home/mc/antiphon-server2/postgres -TailnetAddress 100.93.77.126 -TailnetOnlyConfirmed -HoldAgent <orchestrator agent id> -HoldReason 'postgres cutover' -HoldUntil <T+2h ISO UTC> [-AcceptDrillTime]` | everything above | the phase lines below | per phase |
| T+0–2 | script | Phase 0 | — | `PHASE 0 pool flags match`, `PHASE 0 tailnet 5432 accepted a desktop connect` | REFUSED (exit 3): nothing has changed. Fix and rerun |
| T+2–6 | script | Phase 1: maintenance, then hold, then landPending, then children preview, then `restart-apphost.ps1 -StopOnly` | — | `PHASE 1 landPending is empty.`, `PHASE 1 StopOnly exited 0` | Before StopOnly, the script lifts the hold and maintenance itself. After StopOnly and before the secret change, it restarts AppHost on the old string (`:412-441`) |
| T+6–9 | script | Phase 2a: `pg_dump -Fc` piped to `age` on D | no other session on `antiphon` (`:1145`) | — | Stop gate 2: automatic old-string restart |
| T+9–17 | script | Phase 2b: `scp`, then `age -d -i - \| compose exec -T postgres pg_restore` | empty target | `PHASE 2 pg_restore exited 0.` | Exit 3, and the old-string restart runs in cleanup. The target is now non-empty: reset it (G-7) before any retry |
| T+17–25 | script | Phase 3: table names, per-table `count(*)`, sequences, extensions, migrations, key-table min/max | — | `PHASE 3 match: …` and `PHASE 3 exit check passed.` | Exit 3, with the old-string restart. Do not repoint |
| T+25–33 | script | Phase 4: `compose stop postgres`, volume listed, `exited`, `user-secrets set`, `restart-apphost.ps1 -ExpectedServerSha` | — | `PHASE 4 restart exited 0.` | Exit after the secret changed: **phase 6** (rollback section) |
| T+33–35 | script | Phase 5 | — | `PHASE 5 mechanical checks passed.` Option 2 then adds `…desktop antiphon-postgres is exited`. Option 1 instead gives exit 2 + `RECOVERY: … Do not run phase 6 for this` | Health, version, pool or no server2 client: **phase 6** |
| T+35 | operator, D | Option 1 only: `docker compose -f "C:\src\Antiphon\docker-compose.dev.yml" stop postgres` (never `rm`, never `down -v`) | phase 5 mechanical checks passed | the container is `Exited` | — |
| T+35 | operator, D | `[Environment]::SetEnvironmentVariable('ANTIPHON_SKIP_DEV_POSTGRES','1','User')` (the script's `OPERATOR:` line `:1236`). It is inert on rc `e229…` but needed for every later rc | — | — | Phase 6 tells you to remove it again (`:1028-1029`) |
| T+35–50 | orchestrator | Post-checks (5) | — | all pass | Rollback criteria |
| T+50–60 | orchestrator | The manual server2 dump (post-check 8), then lift the drain | — | — | — |

## (5) Post-cutover verification and time budget

| # | Check | Command | Pass | Budget |
|---|---|---|---|---|
| 1 | Health and version | `curl -s localhost:17202/health`; `GET /api/version` | 200; SHA = `-RcTip`, `land-v2` | 1 min |
| 2 | Pool, uptime | `GET /api/diagnostics/session-state` → `.pool` | `pooling true, noResetOnClose true, maxAutoPrepare 0, multiplexing false` | 1 min |
| 3 | Server2 is the database in use | S: `docker exec antiphon-server2-postgres-postgres-1 psql -U antiphon -d antiphon -Atc "select count(*), min(client_addr::text) from pg_stat_activity where datname='antiphon' and client_addr is not null"`. D: `docker ps -a --filter name=antiphon-postgres --format '{{.Status}}'` and `docker volume ls` | count ≥ 1; desktop `Exited`; `antiphon_pgdata` listed | 2 min |
| 4 | EF migrations applied cleanly | `logs/` server log since the restart: no `Migrat` error. S: `… -Atc 'select count(*), max("MigrationId") from "__EFMigrationsHistory"'` | the same max as phase 3 (`20261114063858_AddSessionTranscriptSummaries` is the latest in master and rc; no migration differs between them) | 2 min |
| 5 | Extensions and pg_stat_statements | S: `… -Atc "select extname||' '||extversion from pg_extension order by 1"` and `-Atc "show shared_preload_libraries"` and `-Atc "select count(*) from pg_stat_statements"` | `pg_stat_statements 1.10` present (from the dump, and migration `20260925120000_EnablePgStatStatements` already recorded); preload lists it; count > 0 after traffic | 2 min |
| 6 | Sequences | Phase 3 already compared `last_value`/`is_called` per sequence. After traffic: S `… -Atc "select count(*) from pg_sequences where last_value is not null"` | no `duplicate key` errors in the server log in the first 10 min | 1 min |
| 7 | Hangfire | `http://localhost:17202/hangfire` | 1 server; recurring jobs listed; no failed jobs in 10 min. Storage is InMemory (`server/appsettings.json:188-191` has no `Storage`), so the queue starting empty is expected | 2 min |
| 8 | Backup on the new source | S: `docker exec antiphon-server2-postgres-postgres-1 pg_dump -Fc --no-owner --no-acl -U antiphon -d antiphon -f /tmp/m.dump && docker exec antiphon-server2-postgres-postgres-1 pg_restore --list /tmp/m.dump \| grep -c 'TABLE DATA'`, then `docker cp` it out and copy it off-host | the TABLE DATA count equals the phase 3 table count | 6 min |
| 9 | Delegate canary on server2 | `delegate.ps1` with one cheap Investigate on server2 (a throwaway question) | it reaches Working with transcript UserPrompt evidence and settles. One land completes if one is queued | 10 min |
| 10 | Monitor | `postgres-cutover-monitor.ps1 -Mode Watch -Execute -ProjectId d4ea7ae9-… -BaselinePath …\baseline.json` at +10, +20, +30 | no `ROLLBACK` (exit 2) | runs after the window |

**Window budget (60 min):**

| Step | Minutes |
|---|---|
| Phases 0–1 | 6 |
| Phase 2 | 11 |
| Phase 3 | 8 |
| Phase 4 | 8 |
| Phase 5 + manual stop | 2 |
| Post-checks 1–9 | 27 |
| **Total** | about 62. Post-checks 8–9 overlap with the monitor; the hard part ends by T+35 |

- Phase 2b on server2 is the main unknown. The restore is now pinned to **one physical core** (`cpuset 0,12`, observed), and the restore drill's measured `dumpRestoreSec` replaces my estimate.
- If phase 2+3 run past T+30, the window overflows. Starting a phase 6 then adds 10–15 minutes.

## (6) Rollback and the point of no return

- **Before phase 4's `user-secrets set`:** the script returns to the old string on its own, with no data loss (`:412-441`). If AppHost stays down, the recovery line says so. Then run the manual path below from step 4.
- **After the secret changed (phase 4 restart failed, or phase 5 failed):** run phase 6 with the same inputs and a **fresh** preview token.

  `pwsh -NoProfile -File <script> -Rollback -Execute -ConfirmWindow -ConfirmToken <8hex> <all the window inputs above>`

  It starts desktop postgres, compares the content fingerprints table by table (20-minute statement bound per table, `:82`), removes the secret, writes `desktop` into `C:\Users\lndco\antiphon-snapshot-runner\snapshot-target`, restarts, and lifts maintenance (`:1002-1055`).
  - The `snapshot-target` write is harmless: the current runner wrapper ignores the file.
  - If the server2 side cannot be read, add `-AcceptDataLossRollback` (runbook G-6; fixed on master `0ac2ff4d6`).
  - Afterwards, remove the `ANTIPHON_SKIP_DEV_POSTGRES` user variable if it was set.
- **Manual rollback** (the script cannot run; on D, from `C:\src\Antiphon`):

  ```powershell
  pwsh -NoProfile -File scripts/set-apphost-maintenance.ps1 -EnterIfAbsent
  docker compose -f docker-compose.dev.yml start postgres
  dotnet user-secrets remove ConnectionStrings:DefaultConnection --id aspire-antiphon-apphost --project Antiphon.AppHost/Antiphon.AppHost.csproj
  [Environment]::SetEnvironmentVariable('ANTIPHON_SKIP_DEV_POSTGRES',$null,'User')
  pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <rc tip>
  pwsh -NoProfile -File scripts/set-apphost-maintenance.ps1 -Clear
  pwsh -NoProfile -File scripts/keep-six.ps1 -ClearHold -Agent <hold agent>
  ```

- **Point of no return: the first write on server2 after the phase 4 restart exits 0.** That is effectively at once, because the server's recurring jobs and runner sync start writing.
  - From then on, every rollback is a data-loss rollback (`-AcceptDataLossRollback`). There is no reverse migration (runbook G-8).
  - Treat the passed 30-minute monitor as the end of the rollback option. After that, a server2 problem is fixed forward, never by returning to the desktop.
  - Keep `antiphon_pgdata` untouched for at least 14 days.

## (7) Unobserved items and risks

**S-1. Settings for the empty cluster before T-0 (observed now: only the three `pg_stat_statements` flags in `Config.Cmd`; `mem_limit 8g`, `shm 1g`, `cpuset 0,12`, no CPU quota).**

The tracked compose has no settings keys, and the deploy script's compose check is not a path I measured. So use `ALTER SYSTEM`, which writes `postgresql.auto.conf` in the volume. It needs no Code card, and it travels with the volume, not with the repo. On S:

```bash
C=antiphon-server2-postgres-postgres-1
for s in "shared_buffers='1GB'" "effective_cache_size='4GB'" "maintenance_work_mem='512MB'" "max_wal_size='4GB'" ; do docker exec $C psql -U antiphon -d antiphon -Atc "ALTER SYSTEM SET $s"; done
docker restart $C && docker exec $C pg_isready -U antiphon -d antiphon
docker exec $C psql -U antiphon -d antiphon -Atc "select name||'='||setting||coalesce(unit,'') from pg_settings where name in ('shared_buffers','effective_cache_size','maintenance_work_mem','max_wal_size','work_mem','max_parallel_workers_per_gather','max_connections') order by 1"
```

| Setting | Value | Why |
|---|---|---|
| `shared_buffers` | **1GB** | Drill plan sizing (`restore-drill-plan.md:314`): DB 3.0 GB; 1 GB is 12.5% of the observed **8g** `mem_limit` (the plan reasoned from the old 4g) |
| `effective_cache_size` | 4GB | Planner hint only, half of `mem_limit`. The host has 98 GiB of page cache (`free -g`), but the cgroup caps the container |
| `maintenance_work_mem` | 512MB | Speeds up `pg_restore` index and FK builds in phase 2b, the longest phase, on a one-core pin. Used only by maintenance operations |
| `max_wal_size` | 4GB | Fewer forced checkpoints while about 3 GB is restored. Disk has 230 GB free (observed) |
| `work_mem` | **leave 4MB** (default) | The drill plan says to match the unset desktop (`:315`), and raise it only on measured `temp_blks_written` |
| `max_parallel_workers_per_gather` | **leave 2** | 2 is already the PostgreSQL 16 default, which equals the plan's target (`:316`). Note: with `cpuset 0,12` there are only 2 logical CPUs, so a leader plus 2 workers oversubscribes. Revisit it (1) only if the monitor shows CPU saturation |
| `max_connections` | **leave 100** | Plan `:317`: keep the image default, which equals the desktop. Historically 14–16 app connections (CARD-0698) |

**S-2. Pool versus `max_connections`.**

- The server opens an Npgsql pool per distinct string: the application string with `-c jit=off` (`ApplicationPostgresConnection.cs`, `Program.cs:127-129`), plus the raw string for the startup `CREATE DATABASE` probe against `postgres`. Hangfire is InMemory, so it adds none.
- The default `Maximum Pool Size` is 100 per pool. In theory the pool can therefore exhaust 100. That is the same exposure as on the desktop today, so it is not new.
- `antiphon` is the image superuser, so `superuser_reserved_connections` (3) is also usable by the app. A saturated pool would block the operator's `psql`.
- No change for the move. If the monitor shows more than 60 backends, set `Maximum Pool Size=60` in the secret.
- The live connection count is **UNOBSERVED** (no SQL by this task).

**S-3. Npgsql over about 2 ms RTT.**

- The defaults stay: `Timeout=15` s connect, `Command Timeout=30` s, no `Keepalive`. A 2 ms RTT is far inside all of them.
- The cost is per round trip. The 2026-11-17 note estimated about 65 s/h of extra wait for the top-7 statements; the monitor's 3× / 1 s rule (D-11) is the gate.
- `Keepalive=30` would detect a dead tailnet path sooner. It is optional and left out, so that the secret differs from the tracked string only in Host, Port and Password.

**Other risks and unobserved items:**

- **UNOBSERVED: the server2 `antiphon` database is empty.** R-6 checks it. A non-empty target fails phase 2.
- **UNOBSERVED: whether Compose v2.18.1 resolves `-p antiphon-server2-postgres exec`** from a directory that has no default compose file name. The directory holds `docker-compose.server2-postgres.yml` and also a stray `docker-compose.dev.yml` (observed). R-6 proves it.
- **UNOBSERVED: Docker Desktop container egress to the tailnet IP** (R-5). The fallback is to run it from the server2 host.
- **UNOBSERVED: the desktop `ALTER SYSTEM` or `ALTER DATABASE` settings.** They do not travel in `pg_dump` without `--create`.
- **UNOBSERVED: other databases in the desktop cluster.** The messaging service uses `am-postgres` on server2 (observed running), so runbook G-15 is very likely moot.
- **Not rehearsed: phase 2's non-seekable `age` pipe.** R-4 rehearses the transport only. pg_restore reading a custom archive from a pipe is a supported serial path, but it is untested here.
- **The one-core pin** (`cpuset 0,12`, `cpu_shares 2048`) is shared with the runner. A busy server2 slows phase 2b and phase 3. The window's drain keeps the runner idle.
- **Other boards share the database.** `landPending` covers one project id only, so drain every board.
- **No daily backup between the window and the retarget.** The schedules stay disabled, so a manual server2 dump is needed daily until P-2, P-3 and W-3 are done (the retarget plan).
- **Option 1 G-1 gap:** every AppHost restart revives the stale desktop container until the hosts run an rc that contains `4a584e9f1`. `verify-dev-stack.ps1` and the bootstrap checks still treat 17280 as production (runbook G-14).
- **Desktop tooling.** Nothing in the window deletes either volume. `antiphon_pgdata` is the rollback asset.
- **Server2 reboot ordering** (docker before tailscaled, runbook G-10) is still **UNOBSERVED**. A reboot could leave the cluster unbound.

## (8) GO / NO-GO checklist (all must be yes)

1. [ ] The restore drill `49232218` reported GO; `drill-record.json` is written with `passed:true`, `latencyMedianMs ≤ 20`, and `dumpRestoreSec + copySec ≤ 600` (or `-AcceptDrillTime` is decided).
2. [ ] `age ≥ 1.1` works on both hosts (R-1); the keys exist (R-3); the round-trip sha256 matches (R-4).
3. [ ] `ssh -o BatchMode=yes mc@server2 true` succeeds at T-15.
4. [ ] The server2 `antiphon` public schema is empty, and `compose -p … exec` works (R-6).
5. [ ] S-1 settings are applied and the cluster restarted; `docker inspect` health is `healthy`.
6. [ ] `conn.txt` is one line with `No Reset On Close=true` and an owner-only ACL; the credential check prints `antiphon` (R-5).
7. [ ] The checkout option is chosen. Option 1: the detached `997f9b8d9` worktree exists and `-RcTip` = the running `/api/version`. Option 2: the new rc is live and verified.
8. [ ] Both Postgres snapshot schedules are disabled; the nightly is disabled if it overlaps.
9. [ ] No Working, Queued or landPending task on any board; no `logs/apphost.down-on-purpose`; no `logs/apphost.restart.lock` or `apphost.launch.lock` held.
10. [ ] The T-5 preview exits 0 and the token was read from it.
11. [ ] An operator is present for the `-Execute` step and for any rollback (human-only).

## Not done, noted

- I ran nothing that writes. I did not install `age`, create keys, run `ALTER SYSTEM`, disable a schedule, create a worktree or write `conn.txt`. All of those are listed above for the window owner.
- Fix ideas, one line each:
  - Default `-Server2SshHost` to `mc@server2`.
  - Make phase 0 probe `age` on both hosts and `compose -p … exec` before StopOnly.
  - Track the S-1 settings in `docker-compose.server2-postgres.yml` `command:`.
