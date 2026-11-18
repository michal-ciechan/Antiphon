# CARD-1406 CPU and memory isolation for Postgres on server2: checkpoints

Code task `9aeb262a`. Parent plan: `docs/superpowers/plans/2026-11-18-postgres-server2-cpu-isolation-plan.md` (Review-clean, `docs/superpowers/reviews/2026-11-18-card-1406-cpu-isolation-plan-review-r1.md`). No deploy, no restart, no container start, no SQL.

Orchestrator decision on review advisory A-1 (2026-11-18): Postgres takes three whole physical cores, cpuset `0-2,12-14` (CPUs 0-2 and their SMT siblings 12-14), and the runner services take `3-11,15-23`. The parent plan's `cpus: 3.0` follows the cpuset, so it becomes `cpus: 6.0` for the six logical CPUs. Every other parent decision (D-1, D-4 to D-8) is unchanged.

## Verification design

- V-1. `scripts/test-server2-postgres-compose.ps1 -Case cpu-isolation` fails on the S1 tree: no Postgres cpuset, the old `cpus: 4.0` / `mem_limit: 4g`, no runner cpuset, no doc sentence, and the deploy refusals still accept a compose without the cpuset.
- V-2. After S2 the case passes, and the whole script passes with the updated `compose-contract`, `drill-compose` and refusal literals.
- V-3. The case fails if Postgres loses its cpuset (production or drill), if a runner service (`state-init`, `session-runner`, `build-slots`, or any later service in the runner file) lacks the runner cpuset or carries one that intersects `0-2,12-14`, or if the temp overlay carries any cpuset. The two declared sets are disjoint, cover CPUs 0-23 once, and the Postgres set holds each SMT sibling.
- V-5 (review A-3). `Get-Server2PostgresComposeRefusal` and `Get-Server2PostgresDrillComposeRefusal` return `compose-invalid` for a compose without the cpuset, with `0-23` or the runner set in its place, without `cpus: 6.0`, with `cpus: 3.0`, without `mem_limit: 8g`, with `mem_limit: 4g`, without `shm_size: 1gb`, or without blkio weight 800. `-WhatIf` refuses a copied compose without the cpuset (exit 2, `compose-invalid`) for both files and calls no docker.
- R-1. The touched scripts and compose files stay ASCII and every existing case still passes.
- R-2 (review A-2). The C# contract classes that read the edited runner compose and `docs/docker-stack.md` stay green.
- V-4 (operator, on the host daemon, not a checkpoint). `docker inspect` `HostConfig.CpusetCpus` is `0-2,12-14` for `antiphon-server2-postgres-postgres-1` and `3-11,15-23` for `antiphon-runner-session-runner-1` and `antiphon-runner-build-slots-1`, then the parent plan's quiet and loaded p95 samples on the empty cluster.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | isolation-red | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1 -Case cpu-isolation` | V-1 | exit 1, and the output contains `FAIL` | n/a | 15 | true | n/a |
| CP-2 | S2 | n/a | isolation-green | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` | V-2, V-3, V-5, R-1 | exit 0, `RESULT passed=` with `failed=0` | n/a | 6 | true | n/a |
| CP-3 | S2 | tests/Antiphon.Tests -> bin-c1406-cpu/ | compose-contracts | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(DindRunnerContractTests*)\|(C1893S0RunnerHoldTests*)/*` | R-2 | all listed, 0 failed | 163 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 15 + 6 + 15 = 36 minutes. CP-1 carries the brief's first-row cold-build budget; the script is a file read and finishes in well under a minute. CP-3 is the one `Antiphon.Tests` build (cold on a loaded server2) plus 163 file-reading Unit tests. No repeat proof after green.
