# Postgres move to server2, blocking gaps part A: checkpoints

Code task `801355c0`, from the runbook `docs/investigations/2026-11-18-postgres-move-runbook.md` (commit `2e8907ee0` on `feat/card-task-3b15e001`), gaps G-1, G-2 and G-4. Script-only. No Docker daemon, no ssh, no production database, no desktop run. `scripts/cutover-postgres-server2.ps1` and its two test scripts belong to in-flight Code `acfe82da` and are not touched here. G-3 (a drill-record producer) is a new drill mode whose JSON reader lives in the cutover script, so it is deferred to part B.

Version rule (operator 2026-11-18): the move keeps PostgreSQL 16.14 on Debian pgdg13. CARD-1988 owns the later upgrade.

## Verification design

- V-1. G-2 patch pin. `Test-Server2PostgresImage` accepts `postgres:16.N@sha256:<64 lowercase hex>` and `postgres:16.N-trixie@sha256:<...>`. It still refuses (`image-floating`) `postgres:16.14`, `postgres:16.14-trixie`, `postgres:16`, `postgres:latest`, `postgres:latest@sha256`, `16.14-bookworm` (pgdg12), another major, `16.14.1`, `16.`, `16.014`, a short digest, an uppercase digest and `library/postgres`. A `-WhatIf` env file with `postgres:16.14@sha256:` is `ok` and runs no docker. A digest-less `postgres:16.14` is refused. Case `image-patch-pin`. Red at `e229fb4d3`: 4 failed.
- V-2. G-1 opt-out, through a child pwsh and a fake docker that records argv. When `ANTIPHON_SKIP_DEV_POSTGRES` is removed from the child environment (absence asserted before the call), empty or `0`, the bring-up is `compose -f <file> up -d` of every service, unchanged. When it is `1`, the bring-up is `compose -f <file> up -d --no-deps redpanda`, docker never sees `postgres`, and the log line names the variable and the skipped desktop Postgres. Cases `skip-value`, `services`, `arguments`, `invoke-absent`, `invoke-empty` and `invoke-optout`.
- V-3. G-1 wiring. `dev-aspire.ps1` and `scripts/autostart-apphost.ps1` dot-source `scripts/lib/dev-postgres-compose.ps1`, bring compose up only through `Invoke-DevComposeUp`, no longer run `docker compose ... up -d` directly, and skip their `antiphon-postgres` waits when opted out. The new files are ASCII. Cases `wiring` and `ascii`. Red at `eff4e7eed`: 15 failed (V-2 and V-3).
- V-4. G-4 target resolution. `Resolve-SnapshotTarget`: `desktop` keeps the given container and an empty endpoint. `server2` gives `antiphon-server2-postgres-postgres-1` and the `ssh://` endpoint. A bound `-Container` other than the server2 container is `target-container`. A non-`ssh://` endpoint, an empty endpoint, or one carrying `;` or a leading `-` is `target-docker-host`. An empty, cased or unknown target is `bad-target`. Case `TargetResolve`.
- V-5. G-4 plans. The `-WhatIf -Plain` plan prints `step source: target=desktop container=antiphon-postgres docker=local`. With `-Target server2` it prints `target=server2 container=antiphon-server2-postgres-postgres-1 docker=ssh` and dumps that container into the same desktop destination. It never prints the endpoint and runs no docker. `-Target server3` exits 2 (`bad-target`). `-Target server2` on the age path or with `-RestoreDrill` exits 2 (`target-plain-only`). Case `TargetPlans`.
- V-6. G-4 server2 publish, process level, with the fake docker and an inherited `DOCKER_HOST=tcp://...`. `-Execute -Plain -Target server2` exits 0. Every docker call carries `DOCKER_HOST=ssh://mc@server2`. It dumps the server2 container once and runs `pg_restore --list` there twice (the restore proof). It never names `antiphon-postgres` and never reads the local docker context. The dump lands in the desktop destination. `snapshot-status.json` records `"exit":0`, `"mode":"plain"` and `"target":"server2"`. No endpoint is printed. Case `TargetServer2Publish`.
- V-7. G-4 Windmill wrapper. `Get-PlainSnapshotTarget`: an absent `SNAPSHOT-TARGET` (asserted absent before the call) is `desktop`. `server2` and `server2` plus CRLF are `server2`. `desktop` is `desktop`. Empty, blank, `Server2`, `server3` and two lines are `''`, which the main body turns into `reason=bad-target`. `Get-PlainSnapshotChildArguments` gives the exact legacy argument list for `desktop` and adds `-Target server2` for `server2`. Case `SnapshotTarget`. Red at `579980a07`: V-4 1, V-5 8, V-6 7 and V-7 1 failed.
- R-1. `scripts/test-server2-postgres-compose.ps1` stays green, including the doc and refusal cases.
- R-2. `scripts/test-apphost-main-worktree-guard.ps1` stays green: `dev-aspire.ps1` still refuses a linked worktree before any Docker or launch work.
- R-3. The plain and publish snapshot cases next to the change stay green: `WhatIfPlan`, `PlainOffByDefault`, `PlainPlan`, `PlainStatusNoSecret`, `PlainPublishPruneOrder`, `PlainOneDrivePlan`, `DockerDaemonPinned`, `PublishRejectsTinyDump` and `PublishRejectsMissingTableData`. The status JSON gains `target`, the fake docker gains an opt-in `DOCKER_HOST` line, and a `cp` into a container no longer writes a host file.
- R-4. `scripts/test-job-status.ps1` stays green, including `RunnerSourceSha` and `CloudSuffix`.

The full default `test-postgres-snapshot.ps1` run is longer than one short row on the loaded runner. It passed 780 assertions with 0 failures before the 570 s cap, so it is not a row. `-Case` now takes a comma list for R-3 and CP-4.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. Manual acceptance stays operator-only after the cutover:

- write `server2` into `C:\Users\lndco\antiphon-snapshot-runner\SNAPSHOT-TARGET`;
- re-pin `SOURCE-SHA`;
- confirm `ssh -o BatchMode=yes mc@server2 docker version` from the Windmill SSH user;
- take one `-Target server2` run;
- set `ANTIPHON_SKIP_DEV_POSTGRES=1` (part B sets it in the cutover).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | g2-deploy-image | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` | V-1, R-1 | RESULT passed=304 failed=0 | n/a | 15 |
| CP-2 | all | n/a | g1-dev-postgres-optout | `pwsh -NoProfile -File scripts/test-dev-postgres-optout.ps1` | V-2, V-3 | RESULT passed=38 failed=0 | n/a | 2 |
| CP-3 | all | n/a | g1-worktree-guard | `pwsh -NoProfile -File scripts/test-apphost-main-worktree-guard.ps1` | R-2 | 24 passed, 0 failed | n/a | 2 |
| CP-4 | all | n/a | g4-snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetResolve,TargetPlans,TargetServer2Publish` | V-4, V-5, V-6 | 50 passed, 0 failed | n/a | 3 |
| CP-5 | all | n/a | g4-snapshot-adjacent | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case WhatIfPlan,PlainOffByDefault,PlainPlan,PlainStatusNoSecret,PlainPublishPruneOrder,PlainOneDrivePlan,DockerDaemonPinned,PublishRejectsTinyDump,PublishRejectsMissingTableData` | R-3 | 157 passed, 0 failed | n/a | 5 |
| CP-6 | all | n/a | g4-job-status | `pwsh -NoProfile -File scripts/test-job-status.ps1` | V-7, R-4 | job-status: 92 passed, 0 failed | n/a | 2 |

### Cost

The ordinary floor is 29 minutes, run serially (`--serial`). Each row was measured below 3 minutes on server2 at a 1-minute load of 12 to 22.
