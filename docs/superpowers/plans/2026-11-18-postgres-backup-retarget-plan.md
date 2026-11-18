# Retarget the daily Postgres backup to follow the database to server2 (CARD-1831 follow-up, operator decision 6)

Plan task `a1368228`, 2026-11-18. Master read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (origin/master = task base).
This task was read-only. It ran no SQL, made no desktop Docker call, ran no restart or deploy, and changed no volume. It made no load on the desktop.
It ran only these:
- A baseline run of the snapshot harness on the server2 runner.
- Anonymous Docker Hub and registry metadata reads, to get the image pin.
- The two runner routes.

The operator's standing authority of 2026-11-18 covers this work: the fast-tracked move, decision 6 "retarget the daily backup to server2 in the same window", Merge verification SHORT, and mutation waived. No go-ahead was needed.

## Outcome in one paragraph

The same Windmill job (`u/lndcobra/antiphon_postgres_snapshot`, tag `desktop`, 07:50 Europe/London) keeps running on the desktop worker through the SSH bridge. It keeps writing into the same desktop folders, so OneDrive, the 14/7 retention, `snapshot-status.json`, the 08:00 stale check and the Telegram sender (which lives on the desktop) are unchanged.

A one-word setting file next to the runner selects the source database:
- `desktop` is today's behaviour, byte for byte.
- `server2` makes `postgres-snapshot.ps1 -Target server2` run the same verified publish against the server2 host daemon, through a pinned Docker context over SSH. The context is named `antiphon-server2`, the container `antiphon-server2-postgres-postgres-1`, and the volume `antiphon-server2-postgres_pgdata`.

`pg_dump` runs inside the server2 container, so the dump's CPU is spent on server2. The desktop only receives about 0.7 GB a day over the LAN. The current publish design re-uploads that file once for the second `pg_restore --list` (D-4).

The cutover window flips the word, runs one manual snapshot, and restores that dump into a throwaway cluster on server2. The cluster uses the same image as production. This needs a plain restore drill, which the script refuses today (D-6).

## Ground truth: what the brief assumes vs what master does

| # | Brief / card assumption | Master `e229fb4d3` | Consequence |
|---|---|---|---|
| G-1 | The wrapper names the container and volume at `scripts/windmill/invoke-postgres-snapshot.ps1:57,67` | **No.** The invoke wrapper names no container or volume. `:57` is `function Invoke-PlainSnapshotChild`, and `:66-68` start the child with `-Plain -Execute -OneDriveCopy -StagingPath`. The child takes the defaults `[string]$Container = 'antiphon-postgres'` (`scripts/postgres-snapshot.ps1:54`) and `[string]$SourceVolume = 'antiphon_pgdata'` (`:69`). The 11-17 citation was taken at an older SHA | The selection belongs in `postgres-snapshot.ps1`. The wrapper only passes the target word |
| G-2 | `docs/investigations/2026-11-10-first-database-dump.md` and `2026-11-10-backup-runner-refresh.md` are "on master" | **Not on master.** They are at `8e1811543` on `origin/feat/card-task-52146b44` and at `b91c22df1` on `origin/feat/card-task-b28fd575`. I read both from those commits | Cite them by commit. Landing them is out of scope |
| G-3 | "The existing throwaway postgres:16 restore proof" | It was a **manual** step on 11-10 (`8e1811543`): a `postgres:16` container on `127.0.0.1:38147`, 93.5 s, exit 0, 139 public tables, then `docker rm -f -v`. In the script, `-Plain -RestoreDrill` is refused with `plain-drill-unsupported` (`scripts/postgres-snapshot.ps1:1910-1913`). The age drill `Invoke-SnapshotDrill` (`:1161`) lists the dump through `docker run -v <desktop stage>:/work:ro` (`:1262-1266`). Over a remote context that bind mounts a **server2** path, so it cannot work unchanged | S3 adds a plain drill that copies the dump into the scratch container (D-6) |
| G-4 | The job can "dump FROM server2 via docker exec over ssh" | The plain path refuses any non-`unix://`/`npipe://` endpoint unless `-AllowRemoteDocker` (`:734-756`). That switch allows **any** remote endpoint, tcp:// included, and only prints a warning | Add a narrow `ssh://`-only acceptance for `-Target server2` (D-3). `-AllowRemoteDocker` stays unused |
| G-5 | server2 container name | Compose project `antiphon-server2-postgres`, service `postgres` (`docker-compose.server2-postgres.yml:7-10`), so the container is `antiphon-server2-postgres-postgres-1`. It is already on the production refusal list (`scripts/postgres-snapshot.ps1:83-91`), with the volume `antiphon-server2-postgres_pgdata` (`docker-compose.server2-postgres.yml:47`) | The names are fixed literals for `-Target server2` |
| G-6 | Database and role | Desktop and server2 both use `POSTGRES_DB: antiphon` and `POSTGRES_USER: antiphon` (`docker-compose.server2-postgres.yml:24-26`). The publish runs `pg_dump -U antiphon -d antiphon` inside the container (`:1710-1713`) and needs no password (local socket inside the container) | No secret moves. No connection string anywhere |
| G-7 | Telegram and OneDrive can run anywhere | `Send-JobStatusLine` runs `dotnet run C:\src\ClaudeBot\scripts\tg-send.cs` (`scripts/windmill/job-status.ps1:143-175`). OneDrive is `C:\Users\lndco\OneDrive` (`:1811`). Both are desktop-only | The job stays on the desktop worker (D-1) |
| G-8 | The second verify is local | After the publish it `docker cp`s the published file **back into the source container** and lists it again (`:1753-1766`). Against server2 that is a 0.7 GB upload | Accepted. Keep it (D-4) |
| G-9 | Desktop settings | `docker-compose.dev.yml:16-27`: `image: postgres:16`, `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`, `container_name: antiphon-postgres`. The CARD-1406 compose (`docker-compose.server2-postgres.yml:11-19`) has the same three `-c` settings, plus `mem_limit: 4g` and `cpus: 4.0` | The settings match. CPU isolation is decision 3 of the move, not this plan |
| G-10 | Image pin format | `deploy-server2-postgres.ps1` accepts only `postgres:16@sha256:<64>` or `postgres@sha256:<64>`, and refuses alpine (`scripts/deploy-server2-postgres.ps1:71-78`) | Pin via `postgres:16@sha256:<index digest>` (§ Version rule) |
| G-11 | Scratch image | `[string]$ScratchImage = 'postgres:16'` (`:70`) is a floating tag. `Test-SnapshotImage` refuses only alpine and bad characters (`:146-151`). A fresh `postgres:16` pull today is **16.16** (below) | The server2 drill uses the source container's own image id (D-7) |
| G-12 | Live job health 11-11..11-18 | **UNOBSERVED.** `snapshot-status.json` and the Windmill run history are on the desktop | Window step W-0 reads them |
| G-13 | Desktop → server2 SSH with docker access for user `lndco` | **UNOBSERVED.** No context or key is evidenced in the repo or the notes | Operator prerequisite P-2 |

## Version rule (operator 2026-11-18)

The move and the backup use the **same** PostgreSQL as the desktop: major 16, server 16.14 (`Debian 16.14-1.pgdg13+1`), the Debian variant and not alpine, and the settings in G-9. Nothing is upgraded here.

Registry facts, read 2026-11-18 from the Docker Hub tag API and the registry, anonymously:

| Tag | Index digest | linux/amd64 manifest | Config | `PG_VERSION` | Pushed |
|---|---|---|---|---|---|
| `postgres:16.14-trixie` (= `postgres:16.14`) | `sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` | `sha256:670391653713782e51974845b217c56fed4dd8729142299c43c919a8d3e15e00` | `sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55` | `16.14-1.pgdg13+1` | 2026-08-05 |
| `postgres:16` (= `16-trixie`) today | `sha256:dd2bf3ce2dc83a81674cf7f217fcfd8a5009a4b325dda5b843bfee82a06cff6f` | `sha256:2d5c77ae…af417e87` | `sha256:6b7038c6…a1d9a3` | `16.16-1.pgdg13+1` | 2026-11-15 |
| `postgres:16.14-bookworm` | `sha256:64154d0b…f438efa8` | | | (pgdg12) | |

- **The exact tag that gives 16.14 on pgdg13 is `postgres:16.14-trixie`.** For the env file, pin it as `ANTIPHON_POSTGRES_IMAGE=postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`. The `:16` in that string is ignored when a digest is given. It satisfies `scripts/deploy-server2-postgres.ps1:75`.
- The desktop image id `sha256:f40a5645…` equals **none** of the digests above. It is not the current 16.14 index digest, nor its amd64 config. Two explanations are open: an earlier push of the same 16.14-trixie tag, or a Docker Desktop store that reports a different id kind. Which one is **UNOBSERVED**.
  - Proof of equivalence at the window (W-2): on server2, `docker run --rm <pinned> postgres --version` must print `16.14`. `docker image inspect` of the pin must show `PG_VERSION=16.14-1.pgdg13+1` and `LANG=en_US.utf8`.
  - A bit-identical image id needs `docker save` on the desktop and `docker load` on server2: operator decision O-3.
- What differs in the images the review tasks used:
  - `postgres:16.16` is a newer minor. It is binary compatible and dump-compatible, but it is **not** the same server, and a drill on it proves restore into 16.16, not into 16.14.
  - `postgres:16-alpine` uses musl, not glibc. Collation behaviour differs from the desktop's glibc `en_US.utf8`, so text-index order and `ORDER BY` results can differ after a restore. `deploy-server2-postgres.ps1` refuses alpine for this reason (`:74`).
  - Neither image is acceptable for the drill or the production cluster.
- The upgrade to the latest major is a **separate later card** (draft in § Follow-up). It is not part of this plan.

## Decisions

- **D-1. Keep the job on the desktop Windmill worker. Dump remotely into the existing desktop folders.**
  - Reason:
    - Telegram (`C:\src\ClaudeBot`) and OneDrive are desktop-only (G-7).
    - The stale check reads the desktop folder.
    - One job keeps one failure domain for alerting.
    - `pg_dump` still runs on server2's CPU.
  - Rejected:
    - **(a) Run the job on a server2 Windmill worker** and copy the dump back. That needs a second job or a second copy step for OneDrive and Telegram, and two places for status.
    - **(b) Write onto server2, then pull.** It doubles the moving parts. A server2 disk-full (85% on 11-17) becomes a backup failure mode.
- **D-2. Target selection: one word in `C:\Users\lndco\antiphon-snapshot-runner\snapshot-target`. The only allowed values are `desktop` and `server2`.**
  - The wrapper reads the word and passes `-Target <word>`.
  - A missing file, an empty file or any other value fails closed with `reason=target-invalid`. The wrapper still sends the FAIL Telegram line.
  - `postgres-snapshot.ps1 -Target` defaults to `desktop` for direct callers.
  - The flip is a one-line file write on the desktop. It needs no Windmill API write, no token and no second job.
  - Rejected:
    - **(a) Windmill schedule `args`.** Each flip is an API write needing a minted token, and a manual run could carry a different arg than the schedule.
    - **(b) A free `-Container` from the wrapper.** One word must select the container, project label, volume and context together. A free name could point the dump at the drill or Windmill DB on the same daemon.
    - **(c) Missing file means desktop.** After cutover that silently aims at the stopped desktop container, which fails anyway. Failing closed names the cause instead.
- **D-3. The server2 path uses the Docker context `antiphon-server2` and accepts only an `ssh://` endpoint.**
  - When `$script:SnapshotDockerContext` is set, every docker call in `Invoke-SnapshotNative` gets `--context antiphon-server2`.
  - The daemon check inspects that named context. It refuses `docker-context` unless the endpoint is `ssh://`.
  - `DOCKER_HOST` set to anything stays refused (`docker-host`), because it would override the context.
  - The endpoint is never printed (existing rule, `:732-733`).
  - Rejected:
    - **(a) `-AllowRemoteDocker`.** It admits plaintext `tcp://`.
    - **(b) A forced-command SSH key** that streams `pg_dump` to stdout. Windows PowerShell 5.1 pipes native stdout as text and corrupts a binary archive. It also skips every tested verify step.
    - **(c) Exposing server2 Postgres to `pg_dump` on the desktop.** That needs a password and a connection string on the desktop, and a desktop pg_dump binary.
  - Cost: the desktop holds a key that is docker-equivalent on server2 (root-equivalent). This is operator decision O-1.
- **D-4. Keep the publish verification unchanged for both targets.** That is `pg_dump` → `pg_restore --list` + size in the container → `docker cp` to stage → size equality → `.partial` → rename → upload back → second `--list` → retention → OneDrive.
  - Reason: `Test-PlainPublishPruneOrder` and the 46 harness cases pin this order (G-8). It costs about 0.7 GB in each direction over the LAN.
  - Rejected: replace the round trip with a container-side `sha256sum` equality. It is cheaper but changes a reviewed safety contract under a fast-track.
- **D-5. Before the dump, `-Target server2` checks the source's identity.**
  - `Get-SnapshotContainerFact` (`:856`) on `antiphon-server2-postgres-postgres-1` must be running.
  - It must carry the label `com.docker.compose.project=antiphon-server2-postgres`.
  - It must mount `antiphon-server2-postgres_pgdata` at `/var/lib/postgresql/data`.
  - Otherwise it refuses with `reason=source-mismatch` (exit 9, the existing refusal family).
  - This catches a context that points at the runner container's nested dockerd, the drill project or the Windmill DB. An empty cluster fails `Test-SnapshotPlainVerified` (no TABLE DATA, below 1 MiB) as `verify-failed`, exit 5.
- **D-6. Add a plain restore drill: `-Plain -RestoreDrill -Target server2`.** It replaces the `plain-drill-unsupported` refusal for that target only. The desktop target still refuses, so the desktop never runs a drill.
  - It reuses `Invoke-SnapshotDrill`'s guards: label, id prefix, production names and volumes, and the source mount. Without age it stages the newest local `antiphon-<utc>.dump`.
  - It does **not** use the `-v <stage>:/work:ro` list (G-3). It `docker cp`s the file into the labelled scratch container, then runs `pg_restore --list` and `pg_restore --exit-on-error` inside it.
  - It counts public tables with `psql -tAc` against the **scratch database only**, and prints `drill=1 bytes=<n> tables=<n> seconds=<n>`. It removes the container and volume unless `-KeepScratch`.
  - The scratch names stay `antiphon-postgres-drill`, `antiphon_drill` and `antiphon-postgres-drill-pgdata`, with `--network none`.
  - Rejected: the CARD-1406 `deploy-server2-postgres.ps1 -RestoreDrill`. It expects the encrypted B1 snapshot and runs on the server2 host shell, not from the job.
- **D-7. The server2 drill image is the source container's own image id**, read from `docker inspect` in the same fact read. It is not `$ScratchImage`.
  - Reason: the version rule. The drill proves restore into exactly the server that will run it.
  - With `-Target desktop`, `ScratchImage` stays as it is (age drill only).
- **D-8. The target appears in the records.**
  - `snapshot-status.json` gains `"target":"desktop|server2"` and `"serverVersion":"<postgres --version digits>"`, from `docker exec <c> postgres --version`, parsed to `^[0-9]+\.[0-9]+$` and otherwise `unknown`.
  - The OK line becomes `DB backup: OK 0.7 GB +cloud src=server2`.
  - The FAIL detail becomes `exit <n> <reason> src=<target>`, for example `DB backup: FAIL exit 9 source-mismatch src=server2`. The reason comes from the child's `SNAPSHOT fail exit=<n> reason=<r>` line (`:680-692`) and is checked against `^[a-z-]{1,40}$`.
  - `New-JobStatusRecord`'s 120-character and secret refusals still apply (`job-status.ps1:32-42`).
  - The stale check is unchanged. It reads the same files.
- **D-9. The wrapper's pin model is unchanged.** `Resolve-PlainSnapshotScript` still requires the four literals and a matching `SOURCE-SHA` (`invoke-postgres-snapshot.ps1:10-44`). The runner refresh (R-step) re-pins `SOURCE-SHA` to the new `postgres-snapshot.ps1` and writes `snapshot-target` = `desktop`. The wrapper's own `SOURCE-SHA` gate does not cover `snapshot-target`. That is acceptable because the word is a two-value enum (D-2).
- **D-10. No session waits for input.** This plan adds or changes no session that waits for input, so the parking checklist does not apply (CARD-1083).

## Slices

### S1. `-Target` and the server2 source (script + witnesses)

Files:
- `scripts/postgres-snapshot.ps1`:
  - `param` gets `[ValidateSet('desktop','server2')][string]$Target = 'desktop'`.
  - Add `$script:SnapshotServer2` literals: context, container, project, volume.
  - `Invoke-SnapshotNative` gets the docker context prefix.
  - `Test-SnapshotDockerDaemon` gets the ssh-only branch for the named context.
  - Add the D-5 source fact check in `Invoke-SnapshotPlainMode`.
  - The plan text prints `step source: target=server2 container=antiphon-server2-postgres-postgres-1 context=antiphon-server2`. It never prints the endpoint.
  - `Write-SnapshotPlainStatus` gets the `target` and `serverVersion` fields.
  - `-Target server2` with `-Container` given explicitly refuses with `ambiguous-mode`.
- `scripts/test-postgres-snapshot.ps1`: the new cases below.

### S2. Wrapper target file and status line

Files:
- `scripts/windmill/invoke-postgres-snapshot.ps1`:
  - Add `Get-SnapshotTarget` (reads `snapshot-target` next to `$PSScriptRoot`, enum check).
  - Pass `-Target`.
  - Parse the child reason.
  - Add `src=` to the line.
- `scripts/windmill/job-status.ps1`: `New-PlainBackupCloudRecord -Target`.
- `scripts/test-job-status.ps1`: the new cases.
- `scripts/windmill/antiphon-postgres-snapshot.json` `description`: one sentence naming the target file. The `content` is unchanged.

### S3. Plain drill on server2 (D-6, D-7)

Files: `scripts/postgres-snapshot.ps1` (`Invoke-SnapshotPlainDrill`, which shares the guard prefix with `Invoke-SnapshotDrill`) and `scripts/test-postgres-snapshot.ps1`.

### S4. Docs

Files:
- `scripts/windmill/README.md` § Daily plain Postgres snapshot: the target file, the context prerequisite, and the flip.
- `docs/docker-stack.md`: one paragraph in the CARD-1406 section that links this plan and gives the image pin.

### Files touched (closed list)

- `scripts/postgres-snapshot.ps1`
- `scripts/test-postgres-snapshot.ps1`
- `scripts/windmill/invoke-postgres-snapshot.ps1`
- `scripts/windmill/job-status.ps1`
- `scripts/test-job-status.ps1`
- `scripts/windmill/antiphon-postgres-snapshot.json`
- `scripts/windmill/README.md`
- `docs/docker-stack.md`

All PowerShell stays ASCII-only and parseable by Windows PowerShell 5.1 (daemon-script rule).

### Collisions

- Of the 150 most recent `origin/*` branches, none changes these script files relative to master (checked 2026-11-18).
- CARD-1974/1975 (F-1/F-2) touch `scripts/cutover-postgres-server2.ps1` and its tests only. That is no overlap.
- CARD-1844 S1/S2 touch `bootstrap-check.ps1` and `scripts/lib/*`. That is no overlap.
- CARD-1831 is in Review. Its branch is already landed on master (`82feba880`…`f0155042e`). This follow-up is a new card on new commits and does not reopen CARD-1831's subject branch.
- `tests/Antiphon.Tests/Scripts/PosixHarnessToolTests.cs:52-80` pins `SnapshotCases(windows)` to existing case names and runs them through `test-postgres-snapshot.ps1 -Case`. New cases do not change that list, so it is unaffected. A renamed case would break it, so cases are added only, never renamed.

### Existing tests affected

- Migrations, public DTOs or routes, operation labels, remote script functions, statement budgets: **none**. The CARD-1782 impacted-classes table has no row for these files.
- Script harnesses:
  - Every existing `test-postgres-snapshot.ps1` case must stay green unchanged (R-1). The default `-Target desktop` keeps them on today's path.
  - `PlainStatusNoSecret` and `PlainOneDriveCopy` read `snapshot-status.json`. The two new fields are added at the end, and those cases check fields by name.
  - `test-job-status.ps1` keeps its 70 assertions (R-2). The OK line gains ` src=desktop` only when a target is passed, and the stale check path is untouched.

## Verification design

Red first: each V-n case is committed and run red before its slice's fix (S1 → V-1..V-6, S2 → V-7..V-10, S3 → V-11..V-13). Cases that run a docker fake reuse the harness's POSIX `sh` fake (`Install-SnapshotFake`, `test-postgres-snapshot.ps1:377`). They join `$script:SnapshotPosixFakeCases` (Linux only, SKIP on Windows, which is the existing pattern). Pure-function cases run on both platforms.

- **V-1 `TargetDesktopUnchanged`** (S1, both OS). `-Plain -WhatIf` with no `-Target` and with `-Target desktop`:
  - Prints `step dump: docker exec antiphon-postgres pg_dump`.
  - Has no `step source:` line and no `--context`.
  - Prints `SNAPSHOT plan exit=0 reason=ok`.
  - Red before S1: `-Target` is an unknown parameter.
- **V-2 `TargetServer2Plan`** (S1, both OS). `-Plain -WhatIf -Target server2`:
  - Prints `step source: target=server2 container=antiphon-server2-postgres-postgres-1 context=antiphon-server2`.
  - Prints `docker exec antiphon-server2-postgres-postgres-1 pg_dump`.
  - Contains no `ssh://`, `@` or `postgres://`.
- **V-3 `TargetServer2ContextOnEveryCall`** (S1, POSIX fake). A fake docker logs argv and answers:
  - `context inspect` with `ssh://fake@server2`.
  - `inspect` with the D-5 facts.
  - `pg_dump`, `--list` and `wc` like `PlainPublishPruneOrder`.
  - `-Plain -Execute -Target server2` exits 0.
  - **Every** logged docker argv starts with `--context antiphon-server2`.
  - The published file lands in the fixture destination.
- **V-4 `TargetServer2RefusesTcp`** (S1, POSIX fake). Two refusals:
  - The context endpoint is `tcp://10.0.0.1:2375`. The result is `reason=docker-context`, with no `exec` logged.
  - `DOCKER_HOST=ssh://x` is set. The result is `reason=docker-host`.
- **V-5 `TargetServer2SourceMismatch`** (S1, POSIX fake). Three variants each give `SNAPSHOT fail exit=9 reason=source-mismatch`, with no `pg_dump` logged:
  - The project label is `antiphon-server2-postgres-drill`.
  - The mount is `antiphon-server2-postgres-drill_pgdata`.
  - The container is not running.
- **V-6 `TargetStatusFields`** (S1, both OS). `Write-SnapshotPlainStatus` with target `server2` and version `16.14` writes `"target":"server2","serverVersion":"16.14"`. A version text `16.14 (Debian 16.14-1.pgdg13+1)` is reduced to `16.14`, and junk becomes `unknown`.
- **V-7 `TargetFileValues`** (S2, test-job-status, both OS). `Get-SnapshotTarget` on a temp dir:
  - Missing, empty, `Server2 `, `server3`, or two lines give `''`, and the wrapper path reports `reason=target-invalid`.
  - `desktop` and `server2` (with trailing CRLF) return the word.
- **V-8 `TargetInvalidSendsFail`** (S2, both OS). With the child and `Send-JobStatusLine` replaced by recorders and a bad target file:
  - No child starts.
  - The line is `DB backup: FAIL exit 9 target-invalid src=none`.
  - It is sent once.
- **V-9 `OkLineNamesSource`** (S2, both OS). `New-PlainBackupCloudRecord -Bytes 750000000 -OneDrive ok -Target server2` gives `OK 0.7 GB +cloud src=server2`. Without `-Target`, today's exact string is unchanged.
- **V-10 `FailLineCarriesReason`** (S2, both OS). Child stdout `SNAPSHOT fail exit=4 reason=dump-failed` gives detail `exit 4 dump-failed src=server2`. A reason with a space or `=` falls back to `exit 4`.
- **V-11 `PlainDrillServer2`** (S3, POSIX fake). `-Plain -RestoreDrill -Execute -Target server2` with one local `antiphon-20261118T075000Z.dump`. In order:
  - `run` with the label, `--network none`, and the image equal to the source fact's image id.
  - `cp` into `<scratch>:/tmp/antiphon-drill.dump`.
  - `pg_restore --list` and `pg_restore --exit-on-error -d antiphon_drill` inside the scratch container.
  - `psql` only against `antiphon_drill`.
  - `rm -f` on the scratch container and `volume rm` on the scratch volume.
  - The output contains `drill=1` and `tables=`.
  - No argv contains `-v` with a `:/work` bind.
- **V-12 `PlainDrillDesktopRefused`** (S3, both OS). `-Plain -RestoreDrill` (desktop target) still gives `reason=plain-drill-unsupported`.
- **V-13 `PlainDrillRefusesProduction`** (S3, POSIX fake). Each of these refuses before any `run`:
  - The scratch container named `antiphon-server2-postgres-postgres-1`.
  - The scratch volume `antiphon-server2-postgres_pgdata`.
  - The scratch database `antiphon`.
- **R-1.** `test-postgres-snapshot.ps1` full harness green: every existing case plus V-1..V-6 and V-11..V-13.
- **R-2.** `test-job-status.ps1` full harness green: 70 existing plus V-7..V-10.
- **R-3.** The `PosixHarnessToolTests` snapshot pair stays green. It is not run as a row: it runs the same `-Case` names covered by R-1, and its C# list is unchanged. Review confirms the list by diff.

No PC-n is named. Mutation is waived (operator 2026-11-12) and is not discharged.

Real-system proof belongs to the operator and is not a checkpoint: the window steps W-3 and W-4 below.

### Checkpoints

Lane: server2 (the runner default `globalRunnerId=server2`). No `-Runner`, no `-Platform`. All rows are short command rows with no build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetDesktopUnchanged` | V-1 | 0 failed, exit 0 | n/a | 1 |
| CP-2 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2Plan` | V-2 | 0 failed, exit 0 | n/a | 1 |
| CP-3 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2ContextOnEveryCall` | V-3 | 0 failed, exit 0 | n/a | 1 |
| CP-4 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2RefusesTcp` | V-4 | 0 failed, exit 0 | n/a | 1 |
| CP-5 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2SourceMismatch` | V-5 | 0 failed, exit 0 | n/a | 1 |
| CP-6 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetStatusFields` | V-6 | 0 failed, exit 0 | n/a | 1 |
| CP-7 | S2 | n/a | job-status | `pwsh -NoProfile -File scripts/test-job-status.ps1` | V-7, V-8, V-9, V-10, R-2 | 0 failed, exit 0 | n/a | 2 |
| CP-8 | S3 | n/a | snapshot-drill | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillServer2` | V-11 | 0 failed, exit 0 | n/a | 1 |
| CP-9 | S3 | n/a | snapshot-drill | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillDesktopRefused` | V-12 | 0 failed, exit 0 | n/a | 1 |
| CP-10 | S3 | n/a | snapshot-drill | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillRefusesProduction` | V-13 | 0 failed, exit 0 | n/a | 1 |
| CP-11 | all | n/a | snapshot-regression | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1` | R-1 | 0 failed, exit 0 | n/a | 6 |

Code adds the exact assertion counts to Expect when it commits each case. The harness prints `<n> passed, <m> failed`.

Owed off-row:
- A Windows PowerShell 5.1 parse of the three edited scripts. The run is `powershell -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile(...)"` on the desktop. Because of the live run f2de5b30, it happens **after** that run ends (window step W-1), not during Code.
- The 11-10 refresh recorded `Test-PlainPublishPruneOrder` as Windows-red by construction (the `cp` fake, `b91c22df1`). `f0155042e` fixed it on master. Its Windows status is not re-proven here.

## Performance budget

Per [docs/testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards):

- **Rows.**
  - CP-1..CP-6 and CP-8..CP-10 are single cases: pwsh start plus one fixture, under 1 min each.
  - CP-7 is one 70+ assertion harness. The 11-10 Windows run is the only evidence; its duration is UNOBSERVED. Estimate 2 min.
  - CP-11 is the full snapshot harness. Baseline on master `e229fb4d3`, server2 runner under a build slot, 2026-11-18: <!--CP11-->.
- **Phases.** Each row is pwsh start (~1–2 s), then fixture creation in a temp dir, then the case. There is no build, Docker, network or database.
- **Shared setup.** None is needed. Each case builds its own temp fixture. The `sh` docker fake is installed per case, as the existing cases do.
- **Timeouts.** The harness has no internal timeout. The checkpoint row deadline is derived from `EstimatedMinutes`, about 2× the measured p95. The C# `PosixHarnessToolTests` per-case 90 s bound is unchanged.
- **Ordinary floor.** 17 minutes summed. It runs as three After groups (S1, S2, S3/all) on server2. Nothing runs on the desktop during the live run.

## Prerequisites (operator, before the window)

- **P-1.** CARD-1406 cluster deployed on server2 with `ANTIPHON_POSTGRES_IMAGE=postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`, or the `docker load`ed desktop image per O-3. Move decisions 2 and 3 cover this.
- **P-2.** On the desktop as `lndco`: an SSH key for a server2 host account in the `docker` group, plus `docker context create antiphon-server2 --docker host=ssh://<user>@<server2 LAN or tailnet name>`.
  - The check: `docker --context antiphon-server2 version`, which prints the server version and not the endpoint.
  - The Windmill SSH-bridge session runs as `lndco` (`antiphon-postgres-snapshot.json` `content`), so the context must be in that profile.
  - `known_hosts` must hold server2's host key. Docker's ssh helper uses the system `ssh`, which has `BatchMode` implied by no tty.
- **P-3.** The runner refresh happens once the code lands, any time before the window. It is a file copy only, with no dump.
  - Copy the reviewed `postgres-snapshot.ps1`, `invoke-postgres-snapshot.ps1` and `job-status.ps1` into `C:\Users\lndco\antiphon-snapshot-runner`.
  - Write `SOURCE-SHA` = sha256 of the copied `postgres-snapshot.ps1`.
  - Write `snapshot-target` = `desktop` (ASCII, no BOM).
  - Verify with a dot-sourced `Resolve-PlainSnapshotScript` and `Get-SnapshotTarget`, as `b91c22df1` did.
  - The 07:50 run after that must still print `src=desktop` OK.

## Cutover-window steps (inside move decision 5's 60-minute window, after cutover phase 3 passes)

- **W-0.** Before the window, read-only: read the desktop `snapshot-status.json` and the Windmill run history for 11-11..today (G-12).
- **W-1.** Run the Windows PowerShell 5.1 parse of the three scripts in the runner directory (owed off-row).
- **W-2.** On server2, run `docker image inspect` of the production container's image. It must show `PG_VERSION=16.14-1.pgdg13+1` and `LANG=en_US.utf8`, not alpine. Also run `docker exec antiphon-server2-postgres-postgres-1 postgres --version` and confirm `16.14`.
- **W-3.** **Flip:** write `server2` into `C:\Users\lndco\antiphon-snapshot-runner\snapshot-target`. **Manual snapshot:** run the same entry the schedule runs, via Windmill "Run now" on `u/lndcobra/antiphon_postgres_snapshot` (or the same `powershell -File …\invoke-postgres-snapshot.ps1` over the bridge). Expect:
  - `SNAPSHOT ok exit=0`.
  - `ONEDRIVE COPY oneDrive=ok`.
  - `DB backup: OK <size> +cloud src=server2` and `telegram=0`.
  - `snapshot-status.json` with `"target":"server2","serverVersion":"16.14"`.
  - Record the dump seconds, bytes and table count against the 11-10 baseline (78.1 s, 530 MB, 139 tables). The DB is about 3 GB now, so expect about 0.7 GB.
- **W-4.** **Restore proof:** run `postgres-snapshot.ps1 -Plain -RestoreDrill -Execute -Target server2` from the runner directory. It restores the W-3 dump into a throwaway scratch container on server2 with the production image id (D-7). Expect:
  - `drill=1` with `tables=` equal to the cutover's verified table count. `pg_restore` exits 0.
  - The scratch container and volume are gone afterwards. Check with `docker --context antiphon-server2 ps -a --filter label=antiphon.snapshot-drill` → empty.
  - Record the restore seconds.
- **W-5.** If W-3 or W-4 fails, the window's rollback (cutover phase 6) restarts `antiphon-postgres`. Write `desktop` back into `snapshot-target`. The next 07:50 run then proves `src=desktop`.
- **W-6.** The next scheduled 07:50 run must print `src=server2` OK, and the 08:00 stale check must print `JOB STATUS fresh`. The orchestrator confirms this from the job output the following morning.

## Operator decisions (yes/no, with recommendation)

- **O-1.** Allow a desktop SSH key with docker-group access on server2, which is root-equivalent there, for the backup context (D-3)? **Recommend yes.** It is the only option that keeps the tested publish path and keeps secrets off the desktop. Scope it to a dedicated server2 account in the `docker` group.
- **O-2.** Fail closed when `snapshot-target` is missing (D-2)? **Recommend yes.** The runner refresh (P-3) writes `desktop` first.
- **O-3.** Require a bit-identical image (`docker save` on the desktop, `docker load` on server2), or accept the registry pin `postgres:16@sha256:95206741…` (16.14-1.pgdg13+1, trixie)? **Recommend the registry pin**, with the W-2 check. The server version and the glibc/Debian variant are the parts that matter for a restore. Choose yes to the bit-identical image only if exact image-id equality with `f40a5645…` is wanted.
- **O-4.** Keep the 0.7 GB re-upload for the second verify (D-4)? **Recommend yes** for the move. Revisit it on the upgrade card.
- **O-5.** Keep the job's Windmill schedule, tag `desktop` and `content` unchanged, so no Windmill write is needed? **Recommend yes.**

## Follow-up (draft card text; the orchestrator files it after a duplicate search)

**PostgreSQL major upgrade after the server2 move is stable.** Move the server2 production cluster from 16.14 to the latest major. A separate plan picks `pg_upgrade --link` or dump/restore, using a dump from this job as the rollback point. Its own window, rollback and restore drill are on the new major's image, pinned by digest. It updates:
- the drill image rule (D-7 follows the source automatically);
- the `postgres:16@sha256` acceptance in `deploy-server2-postgres.ps1:75`;
- the desktop dev compose `image: postgres:16`.

Gate: 14 consecutive green `src=server2` days, plus one successful W-4-style drill on the new image. It is not part of the move.
