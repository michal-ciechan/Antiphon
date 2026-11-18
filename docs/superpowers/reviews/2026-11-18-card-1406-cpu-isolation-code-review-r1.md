# CARD-1406 CPU isolation code: review round 1

Date: 2026-11-18. Review task `afa5577c`, round 1, Final.
Subject: Code task `9aeb262a-26bf-42a0-8f8a-861d6f7d9cdb` on `origin/feat/card-task-9aeb262a` at `0f13b84917d2ef2317f6025814e3b46a3c6c2645` (Claude Code author).
Range: `f4d7830332f652376a84c42b45bc5c8d0f4fa07f..0f13b84917d2ef2317f6025814e3b46a3c6c2645`. Two commits: S1 red `ec6a6d4c146eea7ee3f9806876fac0e499a26fb0`, then S2 pin `0f13b84917`.

## Verdict

CLEAN. No blockers and no regressions. V-4 stays an operator inspect on the host daemon. No PC is named. Mutation stays waived (operator 2026-11-12) and is not discharged. The inherited HistoryEnabled fixture red is already CARD-1985 and does not hold this land.

## A-1 and the pin

Orchestrator decision A-1 is implemented. Production and drill compose both set `cpuset: "0-2,12-14"`, `cpus: 6.0`, `mem_limit: 8g`, `shm_size: 1gb`, and `blkio_config.weight: 800`. Runner `state-init`, `session-runner`, and `build-slots` each set `cpuset: "3-11,15-23"`. `docker-compose.server2-runner.temp.yml` has no cpuset line. Build-slot `MaxConcurrent` 4 and `MaxCpuCount` 6 are unchanged. The image line stays the env-file variable. The comment and `docs/docker-stack.md` name `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` (16.14). Deploy refusal literals match those pins for both compose files.

Re-read inside runner container `7c9c24dac62d` (cgroup v1, `cpuset.cpus` `0-23`, `cpuset.mems` `0-1`, nproc 24):

- SMT pairs are `(0,12)` package 1 node 0, `(1,13)` package 0 node 1, and `(2,14)` package 1 node 0.
- Postgres `0-2,12-14` is those three whole physical cores. The runner set is the other nine physical cores (18 logical CPUs).
- `cpus: 6.0` is the CFS quota for those six logical CPUs.

The parent plan left NUMA and the host engine UNOBSERVED. This container shows the NUMA split above. The host engine version, and the online mask read outside this container, stay UNOBSERVED. The doc says the engine version is UNOBSERVED and leaves the online-mask check to V-4.

## Inherited fixture red

The c1906 and c346 edits are correct and minimal. After the historical-comparison key is removed, each current service asserts `cpuset === '3-11,15-23'` and then deletes that field before `deepEqual`.

The HistoryEnabled red is real on master `9cb2550e74a5dd18cae82b4788abf02e50521435` and at the tip. `node scripts/fixtures/c1906-compiler-policy-compose.mjs --models-only` and `node scripts/fixtures/c346-workspace-slot-compose.mjs` each fail with one added line, `SessionRunner__HostStats__HistoryEnabled: 'false'`. The failure text is the deepEqual message, so the new cpuset assert passed. CARD-1893 S0 `ea4209a167cbaeab7453207485993f285a0cba9c` is an ancestor of master and of the tip. CARD-1985 (`b9476ea9-d2d6-43af-81ea-5076bfb2f8be`, Backlog) already owns retiring that S0 line and its witness. It must not be fixed on this branch before land.

## Evidence

| Check | Result |
|---|---|
| CP-1 at S1 `ec6a6d4c`, build-slot lease `eb7c32d5-9fb7-478d-ba9c-cacc8a685b4e` | exit 1, `RESULT passed=26 failed=42`. The missing new literals fail. Not re-run at the tip: the row's expect is that S1 red. |
| CP-2, checkpoint run `20261118-104753-f35a` | exit 0, `RESULT passed=357 failed=0`, 47.4 s, `sourceState=clean`, `buildSource=notApplicable` |
| CP-3, same run, build `bin-c1406-cpu/` | executed 163, passed 163, failed 0, skipped 0. DockerStackContractTests 117, DockerStackDocumentationTests 19, DindRunnerContractTests 25, C1893S0RunnerHoldTests 2. `buildSource=verified` |
| CP-4 guard batch, same run, build reused | executed 81, passed 81, failed 0, skipped 0. Policy 42, registry merge 5, NativeLinuxPolicyWitness 3, classification guard 1, lane category 1, mechanical contract 11, TaskPlatformGuidance 5, layering 11, tripwire 2. Floor in the review extract was 78. |
| `validate --evidence report.json --expected-source-sha 0f13b84917… --rows CP-2,CP-3,CP-4` | `CHECKPOINT SOURCE VALID source=0f13b84917d2ef2317f6025814e3b46a3c6c2645 rows=3` |
| `git merge-tree --write-tree origin/master HEAD` | exit 0, tree `4b27728c662c0100bcdd22fcf56d07a2b362585c`. `origin/master` is `9cb2550e74a5dd18cae82b4788abf02e50521435`. |
| `scripts/check-evidence-diff.ps1` on the range | commits=2, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range` | kinds=`timing-labels` only. The migrations row did not match, so the downgrade selector count is 0. That timing-labels roster was not run; it is outside the closed checkpoint list. |
| `scripts/check-os-witnesses.ps1` | exit 0. Compose and the deploy script are portable. The test script and the two fixtures are test-only. Two pre-existing `$env:OS -eq 'Windows_NT'` file-mode guards are non-behavior. `witnesses` empty. `restartImpact.required` false. |
| Load before the checkpoint run | 1-minute load 6.59, under 16. The run was not stopped. |

`unlisted: none`. The tool deleted `bin-c1406-cpu/` after the run. No session input wait changed. V-4 and the positive controls stay pending and are outside the checkpoint table.

reviewedRange: `f4d7830332f652376a84c42b45bc5c8d0f4fa07f..0f13b84917d2ef2317f6025814e3b46a3c6c2645`
