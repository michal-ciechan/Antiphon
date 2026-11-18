# CARD-1406 follow-up: CPU and memory isolation for Postgres on server2

Date: 2026-11-18. Plan task `0540d663`. Plan only: no compose edit, no deploy, no container start, no SQL.
Master read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (`HEAD` and `origin/master` at the read).
Authority: operator 2026-11-18 decision 3, require CPU isolation (reserved or pinned) before cutover. The other five decisions of investigation `a84df3ad` are already approved and are not reopened here.
Mutation checks are waived (operator 2026-11-12). Merge verification is short rows only (operator 2026-11-18).

This plan does not add or change a session that waits for input. No release deadline applies.

Live `GET /api/runner-defaults` and `GET /api/session-runners` were not called. The brief forbids extra load on the desktop while Windows run `f2de5b30` is in progress. The plan names no `-Runner` and no `-Platform`.

## Outcome

Pin the host Postgres to CPUs `0-2` and pin the phone-home runner services to `3-23`, on the cgroup v1 hierarchy this host is actually running. A compose contract test goes red first if the pin is missing or the sets overlap. The empty-cluster deploy (`scripts/deploy-server2-postgres.ps1`) carries the Postgres side. The runner file carries the other side so a later recreate keeps it. No deploy-temp phase is part of the Postgres compose.

## Decisions

Stated defaults. Code implements them. A "no" is an override for the caller, not a missing fact.

| Id | Question | Recommendation | Stated default |
|---|---|---|---|
| D-1 | Isolate with a cpuset on both sides, instead of `cpu_shares`, swarm reservations, or cgroup v2 `cpu.weight`? | Yes | Yes |
| D-2 | Give Postgres CPUs `0-2` (3 cores) and the runner services `3-23`? | Yes. Desktop Postgres averages about 1 core and bursts to about 2.2. Three cores cover the burst. Two cores leave no headroom on the burst. | `0-2` / `3-23` |
| D-3 | Replace `cpus: 4.0` with `cpus: 3.0` so the CFS quota matches the pin? | Yes. A quota of 4 on a 3-CPU set is a no-op and a lying contract. | `cpus: 3.0` |
| D-4 | Raise `mem_limit` from `4g` to `8g` and set `shm_size: 1gb`? | Yes. The 2026-11-17 desktop resident set was 2.83 GiB against a 3,018 MB database, so 4g is tight once cache is counted. Linux Docker's default `/dev/shm` is 64 MB; parallel query uses it. | `8g` and `1gb` |
| D-5 | Set Postgres `blkio_config.weight` to 800 and leave the runner at the engine default? | Yes. Weight is a bias, not a reservation. 800 against the default 500 prefers Postgres without capping builds. | 800 on Postgres only |
| D-6 | Change `MaxCpuCount` from 6 to 5 in this slice? | No for this slice. 4 slots times 6 nodes is 24, which does not fit in the 21 runner cores. Leaving 6 keeps today's oversubscribe and keeps the build-slots environment block free of a broker-number edit. | Leave `"6"` |
| D-7 | Ship the Postgres pin with `deploy-server2-postgres.ps1`, and require the runner pin to be in the runner compose before cutover? | Yes. deploy-temp phases are not used for this Postgres compose. | Yes |
| D-8 | Keep the same PostgreSQL 16.14 Debian image, digest-pinned, and file the major upgrade as a later card? | Yes | Yes |

Rejected:

- `cpu_shares` / cgroup v1 `cpu.shares`. Relative weight only. At load 20–50 a heavy share still waits on the run queue. It does not reserve a core.
- `deploy.resources.reservations.cpus`. Swarm scheduling hint. Non-swarm `docker compose up` does not reserve CPUs from it. Do not use the `deploy:` key.
- cgroup v2 `cpu.weight`. This host is not presenting v2. There is no Compose key by that name. On a v2 host, Docker maps `cpu_shares` onto `cpu.weight`, which is the same non-reservation as D-1's rejection.
- `isolcpus` / `irqaffinity`. A reboot of the whole host. Kernel threads can still be kept off `0-2` later. Not required to meet decision 3.
- Leaving `cpus: 4.0` as the only control. That is a CFS ceiling. The runner container's live cpuset is `0-23`, so the ceiling does not stop the runner from running on the same cores.

## What was read on this host

Read from inside the phone-home runner container (hostname `7c9c24dac62d`), 2026-11-18. No `docker` against the host daemon, no volume change, no image pull.

| Fact | Reading |
|---|---|
| Kernel | `Linux 4.15.0-213-generic #224-Ubuntu SMP Mon Jun 19 13:30:12 UTC 2023 x86_64` |
| Cgroup hierarchy | v1. `/sys/fs/cgroup` lists `cpu`, `cpuacct`, `cpuset`, `memory`, `blkio`, `devices`, `freezer`, `pids`. `/sys/fs/cgroup/cgroup.controllers` is absent. |
| Who owns that hierarchy | `/proc/1/cgroup` is `cpu,cpuacct:/docker/7c9c24dac62d715ffd1b02e5edf4c6a69d0f808b4291158e04108079c266a6a9` (and the same id under the other v1 controllers). That is the host engine's cgroup for this container. |
| CPUs | `nproc` = 24. `/sys/devices/system/cpu/online` = `0-23`. This container's `cpuset.cpus` = `0-23`. |
| Nested dockerd | `docker info` inside the container: server 27.5.1, cgroup driver `cgroupfs`, cgroup version 1. This is the nested daemon, not the host daemon. |
| Host engine version | **UNOBSERVED.** The host socket is not mounted. |
| Block schedulers | `sda` and `sdb`: `noop deadline [cfq]`. `dm-0` and `dm-1`: `none` (device-mapper). cfq is what makes a v1 `blkio.weight` mean anything on the physical disks. |

CARD-1643's card body is **UNOBSERVED** (not in this checkout; the board API was not called). The only in-repo note is `docs/investigations/2026-11-17-backlog-triage.md:91` and `:120`: "CP-12 is cgroup v1, and the card says server2 is not that host." That sentence is about which machine may execute CARD-1643 CP-12. It is not a measurement of this kernel. The live hierarchy above is cgroup v1, which matches the repo's older record of kernel 4.15 / cgroup v1 (`docker/session-runner-grok/dind-entrypoint.sh:145-159`, `docs/superpowers/plans/2026-10-29-card-1614-linux-custody-repairs-plan.md` container observation). Compose keys that work here are the v1 `HostConfig` fields: `CpusetCpus`, `NanoCpus`, `Memory`, `ShmSize`, `CpuShares`, `BlkioWeight`.

## Ground truth

| # | What the card / brief assumes | What the tree does at `e229fb4d` | Source |
|---|---|---|---|
| G-1 | Server2 Postgres needs exclusive cores, not only a quota | `mem_limit: 4g` and `cpus: 4.0`. No `cpuset`, no `cpu_shares`, no `shm_size`, no blkio weight. Same pair on the drill file. | `docker-compose.server2-postgres.yml:21-22`, `docker-compose.server2-postgres-drill.yml:19-20` |
| G-2 | The runner and dind already have a cpuset | No `cpus`, `cpuset`, or `mem_limit` in the runner compose or the temp overlay. Live cpuset of this container is `0-23`. dind is inside `session-runner` (`privileged: true`), not its own service. | `docker-compose.server2-runner.yml:35-47`; temp file has no CPU key; live `cpuset.cpus` |
| G-3 | `cpus: 4.0` is a reservation | It is a CFS quota (`NanoCpus`). The contract test and the deploy refusal both require the literal `cpus: 4.0` and `mem_limit: 4g`. | `scripts/test-server2-postgres-compose.ps1:310-311`, `scripts/deploy-server2-postgres.ps1:180-181` and `:214-215` |
| G-4 | Build slots 4 × 6 fit beside a 3-core pin | `MaxConcurrent` `"4"` and `MaxCpuCount` `"6"` on `session-runner` and `build-slots`. 4 × 6 = 24 node-slots. After the pin, 21 cores remain. The product does not fit without oversubscribe. It already equals the whole 24-core host today. | `docker-compose.server2-runner.yml:72-74` and `:182-184`; `docs/docker-stack.md:1916` |
| G-5 | Desktop and server2 Postgres settings match | Desktop service is `image: postgres:16`, the same three `pg_stat_statements` flags, no memory cap, no CPU cap, no `shm_size`. Server2 compose repeats those three flags and adds the 4g / 4.0 ceiling. Neither file sets `shared_buffers` or `work_mem`. | `docker-compose.dev.yml:17-25`; server2 compose `:12-19` |
| G-6 | The image pin can be the tag `postgres:16` | The deploy script accepts only `postgres:16@sha256:` plus 64 hex digits, or `postgres@sha256:` plus 64 hex digits. Any other reference, including `postgres:16.14-trixie`, is `image-floating`. `alpine` is `image-alpine`. | `scripts/deploy-server2-postgres.ps1:71-77` |
| G-7 | Floating `postgres:16` is still 16.14 | **No.** docker-library's current tag list binds `16`, `16-trixie`, and `16.16-trixie` together (library manifest on master, and the 2026-11-15 Hub release of 16.16). A docs snapshot from when 16.14 was current bound `16.14`, `16`, and `16.14-trixie` together. Pulling `postgres:16` today pulls 16.16. | Hub / docker-library docs read 2026-11-18. Desktop image id was not inspected. |
| G-8 | Registry digest of the desktop image is the given image id | The brief's `sha256:f40a5645a21f34c966c46a283e6b7fd5f4a7d4fc1768a5842f34b38423e90c8f` is a local image id. A RepoDigest is a different hash. **UNOBSERVED** until the desktop `docker image inspect` (not run here; that container is `antiphon-postgres`). | brief; `Test-Server2PostgresImage` |
| G-9 | Isolation survives a runner recreate only if the runner file has it | A rollout recreates the runner from `docker-compose.server2-runner.yml`. The temp overlay merges and does not replace unspecified keys. A cpuset set only at `docker update` time is lost on recreate. | `scripts/deploy-server2.ps1` phase set `:15`; temp file `:1-14` |
| G-10 | Postgres deploy needs deploy-temp phases | It does not. The Postgres project is `antiphon-server2-postgres`, applied by `scripts/deploy-server2-postgres.ps1` (`-WhatIf` default). deploy-temp is the runner rollout. | `docs/docker-stack.md:2058-2066` |
| G-11 | Host memory and disk were remeasured today | **UNOBSERVED** today. Last read in `docs/investigations/2026-11-17-postgres-to-server2-move-status.md:143-150`: 24 cores, 125 GiB RAM, 91 GiB available, LV 104G free, Postgres port 5432 refused, `cpus: 4.0` called out as a ceiling. | that note |
| G-12 | CARD-1888 / CARD-1969 remove the need for a pin | They cut reads. The status note's estimate after those cuts is still about 0.8 core if nothing else changes (`:93`). The pin is for build load 20–50, not for the idle average. | status note `:148`, `:209` |

## Image and version

The move uses the same major, the same Debian variant, and the same patch as the desktop. It does not upgrade.

| Image the brief names | What it is | Use on server2 |
|---|---|---|
| Desktop `postgres:16`, local id `sha256:f40a5645a21f34c966c46a283e6b7fd5f4a7d4fc1768a5842f34b38423e90c8f`, server 16.14, package `16.14-1.pgdg13+1` | Debian, not Alpine. `pgdg13` is the PGDG apt suite for Debian 13 (trixie). | The target. Pin by digest in the mode-0600 env file. |
| Tag `postgres:16.14-trixie` | The docker-library tag that named this Debian patch when 16.14 was current (`16.14` and `16.14-trixie` were the same image in that docs snapshot). | The tag to resolve. Do not write the tag into the env file. Resolve it to a digest, then confirm `PG_VERSION` is `16.14-1.pgdg13+1` before trusting it. That confirmation is **UNOBSERVED** here. |
| `postgres:16` and `postgres:16-trixie` as of 2026-11-15 | Now aliases of **16.16**. | Refused in practice: wrong patch, and a floating tag is `image-floating` anyway. |
| `postgres:16.16` (CARD-1974/1975 review r2) | Same major, two patches newer. That review timed the cutover fingerprint on a throwaway 16.16 container (`docs/superpowers/reviews/2026-11-17-card-1974-1975-review-r2.md:13`). | Not the move image. A throwaway drill that uses it is an upgrade. |
| `postgres:16-alpine` (Testcontainers, CARD-1888 / CARD-1920 reviews) | Alpine, musl, not `pgdg13`. CARD-1920 notes glibc and musl getopt differ. The deploy script rejects `alpine` anywhere in the reference. | Not the move image. Not the drill image. |

Extensions and settings, from the compose files only (production database was not queried):

- Both desktop and server2 start `postgres` with `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`.
- The extension row itself is created by the server's EF migrations, not by Compose (`docs/bootstrap.md:217-219`).
- No other GUC is set in either compose file. This slice does not add one.

Operator step before `-Execute`, on the desktop, without printing the connection string: `docker image inspect` the running `antiphon-postgres` and read `RepoDigests` and the image config `PG_VERSION`. The env file value is `postgres:16@sha256:` plus that 64-hex RepoDigest, or `postgres@sha256:` plus the same. If `RepoDigests` is empty, resolve `postgres:16.14-trixie` for `linux/amd64` and inspect that manifest's config the same way. Do not `docker pull postgres:16`.

## Design

### CPU

```yaml
# docker-compose.server2-postgres.yml and docker-compose.server2-postgres-drill.yml
cpuset: "0-2"
cpus: 3.0
```

```yaml
# docker-compose.server2-runner.yml, on state-init, session-runner, and build-slots
cpuset: "3-23"
```

Quote the cpuset. Leave `cpus` unquoted so it stays the same YAML shape as today's `cpus: 4.0`.

Why both sides: a cpuset on Postgres alone limits Postgres to those CPUs. Every other container with an empty cpuset, including today's runner (`0-23`), may still be scheduled there. Exclusive means the runner services are pinned off those CPUs too.

Why these services: `session-runner` is the compiler and the nested dockerd. `state-init` runs as root at deploy. `build-slots` is a separate container (profile `broker`) and would otherwise keep `0-23`. The temp overlay (`docker-compose.server2-runner.temp.yml`) must not set a cpuset. Compose merges it, so the base pin is inherited. A temp cpuset that intersects `0-2` is a failed contract.

Online mask gate, on the host, before `-Execute`: `/sys/devices/system/cpu/online` must be `0-23`. If it is not, stop. This container read `0-23`. The host mask was not re-read outside the container. NUMA layout is **UNOBSERVED**. Do not invent a per-node pin.

Residual, accepted for this slice: Windmill, `staging-postgres` (`cpus: 1.0` only, `docker-compose.server2-staging-server.yml:102-103`), sshd, and kernel threads can still run on `0-2`. Staging is a different compose project. A later edit can pin it to `3-23`. It is not this slice.

### Memory and shm

```yaml
mem_limit: 8g
shm_size: 1gb
```

On both the production file and the drill file. `mem_limit` is `memory.limit_in_bytes` on this v1 host. `shm_size` is the `/dev/shm` mount, independent of the cgroup. Desktop Compose sets neither (`docker-compose.dev.yml:16-41`); the desktop shm size is **UNOBSERVED**. 1gb is larger than the Linux engine default of 64 MB so a parallel plan on a large transcript read does not fail with a shared-memory resize error that the desktop might not hit.

Do not set a runner memory cap. The runner comment and the production contract say there is no cgroup memory cap (`docker-compose.server2-runner.yml:68-70`, `docs/docker-stack.md:1417`). This card does not reverse that.

### IO weight

```yaml
blkio_config:
  weight: 800
```

Postgres services only. The runner stays at the engine default (500) by omitting the key. cfq is active on `sda` and `sdb` in this container, so a v1 weight is not a guaranteed no-op. `dm-0` / `dm-1` report `none`; the LV's requests still land on the physical disk. Weight does not reserve bandwidth. It is not the isolation.

### Runner capacity

After the pin the runner has 21 CPUs (`3-23`). The broker stays at 4 concurrent leases and `-maxcpucount:6` (D-6). Worst case is 24 MSBuild nodes on 21 CPUs, plus the runner process and dockerd. That is about the same oversubscribe the host already runs (24 nodes on 24 CPUs with other services). Postgres no longer shares that fight. Seats stay 10 (`PhoneHome__Capacity: "10"`, `:64`). Seven tasks still queue on four slots. Do not edit `MaxConcurrent`, `MaxCpuCount`, or `MinAvailableMemoryMb` in this slice.

### How a recreate keeps the pin

1. Postgres project: `scripts/deploy-server2-postgres.ps1 -Execute` runs `docker compose up` for `antiphon-server2-postgres`. The cpuset is in that file, so a later `up` keeps it. No deploy-temp, drain-old, or retire-temp phase. `-WhatIf` stays the default and still must not call docker.
2. Runner project: the cpuset is in `docker-compose.server2-runner.yml`. The next compose up of `antiphon-runner` recreates `session-runner` when that service's config hash changes, and recreates `build-slots` when its hash changes (`ensure_build_slots_broker` does not pass `--no-recreate`). Until that recreate, the live runner cpuset stays `0-23` and the Postgres pin is not exclusive.
3. Do that runner recreate with the image already running (`--no-build`, same `SOURCE_SHA12`). Do not combine it with an image roll. CARD-1893's hold is about rolling an image that contains collector `9cf3e491705247bc03814e6afed77a6534805dfd` without `HistoryPath`. A cpuset-only recreate of the current image is not that roll.
4. Do not recreate the runner while server2 tasks are in flight. The desktop Windows run is a different host. Server2 sessions die if `session-runner` is recreated.
5. Cutover gate (operator, on the host daemon): `docker inspect` `HostConfig.CpusetCpus` is `0-2` for `antiphon-server2-postgres-postgres-1` and `3-23` for `antiphon-runner-session-runner-1`. Anything else stops the window.

### Version rule, and the later upgrade

This slice does not change the image reference grammar and does not pull an image. Drill and any other throwaway cluster for this move use the same digest as production.

Follow-up card, not this change, after the moved cluster has been stable: upgrade off 16.14 to the then-current PostgreSQL major. Own plan, own window, own rollback. Recommended mechanism is dump-restore onto a new volume of the new major, then a connection-string switch. Rollback is start the old volume and point the string back. `pg_upgrade` needs both binaries in one container and a data directory that is not in use; it is the alternative the follow-up plan has to measure, not the default. Do not fold that upgrade into the move. This plan does not file the board card. The caller files it.

## Collisions

| Card | Overlap | What this plan does about it |
|---|---|---|
| CARD-1893 | S1 edits `docker-compose.server2-runner.yml` session-runner environment (`HistoryPath`) and requires `build-slots` (`:169-190`) to stay byte-identical so the broker is not recreated (`docs/superpowers/plans/2026-11-16-card-1893-activation-plan.md` D-9). S0's witness only matches the `HistoryEnabled: "false"` line (`C1893S0RunnerHoldTests`). | The cpuset key sits beside `environment`, not inside it, so a HistoryPath hunk and a cpuset hunk merge as text. Adding `cpuset` on `build-slots` does change that service's hash and violates D-9's byte-identical goal on purpose. Land this runner edit after CARD-1893 S1, or in the same resulting file with both hunks kept. One broker recreate, not two. Do not edit `HistoryPath` here. |
| CARD-1984 | Spawn-lane plan. It regenerates `tests/derived-impact-index.json` when a new test class appears (`docs/superpowers/plans/2026-11-17-card-1984-spawn-lane-plan.md` G-13). | No new C# class. The witness stays in `scripts/test-server2-postgres-compose.ps1`. No index edit. |
| CARD-1986 | No plan, review, or card body in this checkout. | **UNOBSERVED.** Before Code edits the runner compose, the caller confirms CARD-1986's file list. If it also edits `docker-compose.server2-runner.yml`, same rule as CARD-1893: keep both hunks, do not revert the other card's lines. |

Impacted-classes table (CARD-1782): **none**. No migration, public DTO, route, operation label, remote script function, or statement budget.

## Slices

### S1 — red witness

Edit only `scripts/test-server2-postgres-compose.ps1`. ASCII. Add case `cpu-isolation` to `$cases` (`:269-295`) and to `Invoke-Case`.

The case reads the production compose, the drill compose, the runner compose, and the temp overlay, and fails unless all of the following hold:

- Production and drill each contain the literals `cpuset: "0-2"`, `cpus: 3.0`, `mem_limit: 8g`, `shm_size: 1gb`, and `weight: 800` under `blkio_config`.
- Production and drill do not contain `cpus: 4.0` or `mem_limit: 4g`.
- `state-init`, `session-runner`, and `build-slots` in `docker-compose.server2-runner.yml` each contain `cpuset: "3-23"`.
- The temp overlay contains no `cpuset:` line. (A line that appears must be `cpuset: "3-23"`, and the case still fails if any runner cpuset token intersects `0`, `1`, or `2`.)
- The two sets are disjoint: no file contains both `cpuset: "0-2"` and `cpuset: "3-23"` on the same service. Practical check: Postgres files contain `0-2` and not `3-23`; the runner file contains `3-23` and not `0-2`.
- `docs/docker-stack.md` contains the sentence `Postgres cpuset is 0-2 and the runner cpuset is 3-23`.

Do not change the existing `compose-contract` literals in S1. Those still expect `mem_limit: 4g` and `cpus: 4.0`, and they still pass. `cpu-isolation` fails. Commit this red test before S2.

### S2 — compose, refusal, doc

- `docker-compose.server2-postgres.yml` and `docker-compose.server2-postgres-drill.yml`: the D-3 / D-4 / D-5 block. Same command flags as today (`:12-19`). No new GUC. Image line stays the env-file digest.
- `docker-compose.server2-runner.yml`: `cpuset: "3-23"` on `state-init`, `session-runner`, and `build-slots`. Update the comment at `:68-70` so it says cores `3-23`, with `0-2` reserved for the host Postgres cpuset. Do not change the three `SessionRunner__BuildSlots__*` values.
- `scripts/deploy-server2-postgres.ps1` `Get-Server2PostgresComposeRefusal` and `Get-Server2PostgresDrillComposeRefusal`: replace the `mem_limit: 4g` / `cpus: 4.0` literals with the new ones, and require `cpuset: "0-2"`, `shm_size: 1gb`, and `weight: 800`. A missing cpuset stays `compose-invalid`.
- `scripts/test-server2-postgres-compose.ps1` `compose-contract` and `drill-compose`: the same literal swap, so the full script goes green together with S2.
- `docs/docker-stack.md`: in the CARD-1406 section (`:2058`) add the cpuset sentence the witness requires, plus `mem_limit: 8g`, `shm_size: 1gb`, and blkio weight 800. In the build-budget paragraph (`:1916`) say the runner cpuset is `3-23` and that 4 × 6 still does not fit in those 21 cores. Do not change the budget numbers.

No change to `scripts/cutover-postgres-server2.ps1`, the desktop compose, `antiphon_pgdata`, or the env-file schema.

## Verification design

- V-1: `cpu-isolation` fails on the pre-S2 tree (missing cpuset, old quota).
- V-2: `cpu-isolation` passes after S2, and the full `scripts/test-server2-postgres-compose.ps1` passes, including the updated refusal literals.
- V-3: the runner temp overlay does not override the pin, and the Postgres set does not intersect the runner set.
- V-4 (operator, on the host, not a checkpoint): `docker inspect` `HostConfig.CpusetCpus` as in the cutover gate above. Then one quiet sample and one sample under a concurrent build on the **empty** cluster (no production data, no desktop dump). Pass when the loaded p95 is at most twice the quiet p95 of the same script. Provisional absolute ceiling, because no server2 cluster exists yet and no p95 has been measured: `SELECT 1` p95 at most 10 ms, and `pgbench -S -c 4 -T 30` p95 at most 50 ms. Replace the ceiling with the measured 2× line once the quiet sample exists. A selection over 30 minutes is not part of this gate. Do not run this against `antiphon-postgres` or during the desktop Windows run.
- R-1: the touched script stays ASCII and the existing refusal cases still pass.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | isolation-red | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1 -Case cpu-isolation` | V-1 | exit 1, and the output contains `FAIL` | n/a | 15 | true |
| CP-2 | S2 | n/a | isolation-green | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` | V-2, V-3, R-1 | exit 0, `RESULT passed=` with `failed=0` | n/a | 6 | true |

### Cost

Ordinary floor is 15 + 6 = 21 minutes. Both rows are one PowerShell process, no `dotnet` build, no container, no slot beyond what `run-checkpoint.ps1` takes for itself. CP-1 is 15 minutes because the brief requires that on the first row; the script itself is a file read and should finish in well under a minute. No repeat proof after green.

## Performance budget

- Selection: the two rows above. No `Antiphon.Tests` project, no Testcontainer, no migration, no host startup. Downstream coverage is the compose text and `deploy-server2-postgres.ps1`'s refusal functions, which the script dot-sources.
- Duration source: **no measured p95**. This task did not run the script (a red run belongs to Code, after the red commit). The script starts no container and does not connect to a database. Provisional row budgets are the table. The checkpoint tool's deadline multiplier is 3× `EstimatedMinutes` (45 minutes for CP-1, 18 for CP-2). That multiplier is the tool default, not a claim that the script takes 15 minutes.
- Phases: file read and string asserts only. No container start, no data load, no snapshot. Build and slot wait are separate from the script body and are whatever the checkpoint broker charges. Slot wait is not part of the 15-minute estimate.
- Setup: none shared. Each invocation is a new process. No assembly fixture.
- Recording: the checkpoint tool's `CHECKPOINT` line and the script's `RESULT passed=` / `FAIL` lines. No external call.
- Timeouts: the script has no timeout of its own. Do not add one. Nothing in this plan is expected to exceed 30 minutes. V-4's `pgbench -T 30` is a 30-second client run, operator-only, and is not a checkpoint row.
- TestDesign is folded into this plan. The next stage is review of the plan, not a separate test-design pass.

## Files

| File | Slice |
|---|---|
| `scripts/test-server2-postgres-compose.ps1` | S1, S2 |
| `docker-compose.server2-postgres.yml` | S2 |
| `docker-compose.server2-postgres-drill.yml` | S2 |
| `docker-compose.server2-runner.yml` | S2 |
| `scripts/deploy-server2-postgres.ps1` | S2 |
| `docs/docker-stack.md` | S2 |
| `docs/superpowers/plans/2026-11-18-postgres-server2-cpu-isolation-plan.md` | this plan |

Not touched: `docker-compose.dev.yml`, `docker-compose.server2-runner.temp.yml` (must stay without a cpuset line), `docker-compose.server2-staging-server.yml`, `docker-compose.server2-server.yml`, cutover script, snapshot script, `antiphon_pgdata`.
