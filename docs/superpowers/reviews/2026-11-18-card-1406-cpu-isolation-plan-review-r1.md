# CARD-1406 CPU isolation plan: review round 1

Date: 2026-11-18. Review task `1c0cc933`, round 1, Final.
Subject: `origin/feat/card-task-0540d663` at `d8f23eb90c98c6bd2db6f8f471077d5d00937ee8`. The author was Grok and the reviewer is Claude Opus.
Range: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4..d8f23eb90c98c6bd2db6f8f471077d5d00937ee8`. One commit adds `docs/superpowers/plans/2026-11-18-postgres-server2-cpu-isolation-plan.md` and changes nothing else.

## Verdict

CLEAN. No blockers and no regressions. The advisories below do not hold the land. A-1 is a question for the operator, and it should be answered before Code is dispatched.

## Checked against the brief

| Brief item | Plan | Result |
|---|---|---|
| Postgres cpuset `0-2`. Runner cpuset `3-23` on state-init, session-runner and build-slots | D-2, Design CPU, S2 | Present |
| `cpus 3.0`, `mem 8g`, `shm 1gb`, `blkio 800` | D-3, D-4, D-5 | Present, on the production file and the drill file |
| Each key works on cgroup v1 | Section "What was read on this host" | Checked again in this container. See the next section. |
| `MaxCpuCount` stays 6 | D-6 | Stays 6. The plan forbids any budget edit. |
| Red-first witness: `-Case cpu-isolation` | S1, CP-1 | S1 commits the red case before S2. The literals cannot pass on today's tree. |
| The runner compose carries the cpuset, so a recreate keeps it | G-9, the recreate steps, S2 | Present. The temp overlay must not carry a cpuset. |
| Image is 16.14, pinned by digest, never floating `postgres:16` (now 16.16) | G-6, G-7, G-8, "Image and version" | Present. The RepoDigest is read on the desktop, and `postgres:16` is never pulled. |
| Collision with CARD-1893 | Collisions table | Handled. See A-4. |

## Host facts re-read for this review

All facts were read inside the server2 runner container `7c9c24dac62d`, on kernel 4.15:

- The controllers are cgroup v1: `cpuset`, `cpu,cpuacct`, `memory`, `blkio` and others. `cpuset.cpus` is `0-23`. `cpu.cfs_period_us` is 100000 and `cpu.cfs_quota_us` is -1. Both `blkio.weight` and `blkio.weight_device` exist, so CFQ group scheduling is built in. `sda` and `sdb` use `[cfq]`. The Compose keys `cpuset`, `cpus`, `mem_limit`, `shm_size` and `blkio_config.weight` therefore map to v1 files that exist. The host engine version was not observed (the plan says the same).
- The nested dockerd creates its containers' cgroups as children of the runner container's own cgroup (`/sys/fs/cgroup/cpuset/docker/<nested>`; two nested containers read `0-23` today). A runner pinned to `3-23` therefore also bounds the Testcontainers and nested builds. The plan's design depends on this but does not state it.
- `cpuset.mems` is `0-1`. There are two NUMA nodes: node0 has the even CPUs and node1 has the odd CPUs.
- SMT pairs: cpu0 is paired with 12, cpu1 with 13, and cpu2 with 14.

## Evidence

- Checkpoint run `20261118-061600-23c5` used a one-row extract under `.antiphon/`. It ran `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` at `d8f23eb90`. The result was GREEN: `RESULT passed=279 failed=0`, exit 0, `sourceState=clean`, `buildSource=notApplicable`, `unlisted: none`. `validate --expected-source-sha d8f23eb90…` printed `CHECKPOINT SOURCE VALID rows=1`. This run is the baseline that S1 and S2 change.
- The plan's own `### Checkpoints` table was checked with `import --plan`: 2 rows imported, with no refusal. CP-1 and CP-2 run only after the S1 and S2 code, so they cannot run at this SHA. They stay with Code.
- `git merge-tree --write-tree origin/master HEAD` gave tree `87b910bd8c6e93aee17b5a1173d8c10caa1faf6e` with no conflicts. Master was `e229fb4d3`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef origin/master -HeadRef HEAD`: commits=1, entries=0, violations=0.
- The guard batch was not run. The subject changes no code and no tests, which is the brief's condition for skipping it.
- There is no migration, so the downgrade selector count is 0. No session waits for input.
- V-4 is an operator task on the host daemon. It and the PCs stay pending. The operator waived mutation on 2026-11-12.

## Advisories

### A-1. CPUs 0-2 are hyperthreads whose siblings 12-14 are in the runner set, and they span both NUMA nodes

- **Where:** D-2, Design CPU, and the S1 witness literals.
- **Failure:** under the build load the pin targets (load 20-50), the runner saturates logical CPUs 12-14. Those CPUs share physical cores with Postgres's 0-2. Postgres keeps its run queue, but it gets about half to two thirds of each core's throughput. That is roughly 1.8-2 effective cores, below the 2.2-core burst that D-2 sizes against. CPUs 0 and 2 are on node0 and CPU 1 is on node1, with `mems` `0-1`.
- **Why:** the plan says "Three cores cover the burst" and "NUMA layout UNOBSERVED". The topology could be read from `/sys/devices/system/cpu/cpu*/topology` and `/sys/devices/system/node`.
- **Fix:** this is an operator choice, because the brief mandated `0-2`/`3-23`. Option one is Postgres `0-2,12-14` (three whole physical cores) with runner `3-11,15-23` (18 logical CPUs; D-6's oversubscribe grows). Option two keeps the current values and lets V-4 (loaded p95 at most twice quiet p95) decide. Either way, make the amendment before Code, because S1 pins the literals.

### A-2. No regression row for the C# contract classes that read the edited files

- **Where:** the `### Checkpoints` table, and "Impacted-classes table: none".
- **Failure:** S2 edits `docker-compose.server2-runner.yml` and `docs/docker-stack.md`. Several classes read those files: `DockerStackContractTests` (including the 2-space block parser `Server2_file_defines_runner_state_init_and_profiled_broker`), `DockerStackDocumentationTests`, `DindRunnerContractTests`, `C1893S0RunnerHoldTests`, and `HostStatsProbeParseTests` (in the SessionRunner tests). None of them is in the closed list. A mis-indented `cpuset:` would turn the block parser red, and no row would catch it.
- **Why:** reading the assertions shows no conflict with the planned edits. The planned key is at 4 spaces, beside `environment`, and the doc edits are additive. The risk is low, but the coverage is still missing.
- **Fix:** Code adds one row with `(DockerStackContractTests*)|(DockerStackDocumentationTests*)|(DindRunnerContractTests*)|(C1893S0RunnerHoldTests*)` on `Antiphon.Tests`. Alternatively, Code Review reruns these classes.

### A-3. The new refusal literals have no negative witness

- **Where:** S2 changes `Get-Server2PostgresComposeRefusal` and `Get-Server2PostgresDrillComposeRefusal`.
- **Failure:** a Code change that swaps `4g`/`4.0` but never adds `cpuset: "0-2"`, `shm_size: 1gb` or `weight: 800` to the required list still passes CP-2. The only refusal test today is the case with no network block (`:578`). The plan's sentence "A missing cpuset stays compose-invalid" has no test behind it.
- **Fix:** add a `-WhatIf` case that removes the cpuset line from a copied compose and expects `compose-invalid`, for both files.

### A-4. The build-slots cpuset change triggers CARD-1893 H-2, and the cutover gate does not check the broker

- **Where:** "How a recreate keeps the pin" steps 2-5, and the Collisions row for CARD-1893.
- **Failure:** before CARD-1893 S2, the first rolling rollout that carries this file reaches `ensure_build_slots_broker` (`scripts/c590-remote.sh:1020`, no `--no-recreate`). That call recreates the broker with no idle gate, even while tasks are in flight. The impact is small: on a 404, `scripts/lib/build-slot.ps1:116` stops the renewer and the build keeps running, so there is a short window of over-admission. After CARD-1893 S2, `--no-recreate` means only `upgrade-broker` applies the broker's cpuset. The cutover gate checks only Postgres and session-runner.
- **Fix:** in the cutover gate, either check `antiphon-runner-build-slots-1` for `3-23` or state that the broker is exempt (it is light). Add a note that the first rollout carrying the change recreates the broker.

### A-5. Small wording and citation issues

- "Practical check: … the runner file contains 3-23 and not 0-2". S2's required runner comment ("with 0-2 reserved for the host Postgres cpuset") contains `0-2`, so a check on the bare substring would go red. Implement the normative rule instead: per-service `cpuset:` literals and token intersection.
- The plan cites build-slots at `:169-190`, copied from the CARD-1893 plan. The service starts at `:172`.
- On v1, buffered writeback is charged to the root blkio cgroup. `weight: 800` therefore mostly biases reads and direct IO. The plan already says the weight is not isolation.

reviewedRange: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4..d8f23eb90c98c6bd2db6f8f471077d5d00937ee8`
