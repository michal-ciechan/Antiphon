# CARD-1745 Review round 1 (task 2e77eeb2) - Linux server SHA restart gate

Subject: origin/feat/card-task-47538c88 tip 8278a7205eccc8308ae9f06d01ed89000df016ee (Code task 47538c88, Grok; red 53fad1f6f809e6c207b943f2e75e8ef3fae9dc8c). Base edb2c20156afa4da503b1b29fb83f1f953987bd5. Master 99f3d328cfe1aaaaf211278f24e43b9d11b61438. Review branch feat/card-task-2e77eeb2 carries the same tip plus this document. Read-only: no product file changed, nothing deployed, nothing restarted, no real docker call.

Verdict: NOT CLEAN. Two defects (D-1 production-down blocker, D-2 Windows-red Unit class). Advisories A-1..A-6 separate. Next stage: code.

## Verification run (leased, checkpoint tool run 20261107-224615-5146, source clean, buildSource=verified)

| CP | Filter | Result |
|---|---|---|
| CP-1 | `/*/*/LinuxServerRestartGateTests/*` (build tests/Antiphon.Tests -> bin-c1745rv/, 412.9 s) | 13 executed, 13 passed, 0 failed, 0 skipped, 59 s |
| CP-2 | guards: ProcessSpawnLimitTests, TestClassificationPolicyTests, WindowsUnitPlatformAdmissionTests, NativeLinuxPolicyWitnessMutationTests, MechanicalPcContractTests, TaskPlatformGuidanceTests, C1729LayeringGuardTests, DockerStackContractTests, DockerStackDocumentationTests | 237 passed, 0 failed, 1 skipped (C1210_Windows_linux_only_outcomes_are_exact: Windows placement only), 134 s |
| CP-3 | `pwsh -NoProfile -File scripts/test-restart-linux-server.ps1 -Case W5` | exit 0 (6 rows) |
| CP-4 | `pwsh -NoProfile -File scripts/test-restart-linux-server.ps1 -Case W6` | exit 0 (6 rows) |

`validate --rows CP-1,CP-3,CP-4 --expected-source-sha 8278a72...`: CHECKPOINT SOURCE VALID rows=3. Whole-run validate refuses `row_failed` only because of the one by-construction skip in CP-2. Unlisted runs: none by the tool. Outside the tool, under individual build-slot leases (non-dotnet, warning only): harness red at base shape and six harness mutants (below). The Unit lane was not rerun (caller instruction); the Code task's 4h29m lane (6790 executed, 7 failed) stands as the lane evidence.

Other checks: `check-evidence-diff.ps1` base..HEAD commits=2 entries=0 violations=0. `git merge-tree --write-tree origin/master HEAD` = 896f08daf6a508e9efa781747fe6f6510305e827, no conflicts (not merged). Subject branch and review branch both at the tip on origin.

## Brief items

1. Inertness: confirmed. `$script:GateDocker` is assigned and `Invoke-GateDocker` is reached only inside the `-Execute` block and the exit-5 Execute tail; WhatIf never calls docker (W2/W3/W4/W10/W13 trace empty; mutant M4 that lets WhatIf enter the docker block turns W2 red). Refusals are real: missing config exit 3 before any probe; SHA format before any HTTP or docker (W7); ports 17200-17209 (W8, mutant M3 red); loopback-only http with explicit port; container regex `^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$` with exact-name bans plus substring bans runner/postgres/gateway/phone (W9, mutant M5 red). Docker receives the exact name as one argument (W5 trace `inspect --format {{.Image}} antiphon-staging`, `stop antiphon-staging`, `start antiphon-staging`); no glob, no compose, no volume, no rm. The desktop AppHost is a Windows process, out of docker's reach; the server2 runner is `antiphon-runner-session-runner-1` (banned by substring); antiphon_pgdata is never addressed.
2. Gate correctness: the comparison requires a full 40- or 64-hex value on both sides and lowercases both (mutant M2 red). The real endpoint returns `AntiphonVersionDto.Version = AntiphonVersion.Sha`, the 40-hex SourceLink SHA, or "unknown"/short when git was absent at build, which the gate correctly reads as `malformed` -> exit 5. `/health` is a status-only 200 check. WhatIf is one probe; Execute polls every 200 ms until `-TimeoutSec` (default 20 s) on health/unavailable and returns at once on ok/mismatch/malformed. Lock: a lock younger than 15 min refuses (exit 3); older is ignored; the lock is written at Execute start, removed on success or inspect failure, retained on stop/start failure (exit 1) and health timeout (exit 1), rewritten with the reason on exit 5. See D-1 and A-1/A-2/A-5 for the consequences.
3. AppHost six-path diff: `git diff --stat edb2c20156..HEAD` over scripts/restart-apphost.ps1, scripts/apphost-common.ps1, scripts/start-apphost.ps1, scripts/autostart-apphost.ps1, dev-aspire.ps1, Antiphon.AppHost/Program.cs is empty. sha256 of scripts/restart-apphost.ps1 at HEAD and at master 99f3d328 is f966a638fafe6e059d5e5dc5ae4b4321ac9c0e59f8a9b5caedeb99599022e678 (claim verified).
4. Witnesses: the harness without the gate beside it (the 53fad1f6f shape) prints `FAIL gate-present` and exits 1, so every case test was red at the red commit. Fresh mutants on a scratch copy of the gate, each under its own lease: M1 drop the post-mismatch `docker stop` -> W6 red (W6-stopped-after-start); M2 drop `ToLowerInvariant` on the observed SHA -> W2 red; M3 drop the 17200-17209 refusal -> W8 red (3 rows); M4 WhatIf enters the docker block -> W2 red (W2-no-docker); M5 drop the runner bans -> W9 red (4 rows, trace shows `stop session-runner` against the fake); M6 WhatIf mismatch exits 0 -> W3 red. Unmutated copy: W2/W6/W8/W9 green. W1-W13 are plausible; all PCs stay pending for method-scoped SourceLanding Mutation.
5. ProcessSpawnLimitTests.cs: the two added lines register the new class in the roster that `Process_spawning_classes_carry_the_limiter` closes against `[ParallelLimiter<ProcessSpawnLimit>]`; the class spawns pwsh per case, so the entry is required, not drift.

Inherited Unit reds: the five RemoteScriptContract rows cannot be caused by this diff. They drive `scripts/c590-remote.sh` deploy fragments (`runner_workspace_slots_enabled` is defined at c590-remote.sh:1095) and `scripts/deploy-server2.ps1`; neither file nor RemoteScriptContractTests.cs changed in base..HEAD, and base equals master for them. The only docs/docker-stack.md reader in that file is C1316_Recycle_audit_cap_clears_the_standing_volume, not one of the five. AttachAsync_copies_StartedAt_and_Pid_and_polls_Exited (SessionRunner) and C1620_EveryCallSiteOperationLabelIsKept (operation labels) have no input in the five changed paths.

## Defects

### D-1 (blocker) -Execute stops a healthy, correct-or-wrong server before learning anything it could not learn first, and leaves production down

Where: scripts/restart-linux-server.ps1:233-256 and 284-292.
Failure: `docker stop` then `docker start` restarts the same container with the same image, so the post-restart identity is always the pre-restart identity. With a wrong `-ExpectedServerSha` (an operator typing the master SHA while the host runs the rc tip, the live hazard AGENTS.md names) or a compose deploy that did not swap the image, the script inspects, stops, starts, observes the mismatch it could have observed before touching docker, stops again, and exits 5 with the only server left stopped. W6 is exactly this path: the stub was healthy and serving before the stop. The AppHost precedent refuses before killing when the expected SHA disagrees with reality (restart-apphost.ps1:201-202) and its exit 5 leaves the child running (line 53, 422); this gate turns the same situation into an outage.
Why: the brief's blocker rule (stop the wrong thing or leave production down). The card allowed "stopped or rolled back", but there is no rollback: `previousImage` is the image of the container that was just stopped.
Fix: in Execute, run `Invoke-GateProbe` once before `docker inspect`. If it returns `mismatch` or `malformed` (health 200, wrong identity) write the lock with that reason, print the exit-5 refusal and `Nothing was restarted.`, exit 5 without any docker call. Proceed to inspect/stop/start only on `ok`, `health` or `unavailable`. Add a harness case (healthy stub, wrong SHA, -Execute -> exit 5, empty docker trace) and keep W6 for the post-restart path. Document the recovery: after exit 5 `docker start <container>` resumes the same unverified build; a different build needs the compose deploy.

### D-2 (defect) LinuxServerRestartGateTests is Windows-red by construction and carries no Linux-only marker, roster entry or exclusion row

Where: tests/Antiphon.Tests/Scripts/LinuxServerRestartGateTests.cs (whole class), scripts/test-restart-linux-server.ps1:47-62.
Failure: the harness writes a `#!/bin/sh` fake docker and runs `chmod a+x`; on Windows `chmod` is a CommandNotFoundException caught as `FAIL harness` (exit 1), and even with a chmod on PATH the extensionless sh file cannot be executed by pwsh, so W5/W6 fail. The class is `[Category("Unit")]` with no `[NativeLinuxOnly]`, no `NativeLinuxGate.Require`, no WindowsUnitSkipRoster entry and no `platform: windows` row in tests/test-execution-policy.json (95 such rows exist, including RemoteScriptContractTests and RollingVolumeRecycleScriptTests). The Windows Unit lane (nightly Windmill checkout, rc qualification) gains 11 reds.
Why: docs/testing-and-build.md line 69 (CARD-1613): rc Linux-only Windows exclusions are pinned `[NativeLinuxOnly]` markers with a policy row; the policy guards `New_exclusion_without_marker_fails` and `Marker_pin_and_origin_are_enforced` enforce the pair. Also the OS-branch-witness obligation: a Linux-only witness must be declared, not left to fail elsewhere.
Fix: add the `[NativeLinuxOnly]` marker (Antiphon.TestSupport) with a CARD-1745 reason to the 11 case methods or the class, add the matching `profiles.rc.exclusions` row (suite antiphon, class LinuxServerRestartGateTests, platform windows) and reseal `policyHash` per the CARD-1613 recipe; rerun the guard batch (CP-2 above) and the gate class.

## Advisories

- A-1 Lock field `previousImage` and the report phrase "previous image id" suggest a rollback target; it is the stopped container's own image id. Rename to `image=` and state in docs/docker-stack.md what an operator does after exit 5 (see D-1 fix).
- A-2 `-TimeoutSec` default 20 s against the AppHost's 150 s. A slow cold start exits 1 with the lock retained under `reason=execute` and the container left running; the 15-minute lock then refuses even a WhatIf re-probe (exit 3). Raise the default, record `reason=health-timeout`, and let WhatIf ignore a lock it did not write or document `-LockMaxAgeMinutes 1` as the re-probe path.
- A-3 `#requires -Version 5.1`, but `Invoke-WebRequest -SkipHttpErrorCheck` exists only in PowerShell 7+; under 5.1 every probe fails closed as "health was not 200". Declare `#requires -Version 7.0`.
- A-4 Daemon pinning: no refusal when `DOCKER_HOST`/`DOCKER_CONTEXT` is set, and `docker inspect` has no `--type container`. The name bans still protect runner/postgres/gateway, but refusing an env-redirected daemon and pinning the type closes the remaining "wrong daemon" path the brief asks about.
- A-5 Lock acquisition is check-then-write (`Test-GateLockFresh` then `Set-Content`); two concurrent runs can both pass. Acquire with `[IO.File]::Open(..., CreateNew)`.
- A-6 docs/docker-stack.md names exits 0, 3 and 5 but not exit 1 (health failure, stop/start failure) nor the lock-retention consequences; `$uri.IsDefaultPort` refuses an explicit `:80` with "must name an explicit port". A `restart: always` policy would restart a gate-stopped container at the next daemon restart; `unless-stopped` keeps the "left stopped" promise.

## Review evidence

subjectTaskId: 47538c88-3cc5-41ab-89f9-338fb87a9d33
reviewedSourceSha: 8278a7205eccc8308ae9f06d01ed89000df016ee
reviewedSourceClean: true (CP-1/CP-3/CP-4 SHA-validated, buildSource=verified; CP-2 green with one Windows-placement skip)
ordinaryScopeCompleted: Interim (gate class, guard batch and harness rerun; the Unit lane was not rerun by caller instruction)
reviewRound: 1
reviewedRange: edb2c20156afa4da503b1b29fb83f1f953987bd5..8278a7205eccc8308ae9f06d01ed89000df016ee
