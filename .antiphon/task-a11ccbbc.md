# Review CARD-1742 round 1 (task a11ccbbc): staging server compose

Subject: `origin/feat/card-task-2a819c07` tip `f93d98e00dc8e67cac95da5bc1ce642a2c325351` (Grok Code `2a819c07-9c6b-4b86-b51a-aea9a54b2dce`; red `62659289deb5ed8d608d5d2a108884739d1fee15`). Range reviewed `b6052173f4b8483b886bd63750ef730eb22467dc..f93d98e00dc8e67cac95da5bc1ce642a2c325351` (2 commits, 7 files, 969+/1-). Master `c2965dc7b1a377ce57aabfede628d5179bcdfaad`.

## Verdict: CLEAN. No blockers. Nothing deployed, started or touched on any host.

## Isolation (brief item 1)

- Own project `name: antiphon-server2-staging`; own network `antiphon-server2-staging` (CARD-1406 uses `antiphon-server2`); own volumes `antiphon-server2-staging_pgdata`, `_server-state`, `_work`. No `external: true`, no `docker.sock`, no `privileged`.
- No reference to `antiphon_pgdata`, `dind-data`, `antiphon-runner`, Windmill, the root compose or the runner compose (read; asserted by `Staging_file_is_its_own_compose_project` and `Staging_postgres_is_private_scratch_not_production`).
- Only host port is `127.0.0.1:17420:8080`; Postgres publishes none. No other repo file uses 17420. Desktop ports 17202-17205/18090/19092 untouched.
- Connection string is `Host=staging-postgres` only; tracked `appsettings.json` `localhost:17280` is overridden; `ChannelBridge__Enabled` false and no `BootstrapServers`; `GitHub:Enabled` false by default; `CardFileSync.AutoCommit` defaults false. No desktop host, DB or broker is referenced anywhere in the compose.
- Egress is configuration-isolated, not network-isolated (bridge can route to LAN); consistent with the card.

## Config matches the card (item 2)

Hangfire worker on; ZombieCensus off (job not registered, `Program.cs:1172`); Schedules, check interpreter, diagnose, output distiller on with `/state/...` Linux paths; `WorktreeResidue__Enabled` true + `Execute` false (report-only; sweep root `/work/worktrees` on the project's own `_work` volume); `WorktreeLegacyRelease` off (job removed, `Program.cs:1192`); `RunnerWorktreeRetention` off; ChannelBridge and Digest off; `AgentTui__KeyRingPath: /state/keyring` on `_server-state`; no `PhoneHome__ServerOrigin`; `SessionRunner__Enabled`/`PhoneHomeRunner__Enabled` false. Remaining Hangfire jobs: branch deletion (default off), watchdog (`ExpectationWatchdog:Enabled` false), KeepSix (scratch DB only). Runner compose, root compose and `deploy-server2.ps1` unchanged (asserted by `Staging_phone_home_origin_vhost_apphost_and_runner_stay`).

## Deploy script (item 3)

- Default `-WhatIf`: prints plan + acceptance URLs; never calls docker (probe M1). `-WhatIf -Execute` exit 3.
- `-Execute` refusals are real: `/.dockerenv` -> `not-server2-host` before any compose call (probe M2); daemon name vs hostname (`Resolve-StagingLane`, probe M10); exact project name `-cne` (probe M3); compose text tripwire for `antiphon_pgdata`/`dind-data`/`antiphon-server2-postgres`/windmill/`0.0.0.0`/5433/17280/17202/`:5432:`/phone-home origin plus 36 required literals (probes M4, M5, M7); env file must exist, be outside the repo, mode 0600, pinned `postgres:16@sha256:<64hex>` or `postgres@sha256:`, non-empty password not equal to the committed dev password (probes M8, M9).
- No teardown/removal command exists in the script, so nothing can match a production volume. Password never printed (probe M6; stdout/stderr/argv asserted). ASCII-only, LF, no CRLF; `.gitattributes` row added.

## Tests (item 4)

- Red commit `62659289d` adds only the test class and the PS harness; compose and script are absent there, so `DockerStackDocuments.Read` throws for all 9 methods (Code: 9 failed/0 passed). Real red by construction; not rebuilt at the red commit (deterministic, budget).
- All nine C# witnesses assert exact file content on the real files; none is a self-compare or constant.
- Docs agree with script/compose: project, port, exit codes 0/2/3, flags, acceptance URLs, `deploy-server2.ps1` untouched (asserted by `Staging_script_plans_without_docker_and_refuses_a_foreign_daemon`).

## Migrations (item 5): none in the diff.

## Checkpoint run (one leased run, checkpoint tool)

Run `20261107-175030-604e`, commit f93d98e00dc8e67cac95da5bc1ce642a2c325351, state=clean, buildSource=verified, cold build `bin-r1742-a/` 312 s, outputs deleted after green. Evidence `.antiphon/checkpoints/20261107-175030-604e/report.md`.

```
CHECKPOINT CP-1 commit=f93d98e00dc8e67cac95da5bc1ce642a2c325351 build=ok filter=/*/Antiphon.Tests.Infrastructure/(Server2StagingServerContractTests*)|(DockerStackContractTests*)|(DockerStackDocumentationTests*)/* executed=144 passed=144 failed=0 skipped=0 trx=/work/worktrees/task-a11ccbbc/.antiphon/checkpoints/20261107-175030-604e/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f93d98e00dc8e67cac95da5bc1ce642a2c325351 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=f93d98e00dc8e67cac95da5bc1ce642a2c325351 build=n/a filter=pwsh -NoProfile -File scripts/test-server2-staging-server-compose.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f93d98e00dc8e67cac95da5bc1ce642a2c325351 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=f93d98e00dc8e67cac95da5bc1ce642a2c325351 build=reused filter=/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(TestLaneCategoryGuardTests*)|(TestClassificationGuardTests*)|(TestClassificationPolicyTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=76 passed=76 failed=0 skipped=0 trx=/work/worktrees/task-a11ccbbc/.antiphon/checkpoints/20261107-175030-604e/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=f93d98e00dc8e67cac95da5bc1ce642a2c325351 sourceState=clean buildSource=verified
verdict: GREEN exit=0   unlisted: none
```

TRX per class: CP-1 Server2StagingServerContractTests 9, DockerStackContractTests 117, DockerStackDocumentationTests 18 (all 9 staging method names present). CP-2 console `SERVER2-STAGING-TEST passed=89 failed=0` (13 cases). CP-3 TestClassificationPolicyTests 39, MechanicalPcContractTests 11, C1729LayeringGuardTests 9, TaskPlatformGuidanceTests 5, SlowTestRegistryMergeTests 5, NativeLinuxPolicyWitnessTests 3, SlowTestTripwireTests 2, TestClassificationGuardTests 1, TestLaneCategoryGuardTests 1 (matches the Code report's 75 plus the lane guard).

Other checks: `scripts/check-evidence-diff.ps1` commits=2 entries=0 violations=0; `git merge-tree --write-tree origin/master HEAD` clean, tree `56d8e0930392972d7f83832ac45ebf25ad09d63d`; worktree clean after the run.

## Mutation probes (scratch git-archive copy, no build, no worktree edits; control 89/0)

| Probe | Mutation | Case | Result |
|---|---|---|---|
| M1 | WhatIf calls `Invoke-StagingUp` | whatif | 1 failed: "WhatIf does not call docker" |
| M2 | `/.dockerenv` check removed | host-command | 2 failed: not-server2-host, does not compose up |
| M3 | project `-cne` guard removed | wrong-project | 2 failed (exit 0, reason ok) |
| M4 | `antiphon_pgdata` refusal removed | volume | 2 failed |
| M5 | compose `0.0.0.0:17420:8080` | whatif | 6 failed (bind-any) |
| M6 | password echoed | dev-password | 1 failed: hides the dev password |
| M7 | compose `Hangfire__ServerEnabled: "false"` | whatif | 6 failed (compose-invalid) |
| M8 | 0600 check removed | env-mode | 2 failed |
| M9 | dev-password check removed | dev-password | 2 failed |
| M10 | lane refusal removed | off-host | 2 failed |

Scratch copy restored to the committed bytes (sha match). PCs: none declared by Code (zero PC-n); none discharged here.

## Advisories (not blocking)

- A-1 The script prints no rollback line; docker-stack.md says rollback is removing the compose project. A later slice can print a guarded `docker compose -p antiphon-server2-staging down` (no volume rm) in the WhatIf plan, CARD-1406 style.
- A-2 `-Execute` proves "host daemon, not in a container" (daemon name equals hostname), not a server2 hostname pin; the doc says "on the server2 host daemon". Pinning the expected daemon name would close it.
- A-3 `staging-state-init` has no `networks:` so compose also creates `antiphon-server2-staging_default`; harmless, but one network is cleaner.
- A-4 The server image ships no `/usr/local/bin/{grok,claude,codex,pwsh}`; the `Agents__Definitions` exe values are inert until a runner exists (Code and the doc say so).
- A-5 `#requires -Version 5.1` on a script that needs pwsh 7.3+ (`File.GetUnixFileMode`, `ArgumentList`); Windows returns `env-mode` first so it is harmless, but the header overstates portability.
- A-6 The compose tripwire is 36 duplicated literals; any compose edit must be mirrored or WhatIf refuses `compose-invalid` (by design; worth a doc line).

## Still owed (unchanged from Code)

Operator `-Execute` on the server2 host; CARD-1406 drill; CARD-1743 durable Hangfire; CARD-1222 census; CARD-1746 bridge; CARD-1745 restart gate; CARD-1744 cutover and key ring.
