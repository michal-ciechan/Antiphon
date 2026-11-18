# Postgres move to server2, blocking gaps part A: checkpoints

Code task `801355c0`, from the runbook `docs/investigations/2026-11-18-postgres-move-runbook.md` (commit `2e8907ee0` on `feat/card-task-3b15e001`), gaps G-1 and G-2. Script-only. No Docker daemon, no ssh, no production database, no desktop run. `scripts/cutover-postgres-server2.ps1` and its two test scripts belong to in-flight Code `acfe82da` and are not touched here. G-3 (a drill-record producer) is a new drill mode whose JSON reader lives in the cutover script, so it is deferred to part B.

G-4 was first built here the superseded way (`830d7e92b8`, `e1031dbc71`). Code task `b92e5206` reverted both. G-4 now follows the reviewed plan `docs/superpowers/plans/2026-11-18-postgres-backup-retarget-plan.md` (Docs `36161193`, tip `d04fa1304`), and its rows live in `docs/superpowers/plans/2026-11-18-postgres-gaps-g4-checkpoints.md`.

Version rule (operator 2026-11-18): the move keeps PostgreSQL 16.14 on Debian pgdg13. CARD-1988 owns the later upgrade.

## Verification design

- V-1. G-2 patch pin. `Test-Server2PostgresImage` accepts `postgres:16.N@sha256:<64 lowercase hex>` and `postgres:16.N-trixie@sha256:<...>`. It still refuses (`image-floating`) `postgres:16.14`, `postgres:16.14-trixie`, `postgres:16`, `postgres:latest`, `postgres:latest@sha256`, `16.14-bookworm` (pgdg12), another major, `16.14.1`, `16.`, `16.014`, a short digest, an uppercase digest and `library/postgres`. A `-WhatIf` env file with `postgres:16.14@sha256:` is `ok` and runs no docker. A digest-less `postgres:16.14` is refused. Case `image-patch-pin`. Red at `e229fb4d3`: 4 failed.
- V-2. G-1 opt-out, through a child pwsh and a fake docker that records argv. When `ANTIPHON_SKIP_DEV_POSTGRES` is removed from the child environment (absence asserted before the call), empty or `0`, the bring-up is `compose -f <file> up -d` of every service, unchanged. When it is `1`, the bring-up is `compose -f <file> up -d --no-deps redpanda`, docker never sees `postgres`, and the log line names the variable and the skipped desktop Postgres. Cases `skip-value`, `services`, `arguments`, `invoke-absent`, `invoke-empty` and `invoke-optout`.
- V-3. G-1 wiring. `dev-aspire.ps1` and `scripts/autostart-apphost.ps1` dot-source `scripts/lib/dev-postgres-compose.ps1`, bring compose up only through `Invoke-DevComposeUp`, no longer run `docker compose ... up -d` directly, and skip their `antiphon-postgres` waits when opted out. The new files are ASCII. Cases `wiring` and `ascii`. Red at `eff4e7eed`: 15 failed (V-2 and V-3).
- R-1. `scripts/test-server2-postgres-compose.ps1` stays green, including the doc and refusal cases.
- R-2. `scripts/test-apphost-main-worktree-guard.ps1` stays green: `dev-aspire.ps1` still refuses a linked worktree before any Docker or launch work.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. Manual acceptance stays operator-only after the cutover:

- set `ANTIPHON_SKIP_DEV_POSTGRES=1` (part B sets it in the cutover).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | g2-deploy-image | `pwsh -NoProfile -File scripts/test-server2-postgres-compose.ps1` | V-1, R-1 | RESULT passed=304 failed=0 | n/a | 15 |
| CP-2 | all | n/a | g1-dev-postgres-optout | `pwsh -NoProfile -File scripts/test-dev-postgres-optout.ps1` | V-2, V-3 | RESULT passed=38 failed=0 | n/a | 2 |
| CP-3 | all | n/a | g1-worktree-guard | `pwsh -NoProfile -File scripts/test-apphost-main-worktree-guard.ps1` | R-2 | 24 passed, 0 failed | n/a | 2 |

### Cost

The ordinary floor is 19 minutes, run serially (`--serial`). Each row was measured below 3 minutes on server2 at a 1-minute load of 12 to 22.
