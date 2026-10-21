# CARD-1408: post-cutover Postgres monitor and latency rollback trigger

Stage C3 of CARD-1401. Parent plan: `docs/superpowers/plans/2026-10-21-card-1401-postgres-to-server2-plan.md`, decision D-11 and phase 5. Base `13048a2eb356446f4f14960249d7fde2ce5c51e8` (origin/master). Script only. No Antiphon.Tests project, no database, no Docker, no live HTTP in tests.

## Scope

- `scripts/postgres-cutover-monitor.ps1`: read-only monitor. Modes `Plan` (default), `Baseline`, `PreWindow`, `Watch`. Every mode is a dry plan until `-Execute`. Endpoints are parameters. The measurement seams (`-HttpGet`, `-TcpConnectMs`, `-Now`) take scriptblocks so the harness needs no server.
- `scripts/test-postgres-cutover-monitor.ps1`: `-Case` harness in the repo's script-test pattern.
- Baseline and result files default to `.antiphon/postgres-cutover/`, which `.gitignore` already ignores.

## Decisions

- **D-1 - Exit codes.** 0 `OK`, 1 `WARN`, 2 `ROLLBACK`, 3 `ABORT` (pre-window), 4 `INVALID` (bad input, missing baseline, unmeasurable). `ROLLBACK` and `ABORT` are also printed as their own line so a runbook or scheduled job can grep for them.
- **D-2 - Rollback rule (D-11).** `Watch` rolls back when the fresh pipeline median is above both `-MedianFloorMs` (1000) and `-MedianRatio` (3) times the baseline median, or when the window's dispatch plus land failure count exceeds the baseline count by more than `-FailureSlack` (2). A median above the ratio but under the floor exits 0 with a `NOTE` line.
- **D-3 - Pre-window abort.** `PreWindow` aborts when the TCP connect median to `-Server2Host:-Server2Port` (the Tailscale address) is above `-ConnectAbortMs` (20). `-LanHost` is measured as evidence only and never decides.
- **D-4 - Warn.** Exit 1 when the fresh window had sample errors, a failure count above baseline by one to `-FailureSlack`, a health code other than 200, or a version SHA that differs from the baseline. None of these is a rollback trigger.
- **D-5 - Read-only.** GET only. The script never calls `restart-apphost.ps1`, `docker`, `user-secrets`, `psql`, or a `POST`/`PUT`/`DELETE`. The only file it writes is its own JSON. Property names that look like credentials are dropped from recorded diagnostics.
- **D-6 - Failure count.** One `GET /api/agent-tasks?projectId=<id>&updatedSince=<window start>` list per measurement. Failed tasks are items with `status` `Failed`; failed lands are items whose `landing` or `landRequest` reports `LandRefused`, `Failed` or `NeedsResolution`. `-TaskListUrl` overrides the URL.

## Operator-only

Scheduling the monitor, the baseline window timing, the project id and endpoints, the server2 Tailscale and LAN addresses, tightening thresholds in the window log before phase 1, and running phase 6 after `ROLLBACK`.

## Verification design

- V-1 median above both the 1 s floor and 3x baseline exits 2 and prints `ROLLBACK`; above 3x a tiny baseline but under 1 s exits 0; above the floor but under 3x exits 0.
- V-2 failures more than 2 above baseline exit 2; exactly 2 above is not a rollback.
- V-3 TCP connect median above 20 ms exits 3 and prints `ABORT`; a reading near 7 ms exits 0.
- V-4 the default dry plan calls no seam and writes no file; `Watch -Execute` without a baseline exits 4; credential-looking properties are absent from the recorded JSON.
- V-5 the script source has no `restart-apphost`, `docker`, `user-secrets`, `psql`, or non-GET HTTP verb; a child `pwsh` process returns exit code 2 with `ROLLBACK` on stdout.

The red witness is CP-1 against the test-only commit (monitor script absent, harness exit 1). The green run is CP-1 against the production commit.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | n/a | monitor-thresholds | `pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1` | V-1, V-2, V-3, V-4, V-5 | at least 20 assertions passed, 0 failed, exit 0 | n/a | 3 |

### Cost

Ordinary Code floor 3 minutes plus authoring. No PCs are commissioned; SourceLanding Mutation may add deliberate mutants of the threshold comparisons.
