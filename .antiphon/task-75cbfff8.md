# CARD-0957 batch: Final Review (task 75cbfff8)

Subject: Code task 21e85ec1-c03a-4d1e-99e1-a7c1790d23ac, branch feat/card-task-21e85ec1 at 082b6c0337b3e3a304ddae5fd5e3fca0925d27dd (from master afb8ccfb).
Verdict: no regression found on the healthy-host rollout path, and no new security exposure. Ready for a plain land. Two follow-ups are disclosed (CARD-0994, CARD-0995).

## 1. Delta against current master (bf58e534)

- Master's side (51 files: HostCleanup, CARD-0970 tests, Pty) and the branch's 11 files have **no files in common**. Master's side touches no scripts, RemoteScript tests or docker-stack files.
- In a throwaway clone, a trial `merge --no-commit origin/master` was clean. The land's exact `git -c rebase.autoStash=false -c rebase.updateRefs=false rebase origin/master` also completed cleanly: 8/8 commits, tip a8c930ad6 in the clone, 11 files, +664/-60.

## 2. Healthy-host trace (the d81ff99c sequence)

The healthy host here is: cold marker, no temp container, temp a retired offline placeholder with draining=true, redirectTo=server2, retireWhenIdle=true and runnerSessions=null. Old is accepting and eligible.

- **deploy-temp**
  - `-SavedDonor` is not used, so the new refusal does not fire.
  - The block-entry condition is now `available -ne $true` instead of `-not dispatchEligible`. An offline temp still enters.
  - Assert-TempSeedCounters is unchanged.
  - New pre-check: if draining, redirectTo must be `server2`. drain-temp and retire-temp always leave `server2`, so this passes.
  - New old-runner check: dispatchEligible, accepting, not draining, not retired. A serving old runner passes. `draining` is a non-nullable bool in the DTO.
  - New `Assert-TempProjectAbsent`: one read-only `ssh -o BatchMode=yes mc@server2 'docker ps -aq --filter label=...antiphon-runner-temp'`. This is the same ssh target and form as c590-real.ps1, with no sudo. Empty output passes.
  - The retired clear and hold are unchanged. The new `TempRunnerRetiredDuringHold` checks see retiredAt=null after clear+hold, because the hold has retireWhenIdle=false and the sweep ignores it.
  - Host side: the seed's cold-ready branch and the start of deploy-temp-runner now call `c849_no_temp_containers`. Seed never creates a temp container, so both pass on a healthy host.
  - `c849_image` now returns 10/11 instead of calling write_result inside the substitution. The success path still prints and returns 0. The callers' `|| {...}` blocks only run on failure. Errexit inside the substitution is already off in non-POSIX bash, so behaviour inside the function is unchanged.
  - Observe now also requires the literal Docker Mountpoint to equal `<DockerRootDir>/volumes/<name>/_data`, and test -L to return exactly 1. Docker always builds the Mountpoint that way, and base already required resolved == canonical.
  - Final clear and wait are unchanged.
- **drain-old, redeploy-old, drain-temp**: these PowerShell phases are byte-identical. On the host side (deploy-parent, verify-runner-caches), only the error handling in `c849_image` and the observe equality changed; both are traced above. `verify-card0849-caches -Case Both` runs only verify-runner-caches, so the new seed absence check does not reach the step-7 smoke.
- **Destructive paths and sudo**: there are no new docker rm, volume rm or compose down calls, and no new marker writes. The added sudo calls are `sudo -n test -L` inside observe and prune, where it was already used. The sudo-scope contract test passed. The harness only removes its own GUID root, after checks on the literal, the parent and reparse points.
- **Healthy acceptance on real Docker**: the retained fixture has refusal cases only, so I added a scratch happy-path run. Healthy observe and prune both return exit 0 and are accepted, on base and on branch alike (4/4, evidence /tmp/c957-real-bB35ET).

## 3. Verification at 082b6c03 (every driver went through a build slot)

| Run | Result |
|---|---|
| CP-contracts (run-checkpoint.ps1, bin-r957-contracts/, 8 classes) | executed 302, passed 302, failed 0, skipped 0; sourceState=clean, buildSource=verified, source=082b6c03; TRX `.antiphon/r957-checkpoints/CP-contracts-20261002-182412-7dac/run.trx` |
| Rolling harness test-deploy-server2.ps1 (jq present) | 24 groups / 66 invocations / 227 assertions, 0 failures; success root removed |
| jq driver `-Case absent` | 31 assertions, 0 failures; harness 23/62/218; T-20 skipped by name |
| Real-Docker c973-rolling-host-cases.mjs | 19 cases, 0 failures (/tmp/c957-real-2A9h3S). The first attempt omitted jq from PATH: base-cold-existing-temp came back CacheVolumeForeign (`jq: command not found`). That was my setup error and is not evidence; the fixture cleaned up after itself. |
| Scratch happy-path observe/prune (base and branch) | 4/4 accepted |
| Mutation 1: move `Assert-TempProjectAbsent` after the retired clear | RED, `FAIL T-22 existing keeps retirement` (-Only host-absence); restored, `git diff` empty, green rerun |
| Mutation 2: delete `c849_no_temp_containers` at the start of case_deploy_temp_runner | RED in the real fixture: branch-deploy-existing-temp gave ParentStackMissing; restored, `git diff` empty, 19/19 green rerun. The ordinary TUnit lane would **not** catch this (all blocks stub it), hence CARD-0995. |

- Unit lane: skipped. No shared test helpers or registries were edited; only RemoteScriptContractTests.cs, which uses its own file-local helpers.
- jq 1.7.1, SHA256 5942c9b0…c8ff5, copied to a scratch tools directory.
- All bin-r957-* output was removed and the worktree is clean.
- PCs stay pending for method-scoped SourceLanding Mutation.

## FOLLOW-UPS (filed; duplicate searches run first)

- **CARD-0994** (High/Soon): after a normal drain-temp, the temp runner stops itself on idle Retire. Its container is left exited on the host unless retire-temp runs, and gate 9 keeps it and requires human confirmation.
  - The next deploy-temp now refuses `TempContainersRemain`. Base replaced that container on the cold-marker path.
  - retire-temp refuses an offline temp (`runnerSessions=null`, RunnerCounterUnknown). So a retained offline container has no sanctioned path.
  - Live server2-temp right now: retired 2026-10-02T17:41:32Z (idle), offline, runnerSessions=null. I could not check whether the container is still on the host (no ssh from here).
  - Before the next rollout, run `ssh mc@server2 'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp'`. If it prints anything, deploy-temp will stop safely, before any mutation.
  - This is not a regression by the brief's definition of a healthy host, which has no leftover temp container. It matches the intent of CARD-0958 item 2.
- **CARD-0995** (Normal): the host-side absence check in deploy-temp-runner, and the cold-ready seed absence check, have no guard in the ordinary TUnit lane.
