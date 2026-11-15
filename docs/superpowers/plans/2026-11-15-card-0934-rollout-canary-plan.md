# CARD-0934 gate-2 Plan canary

Deliver the [eleven-line gate note](2026-11-15-rollout-canary-note.md) and report execution identity. This task documents the gates; the caller owns subsequent rollout execution.

## Decisions

- D-1: Use the checked-in gate table and named phases because it owns the rollout contract; reject inferred promotion or executing rollout phases from this canary.
- D-2: Preserve the requested note and add this card-named plan; commit/push both on the assigned branch because higher-priority stage invariants require publication. The brief's single-file/no-push alternative conflicts with those invariants.
- D-3: Use document inspection and runtime identity reads as complete verification; reject builds/tests because no executable contract changes. No implementation or TestDesign stage remains.
- D-4: Read placement live; pin only the sanctioned canary host, omit OS restrictions and hard-coded fleet addresses. The observations below are a snapshot, not future placement defaults.

## Ground truth

| Brief assumption | Observed code/runtime behavior |
|---|---|
| Nine rollout gates | The gate table names nine; `scripts/deploy-server2.ps1` exposes five mutating rollout phases, with separate smoke/promotion checks. |
| Temp promotion | `drain-old` sets redirect to temp; `server/Application/Services/DefaultRunnerRoutingPolicy.cs` follows eligible redirects for automatic placement; explicit pins remain explicit. No independent promotion phase. |
| Four-hour old drain | Script parameter defaults to 480 minutes; the runbook explicitly passes `-WaitIdleMinutes 240`. Require all three known-zero counters before redeploy. |
| Old becomes ready after canary | `redeploy-old` clears drain before separate smoke; gate-7 failure requires promptly draining old toward accepting temp. |
| Canary runner | Live defaults revision 2 prefer server2; catalogue reports temp Linux, available, dispatch eligible and accepting, with one occupied seat. Confirmed status build: `a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`. |
| Per-task workspace | `/work/worktrees/task-8ec395a1`, pattern `/work/worktrees/task-<id>`; hostname `67ce342a5272`; `codex-cli 0.160.0`; initial HEAD `52a90a3935f1f8ed4198200ce3a5e21ca8dab370`. |

## Slices

| Slice | Files | Verification |
|---|---|---|
| S-1: bounded gate summary and stage plan | This file and `docs/superpowers/plans/2026-11-15-rollout-canary-note.md` | Compare all nine gates/stop conditions with owner; count note lines; inspect diff; verify branch, remote SHA and clean status. |

Existing tests affected: none; no migrations, public DTO/routes, operation labels, remote functions or statement budgets change. See [impacted classes, CARD-1782](../../testing-and-build.md#impacted-classes-card-1782).

## Verification design

Manual documentation lane on the assigned canary runner: read owner and the two placement GET routes, confirm runner status and command identity, then inspect the two-file diff and note line count. No test filters, suffix-collision roster, checkpoint rows, process launches beyond local inspection commands, or waiting-input sessions are introduced. Completion proves the delegate ran and produced this report; the caller must observe report receipt to credit gate 2.

### Checkpoints

None: no build/test checkpoints apply to a documentation canary. Manual lane and exact document checks are specified above.

## Performance budget

No tests/builds: documentation-only changes affect no project graph or downstream app/category batches. Target manual inspection/report within 10 minutes (provisional planning target, not a measured p95 or execution timeout); read shared owner/setup once per session. Container readiness, data preparation, host startup, migrations, snapshots, test body/teardown, build and slot admission are not invoked. Runtime HTTP reads use 20-second bounds, a provisional single-observation choice with no p95 sample, not a claimed 2x measured-p95 budget. No production/test timeout changes. Retain command timings/identity in task transcript and final report; no generated evidence files. Any future test stage must measure comparable p95 and use about 2x timeouts under [performance standards](../../testing-and-build.md#performance-standards); a test run over 30 minutes requires Debug.
