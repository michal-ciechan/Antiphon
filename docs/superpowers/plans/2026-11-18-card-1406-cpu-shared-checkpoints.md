# CARD-1406 Postgres on server2: one reserved core plus shared burst, checkpoints

Code task `92424f0e`, stacked on the review-clean CPU isolation tip `0f13b84917d2ef2317f6025814e3b46a3c6c2645` (`feat/card-task-9aeb262a`, checkpoints `docs/superpowers/plans/2026-11-18-card-1406-cpu-checkpoints.md`). No deploy, no restart, no container start, no SQL.

Operator decision (2026-11-18, "we should only pin 1 cpu and rest to use shared"): Postgres gets one reserved physical core and may burst onto the shared pool, instead of the three-core pin `0-2,12-14`. One physical core is CPU 0 and its SMT sibling 12 (host topology: cpu0 `thread_siblings_list` = `0,12`, observed in `docs/investigations/2026-11-18-empty-cluster-deploy-prep.md` on `feat/card-task-61c8b8b6`; both are NUMA node0).

## Final layout (Code task `27ed273a`, supersedes the design call below)

Operator 2026-11-18, after this plan was written: "we should only pin 1 cpu and rest to use shared" and "Don't reserve cores for session runner". Design (2) with shares is final. The task branch merged `origin/master` `ae7bf7368` (which already carried S1/S2 as `b612dd5f7`/`1cffbb8ad`), resolved every conflict to master's side, and then edited master's single `cpu-isolation` case.

- Postgres (production and drill): `cpuset: "0,12"` (one physical core, not exclusive, not reserved), `cpu_shares: 2048`, `mem_limit: 8g`, `shm_size: 1gb`, blkio weight 800, and no `cpus` (the cpuset caps it at 2 logical CPUs). The pin is one commented line per file plus `$script:Server2PostgresCpuset` in the deploy script; two physical cores is `0-1,12-13`.
- Runner services: no `cpuset` and no `cpu_shares`; `docker-compose.server2-runner.yml` is byte-identical to its pre-CARD-1406 content, so no runner or broker recreate is owed.
- V-1F. `-Case cpu-isolation` fails on master's three-core tree (observed: passed=51 failed=44).
- V-2F. After the fix the whole script passes (observed: passed=407 failed=0).
- V-3F. The case fails if Postgres loses or changes cpuset `0,12`, `cpu_shares: 2048` (or sets either twice), `mem_limit: 8g`, `shm_size: 1gb` or blkio weight 800, or sets any `cpus`; if any runner service or the temp overlay carries a `cpuset` or `cpu_shares`; or if the doc lacks the final sentence and V-4 expectations.
- V-5F. Both refusals return `compose-invalid` without the cpuset, with `0-23`, `0-2,12-14`, `0,12,13` or a second cpuset line, without `cpu_shares: 2048`, with 1024 or 20480, with any `cpus` line, and for the memory, shm and blkio mutants; `-WhatIf` refuses copies without the cpuset and without `cpu_shares: 2048` and calls no docker.
- R-1F. `node scripts/fixtures/c1906-compiler-policy-compose.mjs` and `c346-workspace-slot-compose.mjs` pass with no runner cpuset strip (the CARD-1893 HistoryEnabled allowance from master stays).
- R-2F. `DockerStackContractTests*` and `DockerStackDocumentationTests*` once (operator: short tests only; CP-3's `DindRunnerContractTests`/`C1893S0RunnerHoldTests` not rerun because the runner compose returned to its pre-card bytes).
- V-4F (operator, host daemon): Postgres `cpuset=[0,12] shares=2048 8589934592 1073741824 800`, both runner containers `cpuset=[] shares=0`; see `docs/docker-stack.md`.

## Design call

Design (1) is implemented. The reservation is made from the runner side: `state-init`, `session-runner` and `build-slots` take cpuset `1-11,13-23`, so no runner thread, nested dockerd container or nested build can be scheduled on CPU 0 or 12. Postgres sets no cpuset (it may run on all 24 logical CPUs), `cpu_shares: 2048` (cgroup v1 `cpu.shares`, `HostConfig.CpuShares`; the runner services keep the default 1024), and keeps `cpus: 6.0` (CFS quota, at most six CPUs of time per period), `mem_limit: 8g`, `shm_size: 1gb` and blkio weight 800. Every key is a cgroup v1 `HostConfig` field that Compose v2 already exposes; nothing needs a parent cgroup or a cgroup v2 key.

Recommendation, design (1) over design (2) (`cpuset: "0,12"` only):

1. (1) keeps the desktop profile's bursts: about 1 core average fits the reserved pair, and the 2.2-core bursts spill onto the shared pool under shares 2048 instead of queuing on two hyperthreads.
2. (2) is a hard ceiling of one physical core, about 1.2-1.3 cores of throughput with SMT, below the measured 2.2-core burst: bursts would queue.
3. (1)'s reservation is soft for Postgres: the core is kept free of runner work, but Postgres reaches it only through the scheduler's idle balancing, not by pinning; non-runner host processes (Windmill, `staging-postgres`, sshd, kernel threads) can still use CPUs 0 and 12, as under (2) and the old pin.
4. On the shared CPUs shares are a weight, not a reservation; cgroup v1 spreads a group's shares across the CPUs it is runnable on, so a narrow Postgres group at 2048 outweighs the wide runner group (1024 over up to 22 CPUs) on any CPU where both are runnable (kernel semantics, UNOBSERVED on this host).
5. Recommend (1); (2) is the fallback if a loaded V-4 sample shows Postgres latency suffering from scheduler migration off the reserved core.

## Capacity

The runner keeps 22 logical CPUs (11 physical cores with their siblings), up from 18 under the three-core pin. The build-slots broker numbers are unchanged (CARD-1893 owns them): 4 leases x `-maxcpucount:6` = 24 MSBuild nodes on 22 CPUs, about 1.09 nodes per CPU before the runner process and dockerd, against 1.33 under `3-11,15-23`. When Postgres bursts to its full 6.0 quota, at most 4 CPUs of that time can come from the shared pool (2 are the reserved pair), leaving the runner at least about 18 CPUs of time, the same floor as the old pin. All build-time and latency effects are UNOBSERVED until the runner recreate and the V-4 samples.

## Verification design

- V-1. `scripts/test-server2-postgres-compose.ps1 -Case cpu-isolation` fails on the S1 tree (`0f13b849` compose, deploy and docs with the new witness): Postgres still carries cpuset `0-2,12-14` and no `cpu_shares: 2048`, the runner services carry `3-11,15-23`, the doc has no reserved-core sentence, and the deploy refusals accept a compose without `cpu_shares: 2048`.
- V-2. After S2 the case passes, and the whole script passes with the updated `compose-contract`, `drill-compose` and refusal literals.
- V-3. The case fails if Postgres (production or drill) loses `cpu_shares: 2048` (or sets it twice), `cpus: 6.0`, `mem_limit: 8g`, `shm_size: 1gb` or blkio weight 800, or carries any cpuset; if `state-init`, `session-runner` or `build-slots` lacks cpuset `1-11,13-23`; if any runner service's cpuset contains CPU 0 or 12 or covers anything outside `1-11,13-23`; if the runner file sets `cpu_shares` or `mem_limit`; or if the temp overlay carries a cpuset. The reserved set is one physical core and, with the runner set, covers CPUs 0-23 once.
- V-5. `Get-Server2PostgresComposeRefusal` and `Get-Server2PostgresDrillComposeRefusal` return `compose-invalid` for a compose without `cpu_shares: 2048`, with 1024 or 20480 in its place, with a `0,12`, `0-2,12-14` or runner cpuset added, without `cpus: 6.0`, with `cpus: 3.0`, without `mem_limit: 8g`, with `mem_limit: 4g`, without `shm_size: 1gb`, or without blkio weight 800. `-WhatIf` refuses a copied compose without `cpu_shares: 2048` (exit 2, `compose-invalid`) for both files and calls no docker.
- R-1. The touched scripts and compose files stay ASCII and every existing case still passes.
- R-2. The C# contract classes that read the edited runner compose and `docs/docker-stack.md` stay green.
- V-4 (operator, on the host daemon, not a checkpoint). `docker inspect` `HostConfig.CpusetCpus` is `1-11,13-23` for `antiphon-runner-session-runner-1` and `antiphon-runner-build-slots-1`; Postgres reads `CpusetCpus` empty, `CpuShares` 2048, `NanoCpus` 6000000000; then the parent plan's quiet and loaded p95 samples on the empty cluster.

`scripts/fixtures/c1906-compiler-policy-compose.mjs` and `scripts/fixtures/c346-workspace-slot-compose.mjs` assert and strip the runner cpuset, now `1-11,13-23`. Both stay red on this stack for the inherited CARD-1893 S0 `SessionRunner__HostStats__HistoryEnabled` line until `feat/card-task-937e5617` (`f5c66c404`) lands; that allowance sits a few lines above the cpuset hunk and is not duplicated here. Evidence is the direct `node <fixture>` probe (unlisted, reason: inherited-red proof without a second Antiphon.Tests build), which must show HistoryEnabled as the only difference.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | shared-red | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1 -Case cpu-isolation` | V-1 | exit 1, and the output contains `FAIL` | n/a | 15 | true | n/a |
| CP-2 | S2 | n/a | shared-green | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` | V-2, V-3, V-5, R-1 | exit 0, `RESULT passed=` with `failed=0` | n/a | 6 | true | n/a |
| CP-3 | S2 | tests/Antiphon.Tests -> bin-c1406-shared/ | compose-contracts | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(DindRunnerContractTests*)\|(C1893S0RunnerHoldTests*)/*` | R-2 | all listed, 0 failed | 163 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 15 + 6 + 15 = 36 minutes. CP-1 carries the first-row cold-build budget; the script is a file read and finishes in well under a minute. CP-3 is the one `Antiphon.Tests` build (cold on a loaded server2) plus about 163 file-reading Unit tests. No repeat proof after green.
