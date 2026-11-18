# Empty server2 Postgres cluster: one-step deploy package (2026-11-18)

Investigate task `61c8b8b6`. Preparation only, read-only. Observations 2026-11-18 11:45–11:50Z from the server2 runner container `7c9c24dac62d` (a session-runner: PID 1 is `antiphon-dind-entrypoint.sh dotnet Antiphon.SessionRunner.dll`).

- Branch read: `feat/card-task-61c8b8b6` at `0f13b84917d2ef2317f6025814e3b46a3c6c2645` (the CARD-1406 CPU isolation tip, landing in Merge 270).
- Master read: `7f9c6d365d34ef05d0c6316f0d04f6c05520646b` (origin/master at 11:46Z; it carries the G-2 patch-pin fix `d248cdf09f`).
- Inputs: `docs/investigations/2026-11-18-postgres-move-runbook.md` (A.1–A.3, gap list), the CPU-isolation and restore-drill plans of 2026-11-18, `scripts/deploy-server2-postgres.ps1`, both server2 Postgres compose files, `docs/docker-stack.md` (CARD-1406 sections, `:2058-2112`), `docs/agent-credentials.md`.

What I ran: the script's `-WhatIf` previews (no docker call in that mode), dot-sourced `Test-Server2PostgresImage`, `git merge-tree`, `/sys` and cgroup reads in this container, TCP connects with no protocol bytes, anonymous Docker Hub manifest/config reads (no pull). Nothing was created, started, pulled or written on any host. No SQL. No desktop command. No secret read.

What I could **not** run:

- **The desktop checkout is not reachable from this runner**, so the `-WhatIf` ran on the runner mirror with Linux pwsh. The script code is the same; only the printed paths differ.
- **`ssh mc@server2` is refused from the runner**: `Permission denied (publickey)`; the container has no `~/.ssh` and no agent. The runner's `docker.sock` is the **nested** daemon (`docker info` Name = `7c9c24dac62d`, 27.5.1), not the host. Every host-daemon item below is therefore **UNOBSERVED** unless it is a kernel-wide fact (`/sys`, `/proc`, the filesystem under the container overlay) or a network probe.

## Verdict

**The empty cluster can be deployed in one operator sitting once Merge 270 has landed, with one ordering rule and two operator inputs.**

1. **Ordering rule (measured): run the script and the compose files from the SAME tree, and that tree must be post-Merge-270 master.**
   - The branch script and the branch compose pass: `SERVER2-POSTGRES plan exit=0 reason=ok`.
   - **Master's script (`7f9c6d36`) refuses the CARD-1406 compose with `compose-invalid`, exit 2.** It still requires the old literals `mem_limit: 4g` and `cpus: 4.0` (`git show origin/master:scripts/deploy-server2-postgres.ps1`, `:184-185`).
   - The branch script refuses the operator's pin form `postgres:16.14@sha256:…` (`image-floating`), because it predates G-2. It accepts `postgres:16@sha256:9520…`.
   - The merged tree (`git merge-tree --write-tree origin/master HEAD` = `1fcb4e3b…`) has both: it accepts every pin form and passes the cpuset compose (preview below).
   - **Merge 270 has a textual conflict to resolve**: `scripts/test-server2-postgres-compose.ps1` conflicts between master (`708e9bc454`, the G-2 red witness) and this branch. The deploy script and both compose files merge cleanly.
2. **The two operator inputs are the env file (with a new password) and the go-ahead for `-Execute`.** Nothing else is the operator's to supply. The image pin is fixed by the version rule.
3. **The image ID will not equal the desktop's (measured; not a defect of the pin).** The pin `postgres:16@sha256:95206741…` resolves today (11:49Z) to the linux/amd64 manifest `sha256:67039165…` and the config `sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55`. Its `PG_VERSION` is `16.14-1.pgdg13+1`, it was built 2026-08-05, and its 14 layers are 160,173,939 bytes compressed.
   - On server2's classic store, `docker image inspect` will show `Id = sha256:8a4b0458…`, **not** the desktop's `sha256:f40a5645…` (the runbook found f40a5645 matches neither this config nor the index).
   - The version equality is therefore proven by `PG_VERSION` / `SHOW server_version` (16.14, pgdg13), not by image ID. The desktop RepoDigest is **UNOBSERVED**.

## -WhatIf previews (secret-free, measured 11:46Z, runner mirror)

| # | Script | Compose | Output | Exit |
|---|---|---|---|---|
| a | branch `0f13b849` | branch production | `docker compose --env-file <env-file> -f /work/worktrees/ws-02-d3/docker-compose.server2-postgres.yml -p antiphon-server2-postgres up -d` / `SERVER2-POSTGRES plan exit=0 reason=ok` | 0 |
| a2 | branch | branch drill (`-RestoreDrill`) | 13 `drill-command:` lines, ending `… docker volume rm antiphon-server2-postgres-drill_pgdata; fi` / `SERVER2-POSTGRES plan exit=0 reason=ok` | 0 |
| b | **master `7f9c6d36`** | branch production | `SERVER2-POSTGRES fail exit=2 reason=compose-invalid` | **2** |
| c | merged tree `1fcb4e3b` | merged production | `docker compose --env-file <env-file> -f <root>/docker-compose.server2-postgres.yml -p antiphon-server2-postgres up -d` / `SERVER2-POSTGRES plan exit=0 reason=ok` | 0 |
| c2 | merged | merged drill | `… plan exit=0 reason=ok` | 0 |
| d | branch, `-EnvFile /home/mc/antiphon-server2/postgres.env` (absent here) | — | `SERVER2-POSTGRES fail exit=2 reason=env-missing` | 2 |

`-WhatIf` without `-EnvFile` does not validate the image. With an env file it validates mode 0600, outside-repo, bind, image, non-empty and non-dev password (`:402-441`). So the env-bearing preview in step S5 below is the real pre-flight of the pin. Pin acceptance, measured via dot-sourced `Test-Server2PostgresImage`:

| Image value | branch | master | merged |
|---|---|---|---|
| `postgres:16@sha256:95206741…74b0b` | accepted | accepted | accepted |
| `postgres:16.14@sha256:95206741…` | `image-floating` | accepted | accepted |
| `postgres:16.14-trixie@sha256:95206741…` | `image-floating` | accepted | accepted |
| `postgres:16`, `postgres:16.14` | `image-floating` | `image-floating` | `image-floating` |
| `postgres:16-alpine@sha256:…` | `image-alpine` | `image-alpine` | `image-alpine` |

## server2 preflight (read-only)

| Item | Reading | Status |
|---|---|---|
| Docker data root and free space | The container overlay's `lowerdir` is under `/var/lib/docker/overlay2/…` (host paths), on `/dev/mapper/ubuntu--vg-ubuntu--lv`, ext4, 704.7 GiB size, 437.3 GiB used, **238.7 GiB available (65%)**. The gate is ≥ 20 GiB: **passes by about 12×.** | Observed through the overlay. The host `docker info .DockerRootDir` string is **UNOBSERVED** |
| Planned volume location | `antiphon-server2-postgres_pgdata` will be a local named volume under the data root, i.e. the same LV | Inferred |
| Host docker / compose versions | — | **UNOBSERVED** (nested daemon is 27.5.1; the runbook proved `-p` exec without a file on compose v2.32.4 only) |
| cgroup version | v1: `/sys/fs/cgroup` is tmpfs with `cpuset`, `cpu,cpuacct`, `memory`, `blkio` (with `blkio.weight`); no `cgroup.controllers` | Observed (kernel 4.15.0-213, uptime 239 d) |
| cpuset keys work | This container's `cpuset.cpus` = `0-23`, `cpuset.mems` = `0-1`, CFS quota `-1`, memory limit unlimited. v1 `cpuset`/`cpu`/`memory`/`blkio` controllers are present, so `CpusetCpus`, `NanoCpus`, `Memory`, `BlkioWeight` map. | Observed for the controllers. The host daemon's acceptance of `blkio_config.weight` is **UNOBSERVED** (the CFQ/BFQ scheduler on the LV decides whether it has effect) |
| Online CPUs | `/sys/devices/system/cpu/online` = `0-23`, `nproc` 24, 2 × Xeon X5690, 6 cores/socket, 2 threads/core | Observed |
| SMT siblings | cpu0 `0,12`; cpu1 `1,13`; cpu2 `2,14`; cpu12 `0,12`; cpu13 `1,13`; cpu14 `2,14`; cpu3 `3,15`; cpu11 `11,23`. **0-2 pair with 12-14: confirmed.** | Observed |
| NUMA (new fact) | node0 = even CPUs, node1 = odd. So `0-2,12-14` = two cores on node0 (cpu0/12, cpu2/14; package 1) and one core on node1 (cpu1/13; package 0). No `cpuset.mems` in the compose, so memory comes from both nodes | Observed. Not a blocker; the plan said "do not invent a per-node pin" |
| CPU load | loadavg 8.40 / 5.68 / 4.57 on 24 CPUs (11:48Z). Memory 125 GiB total, 96 GiB available | Observed |
| Runner cpuset today | This runner container: `0-23`. `docker inspect` of `antiphon-runner-session-runner-1` and `build-slots` | Host inspect **UNOBSERVED**. Expected `""` (no cpuset) until the runner compose with `3-11,15-23` is redeployed; the runner live cpuset `0-23` matches that |
| Image already pulled on the host | — | **UNOBSERVED** (`docker image inspect` needs the host daemon) |
| Registry reachable, digest valid | `HEAD/GET registry-1.docker.io/v2/library/postgres/manifests/sha256:95206741…` → HTTP/2 200, OCI index, `docker-content-digest` equal to the pin | Observed from the runner (same egress as the host is **UNOBSERVED**) |
| Host pwsh, age, tailscale CLI | — | **UNOBSERVED**. pwsh is needed for `-Execute`; `tailscale` must be on PATH or `-Execute` refuses `bind-not-tailnet` (`:455-459`); age is not needed for the empty cluster |
| Port 5432 | TCP connect to `100.93.77.126:5432` → ECONNREFUSED; `192.168.4.34:5432` → ECONNREFUSED (11:48:56Z). `100.93.77.126:5433` open (Windmill), `192.168.4.34:5433` refused, `100.93.77.126:22` open | Observed from inside the runner (through Docker NAT). A RST, not a timeout, so no DROP firewall rule on that path |
| Reach from the desktop | Via the tailnet bind `100.93.77.126:5432` only; LAN `192.168.4.34:5432` must stay refused | Desktop path **UNOBSERVED** (desktop not reachable from here) |
| ufw / iptables | — | **UNOBSERVED**. Docker-published ports bypass ufw's INPUT chain anyway |
| Planned names absent | Container `antiphon-server2-postgres-postgres-1`, volume `antiphon-server2-postgres_pgdata`, network `antiphon-server2`, drill volume/network | **UNOBSERVED on the host**. Absent on the nested daemon (irrelevant). `antiphon-server2` is also the network the CARD-1744 server compose joins; if it already exists without compose labels, `up` warns or fails |

## The one-step operator package

Hosts: **D** = desktop main checkout `C:\src\Antiphon` (pwsh 7, has `ssh mc@server2`). **S** = server2 host shell as `mc` (never the runner container: the script refuses `/.dockerenv`, `:316`). Staging dir proposal `/home/mc/antiphon-server2/postgres` (0700). Env file `/home/mc/antiphon-server2/postgres.env` (0600, outside any repo).

### What the operator supplies

- **Env file** on **S** at `/home/mc/antiphon-server2/postgres.env`, mode 0600, owner `mc`. Three variables, no others:
  - `ANTIPHON_POSTGRES_BIND_ADDRESS` = the single line of `tailscale ip -4` (expected `100.93.77.126`).
  - `ANTIPHON_POSTGRES_IMAGE` = exactly `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` (the operator's pin line; `postgres:16.14@sha256:<same>` is also accepted on post-`d248cdf09f` master).
  - `POSTGRES_PASSWORD` = new, random, letters and digits only (no `$`, `;`, `=`, quote or space: compose interpolates `$`, and the Npgsql connection string splits on `;`/`=`). Not `antiphon_dev`.
- **Password custody.** `docs/agent-credentials.md` has no Postgres row (gap G-18), so no store is mandated today. Two options in the established patterns:
  - **Recommended: a new Bitwarden item** (proposed name `antiphon/server2/postgres-password`, login password field, generator length 40, letters+digits). Stream it over SSH stdin into the env file, the same way `refresh-server2-github-token.ps1` and the Claude token refresh move vault values (`agent-credentials.md:325,367-369,457`). The desktop needs this same value at the cutover (`conn.txt`, then the AppHost user-secret), so a durable vault copy avoids reading it back off server2.
  - Alternative (runbook A.1.3): `openssl rand -hex 24` written straight into the env file on S. The env file is then the only copy until the cutover.
  - **Who may run it: the operator only** (or the operator's ClaudeBot lane with an unlocked vault). No delegate or orchestrator creates, reads or moves this value.
- **The go-ahead for `-Execute`**: runbook §C and the CPU-isolation plan make it operator-only, because it reads the secret-bearing file.

### Ordered commands

| # | Host | Command | Precondition | Expected |
|---|---|---|---|---|
| D0 | D | `git -C C:\src\Antiphon fetch origin; $M = git -C C:\src\Antiphon rev-parse origin/master; git -C C:\src\Antiphon merge-base --is-ancestor d248cdf09f $M; $?; git -C C:\src\Antiphon show "${M}:scripts/deploy-server2-postgres.ps1" \| Select-String -SimpleMatch 'cpuset: "0-2,12-14"'` | Merge 270 landed | `True`, then two matching lines (production and drill checks). If no match: **stop**, Merge 270 is not in master yet (preview b) |
| D1 | D | `docker image inspect postgres:16 --format '{{.Id}} {{json .RepoDigests}}'`; `docker image inspect postgres:16 --format '{{range .Config.Env}}{{println .}}{{end}}' \| Select-String PG_VERSION` | Metadata only; no container, no SQL | `PG_VERSION=16.14-1.pgdg13+1`. Record `Id` (expected `sha256:f40a5645…`) and RepoDigests. A RepoDigest of `postgres@sha256:95206741…` would make the two bit-identical |
| D2 | D | `$t = "$env:TEMP\pg-stage-$($M.Substring(0,12)).tar"; git -C C:\src\Antiphon archive -o $t $M scripts/deploy-server2-postgres.ps1 docker-compose.server2-postgres.yml docker-compose.server2-postgres-drill.yml docker-compose.dev.yml; scp $t mc@server2:/tmp/pg-stage.tar; ssh mc@server2 'install -d -m 0700 /home/mc/antiphon-server2/postgres && tar -xf /tmp/pg-stage.tar -C /home/mc/antiphon-server2/postgres && rm /tmp/pg-stage.tar && cd /home/mc/antiphon-server2/postgres && sha256sum scripts/deploy-server2-postgres.ps1 docker-compose.server2-postgres*.yml'` | D0 passed | Three hashes. Record them with `$M`. `docker-compose.dev.yml` is staged only so the script's `dev-password` check has its input (`:126-133`); without it that check is skipped silently |
| S1 | S | `docker info -f '{{.Name}} {{.ServerVersion}} {{.DockerRootDir}} {{.CgroupVersion}} {{.CgroupDriver}}'; hostname; df -BG "$(docker info -f '{{.DockerRootDir}}')"; docker compose version; command -v pwsh tailscale; tailscale ip -4; cat /sys/devices/system/cpu/online` | — | Name equals hostname (the script's host-lane test, `:306-321`); CgroupVersion 1; ≥ 20G avail (expect ~238G); compose v2; both commands found; exactly one line `100.93.77.126`; `0-23` |
| S2 | S | `docker ps -a --format '{{.Names}}' \| grep -E 'postgres\|antiphon-server2' ; docker volume ls -q \| grep -E 'antiphon-server2-postgres'; docker network ls --format '{{.Name}}' \| grep -x antiphon-server2; ss -ltn \| grep -E ':(5432\|5434)\b'; docker inspect -f '{{.Name}} cpuset={{.HostConfig.CpusetCpus}}' antiphon-runner-session-runner-1 $(docker ps -q --filter name=build-slots)` | — | No `antiphon-server2-postgres*` container or volume; note whether network `antiphon-server2` exists; nothing on 5432/5434; runner and build-slots `cpuset=` empty (runner pin not deployed yet) |
| S3 | S | `docker image inspect postgres@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b --format '{{.Id}}' 2>/dev/null \|\| echo not-pulled` | — | `not-pulled` or `sha256:8a4b0458…db55` |
| S4 | D (vault) or S | **Env file** (operator). Vault option, from D with `BW_SESSION` set: `bw get password '<item id>' \| ssh mc@server2 'umask 077; f=/home/mc/antiphon-server2/postgres.env; IFS= read -r p; { printf "ANTIPHON_POSTGRES_BIND_ADDRESS=%s\n" "$(tailscale ip -4)"; printf "ANTIPHON_POSTGRES_IMAGE=%s\n" "postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b"; printf "POSTGRES_PASSWORD=%s\n" "$p"; } > "$f.tmp" && chmod 600 "$f.tmp" && mv "$f.tmp" "$f"; unset p'`. openssl option, on S: the same block with `p=$(openssl rand -hex 24)` instead of `read` | S1 printed exactly one tailscale line | No output. `stat -c '%a %U' /home/mc/antiphon-server2/postgres.env` → `600 mc`; `grep -c = …` → 3. Never `cat` it |
| S5 | S | `pwsh -NoProfile -File /home/mc/antiphon-server2/postgres/scripts/deploy-server2-postgres.ps1 -WhatIf -EnvFile /home/mc/antiphon-server2/postgres.env` | S4 done | `docker compose --env-file /home/mc/antiphon-server2/postgres.env -f /home/mc/antiphon-server2/postgres/docker-compose.server2-postgres.yml -p antiphon-server2-postgres up -d` then `SERVER2-POSTGRES plan exit=0 reason=ok`. Any `fail exit=2 reason=…` stops: `env-mode`, `bind-*`, `image-*`, `empty-password`, `dev-password`, `compose-invalid` |
| S6 | S | `docker pull postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` | S5 ok | ~160 MB; then S3 prints `sha256:8a4b0458…db55` |
| S7 | S | `pwsh -NoProfile -File /home/mc/antiphon-server2/postgres/scripts/deploy-server2-postgres.ps1 -Execute -EnvFile /home/mc/antiphon-server2/postgres.env` | S5 ok, S6 done | `SERVER2-POSTGRES ok exit=0 reason=ok`. Refusals: `not-server2-host` (wrong shell), `bind-not-tailnet`, `compose-up-failed`. Without host pwsh: run the S5 printed line by hand |
| S8 | S | `until [ "$(docker inspect -f '{{.State.Health.Status}}' antiphon-server2-postgres-postgres-1)" = healthy ]; do sleep 5; done` (bounded by the healthcheck: start 10 s + 12 × 5 s) | S7 ok | `healthy` within ~70 s |

### Verification after the deploy

On **S**, with `C=antiphon-server2-postgres-postgres-1`:

```bash
docker inspect -f '{{.Config.Image}} id={{.Image}} cpuset={{.HostConfig.CpusetCpus}} nanocpus={{.HostConfig.NanoCpus}} mem={{.HostConfig.Memory}} shm={{.HostConfig.ShmSize}} blkio={{.HostConfig.BlkioWeight}} restart={{.HostConfig.RestartPolicy.Name}} ports={{json .HostConfig.PortBindings}}' $C
docker exec $C psql -U antiphon -d antiphon -Atc "show server_version"
docker exec $C psql -U antiphon -d antiphon -Atc "select name||'='||setting||coalesce(unit,'') from pg_settings where name in ('max_connections','shared_buffers','shared_preload_libraries','pg_stat_statements.track','pg_stat_statements.max','password_encryption','listen_addresses') order by 1"
docker exec $C psql -U antiphon -d antiphon -Atc "select name, default_version from pg_available_extensions where name='pg_stat_statements'"
docker exec $C psql -U antiphon -d antiphon -Atc "select datcollate, datctype, encoding from pg_database where datname='antiphon'"
docker exec $C df -h /dev/shm
cat /sys/fs/cgroup/cpuset/docker/$(docker inspect -f '{{.Id}}' $C)/cpuset.cpus    # cgroupfs driver path; skip if systemd driver
```

Expected:

- `postgres:16@sha256:95206741… id=sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55 cpuset=0-2,12-14 nanocpus=6000000000 mem=8589934592 shm=1073741824 blkio=800 restart=unless-stopped`, and the port binding `HostIp 100.93.77.126, HostPort 5432` only.
- `16.14 (Debian 16.14-1.pgdg13+1)`.
- `max_connections=100`, `shared_buffers=163848kB` (16384 × 8 kB = 128 MB; the compose sets neither, same as the desktop), `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`, `password_encryption=scram-sha-256`, `listen_addresses=*`.
- `pg_stat_statements|1.10`. The extension itself is created by the restore, not now.
- `/dev/shm` 1.0G.
- The empty-cluster CPU sample (plan V-4) is a separate operator step; it is not part of this package.

From **D** (reachability; no password, no SQL):

- `Test-NetConnection 100.93.77.126 -Port 5432` → `TcpTestSucceeded : True`. `Test-NetConnection 192.168.4.34 -Port 5432` → False (tailnet-only bind).
- `pg_isready -h 100.93.77.126 -p 5432 -U antiphon -d antiphon` → `accepting connections`, if a Postgres client is installed on the desktop (**UNOBSERVED**). Do not use `docker exec antiphon-postgres` for this; that is the production container.
- Version equality: D1's `PG_VERSION=16.14-1.pgdg13+1` against S's `show server_version` = `16.14 (Debian 16.14-1.pgdg13+1)`. **The image IDs will differ** (`f40a5645…` against `8a4b0458…`); see Verdict 3.

From the runner (orchestrator): repeat the 11:48Z TCP probe. `100.93.77.126:5432` must change from refused to open, and `192.168.4.34:5432` must stay refused.

### Rollback: remove the empty cluster

Only valid **before** any restore into it. On **S**:

```bash
cd /home/mc/antiphon-server2/postgres
docker compose --env-file /home/mc/antiphon-server2/postgres.env -f docker-compose.server2-postgres.yml -p antiphon-server2-postgres down
docker volume rm antiphon-server2-postgres_pgdata
```

- `down` removes the container and the project network `antiphon-server2`. If another project's container is attached to that network (the CARD-1744 server compose joins it), the network removal fails and it is left; that is harmless. `down` without `-v` keeps the named volume; the second line removes it, and with it the empty cluster. Never `down -v` on a cluster that holds restored data.
- `--env-file` is required: every variable is `:?`-required, so `down` without it fails interpolation.
- It leaves: the pulled image (`docker image rm postgres@sha256:95206741…` to drop it), the staged files in `/home/mc/antiphon-server2/postgres`, the env file, and the vault item. Delete the env file with `shred -u` only if the password is being abandoned.
- It touches nothing on the desktop, nothing in the runner project, and not Windmill.

## Who does what

| Step | Orchestrator, autonomously | Operator |
|---|---|---|
| D0 (Merge 270 in master) | yes | — |
| D1 (desktop image metadata) | possible from a desktop-lane session; metadata only | default |
| D2 (stage files to S) | technically yes from the desktop lane (it has `ssh mc@server2`; this runner does not). It writes to the host, so it is outside this task's read-only authority; a new go-ahead is needed | yes |
| S1–S3 preflight reads | yes, from the desktop lane over ssh | — |
| S4 env file, password, vault item | **no** | **yes** (custody) |
| S5 env preview | runs the script against the secret-bearing file, so operator | **yes** |
| S6 pull, S7 `-Execute`, S8 wait | **no** (operator-only per runbook §C) | **yes** |
| Verification on S | the `docker inspect` and TCP reads, yes; the `psql` reads run inside the new empty cluster: operator, or the orchestrator if delegated | default |
| Rollback | — | **yes** |
| Runner cpuset `3-11,15-23` live | a `--no-build` recreate of `antiphon-runner` is an orchestrator rolling phase, with no server2 tasks in flight (`docs/docker-stack.md:2076`) | approves timing |

## GO / NO-GO

| # | Gate | State |
|---|---|---|
| 1 | Merge 270 landed and master's script contains the cpuset literals (D0) | **NO-GO until landed.** Today master `7f9c6d36` refuses the cpuset compose (`compose-invalid`). Merge 270 also must resolve the `scripts/test-server2-postgres-compose.ps1` conflict |
| 2 | Script and compose staged from the same `$M` (D2 hashes) | Not started |
| 3 | Data-root free ≥ 20 GiB | **GO**: 238.7 GiB (through the overlay; the `DockerRootDir` string is **UNOBSERVED**) |
| 4 | cgroup v1 with the cpuset/cpu/memory/blkio controllers | **GO** (observed). blkio weight effect is **UNOBSERVED** |
| 5 | Online CPUs `0-23`, SMT `0-2` ↔ `12-14` | **GO** (observed) |
| 6 | Port 5432 free on tailnet and LAN | **GO** (refused at 11:48:56Z from the runner). Host `ss -ltn` **UNOBSERVED** |
| 7 | No existing `antiphon-server2-postgres*` container or volume; status of network `antiphon-server2` | **UNOBSERVED** (S2) |
| 8 | Host docker and compose versions; `docker info .Name` equals `hostname` | **UNOBSERVED** (S1) |
| 9 | Host `pwsh` and `tailscale` on PATH; `tailscale ip -4` gives one line | **UNOBSERVED** (S1). Without pwsh, the manual line still works |
| 10 | Registry digest valid | **GO** (HTTP 200, digest equal, from the runner). Host egress **UNOBSERVED** |
| 11 | Image already on the host | **UNOBSERVED** (S3) |
| 12 | Desktop image metadata (`PG_VERSION`, RepoDigests) | **UNOBSERVED** (D1) |
| 13 | Env file present, 0600, S5 preview `plan exit=0 reason=ok` | Operator; not started |
| 14 | Password stored durably (vault item) | Operator decision; no Postgres row in `agent-credentials.md` (G-18) |
| 15 | Desktop TCP path to `100.93.77.126:5432` after deploy | **UNOBSERVED** until deployed |
| 16 | Runner cpuset `3-11,15-23` live | **Not required for the empty deploy.** Until the runner is recreated, Postgres is pinned but not exclusive (the runner still uses `0-23`). Required before the cutover (plan cutover gate) |

## Remaining uncertainties

- Every host-daemon reading (no ssh key in the runner): versions, existing names, image presence, `pwsh`/`tailscale` on the host, the runner and build-slots `HostConfig.CpusetCpus`, ufw.
- The desktop RepoDigest and which 16.14 build `f40a5645…` is.
- Whether the network `antiphon-server2` already exists from another project.
- Whether the block scheduler honours `blkio.weight` on the LV.

## Not done, noted

- Fix idea: a `-Preflight` read-only mode in `deploy-server2-postgres.ps1` that prints S1–S3 and refuses below 20 GiB (G-11).
- Fix idea: add the Postgres password row to `docs/agent-credentials.md` once the operator picks the store (G-18).
