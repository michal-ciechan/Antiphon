# CARD-1079 S4 — occupancy audit route

Outcome: `GET /api/hosts/{hostId}/occupancy-samples` is live as a read-only audit, and the three S3 review items are folded in. Official CP-7 is green on a clean tree. CP-8 was not re-run: the client visuals for the three kinds already landed with S3.

Tested source: `d54bd8c674d27d1fbff0350e63fd9618ea2c9794`
Branch: `feat/card-task-fdf2e7c4`
Worktree: `/work/worktrees/task-fdf2e7c4`
Base: `ed41f67cd7532336a3f0925fa9ed7d8c919d35fe`
Landing owner / original Code task: `fdf2e7c4` (`fdf2e7c4-b6a8-4205-ad75-be09f50aa19c`)
Plan: `docs/superpowers/plans/2026-10-06-card-1079-seat-idle-occupancy-attention-plan.md`
Restart: none. Detection only. Nothing on this slice stops, kills, releases, or writes a session or task.

## What landed in the tested commit

- `GET /api/hosts/{hostId}/occupancy-samples` in `server/Api/Endpoints/HostEndpoints.cs`. Newest first (`SampledAt`, then `Id`). Default window is the last 24 hours from the injected `TimeProvider`. Omitted `limit` is 500; a larger value is clamped to 2000 and the applied value is echoed as `limit`. `limit` below 1 is 400 `{ error: "limit_invalid" }`. A reversed window (`from` > `to`) is 200 with an empty `samples` list. Unknown host is 404 with body `not_found`, via `HostBudgetService.EffectiveAsync`. Query times are normalized to UTC. The query is `AsNoTracking` and does not call `SaveChanges`. POST to the route is 405. `ProjectAsync` is unchanged.
- `HostOccupancySampleDto` and `HostOccupancySamplesResponse` in `server/Application/Dtos/HostOccupancyDtos.cs`.
- `Attention` settings in `server/appsettings.json` beside `Alarms`, including rollback `SeatWatchEnabled: true`. Defaults match `AttentionSettings`.
- Evidence age is `age={minutes}min` (no space) in both seat and orphan evidence. Headlines still say "min".
- Docs: `docs/antiphon-api.md`, `docs/ops-http.md`, `docs/bootstrap.md`.
- No new migration. No client edit. No slow-allowlist edit. No `ParallelLimiter`.

## Folded S3 review items

1. `SeatOccupancyAttentionTests.C1079_Disabled_watch_hides_seat_idle_divergence_and_orphan_rows` — `SeatWatchEnabled=false` yields none of the three kinds. The gate already existed; this pins it at `AttentionService`.
2. `AttentionKindWireTests.Seat_occupancy_kinds_keep_appended_values_57_through_59` — values 57, 58, 59 and the JSON names `SeatIdle`, `OccupancyDivergence`, `SlotOrphan`.
3. Evidence age unit — `age=30min`, `age=1min`, `age=50min`.

## Closed checkpoint

Tool bootstrap: `scripts/build-slot.ps1 -Label c1079-s4-tool` (lease `2c0ba5eb-8136-45e9-8d0b-1be5e4baf6f8`, waited=0s, held=5s, 0 errors, the pre-existing CS8602 in `TaskOwnerGuard.cs`) to `tools/Antiphon.Checkpoints/bin-c1079-s4-tool/`, then `dotnet exec`. Run `20261006-102823-d020`. `--rows CP-7` only. `--after S4` was not used, because that row set also contains CP-8. Verdict GREEN exit=0. The tool deleted its owned `bin-c1079-s1/` through `bin-c1079-s4/`.

`scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261006-102823-d020/report.json -ExpectedSourceSha d54bd8c674d27d1fbff0350e63fd9618ea2c9794 -Rows CP-7` exited 0: `CHECKPOINT SOURCE VALID`.

Unedited tool line:

CHECKPOINT CP-7 commit=d54bd8c674d27d1fbff0350e63fd9618ea2c9794 build=ok filter=/*/Antiphon.Tests.Application/HostOccupancySampleEndpointTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fdf2e7c4/.antiphon/checkpoints/20261006-102823-d020/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=d54bd8c674d27d1fbff0350e63fd9618ea2c9794 sourceState=clean buildSource=verified

CP-8 skipped. The plan files the client visuals under S4, and those visuals already landed (task `2a61837b`, tested source `4654aeb4a7c1bc347d4d655c24ef2c4e8e11af9a`, Vitest 30 passed / 0 failed, recorded on master by `ed41f67cd`). This slice does not touch the client, so CP-8 was not repeated.

## Red first

On the dirty tree, before the route and the age unit, output `bin-c1079-s4-red/` (not checkpoint evidence):

- `HostOccupancySampleEndpointTests`: executed 3, passed 0, failed 3. The two window tests got 404 because the route was absent; the unknown-host test got a framework 404 whose body did not contain `not_found`. TRX `.antiphon/c1079-s4-red-endpoint/red-endpoint.trx`.
- `SeatOccupancyAttentionTests`: executed 9, passed 7, failed 2. Failures were `C1079_Seat_idle_warning_names_runner_session_task_status_age_and_pushed` and `C1079_Slot_orphan_is_immediate_for_occupying_orphans_only` on the bare `age=` field. The new disabled-watch test was among the 7 that passed. TRX `.antiphon/c1079-s4-red-attention/red-attention.trx`.

Repair rounds used: 0 of 2. Those reds were the guarded defects, then the implementation. No assertion was widened and no timeout was raised.

## Unlisted runs on the tested SHA

Reason: the brief requires the existing classes this change touches, plus the registry guard, run serially so a combined driver does not hit Postgres 53300. They are not extra checkpoint rows. One build, then `--no-build`. Clean tree at `d54bd8c674d27d1fbff0350e63fd9618ea2c9794`.

Build `tests/Antiphon.Tests` to `bin-c1079-s4-legacy/` with `UseAppHost=false`. Lease `7e4a5af7-b209-495d-8f27-a620501c68d0`, waited=0s, held=105s, 0 errors.

| Run | Filter | Result | Lease | waited | held | TRX |
|---|---|---|---|---|---|---|
| R-3 hosts | `/*/Antiphon.Tests.Application/HostEndpointTests/*` | 4 executed, 4 passed, 0 failed, 0 skipped | `017d5e23-b00d-494b-b3c9-2f77d1815026` | 0s | 45s | `.antiphon/c1079-s4-legacy/hosts/run.trx` |
| R-4 pipeline | `/*/Antiphon.Tests.Application/AgentTaskPipelineStatusTests/C654_*` | 4 / 4 / 0 / 0 | `af43a872-cf0b-4c72-9938-e8145176e668` | 0s | 44s | `.antiphon/c1079-s4-legacy/pipeline/run.trx` |
| attention | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/*` | 9 / 9 / 0 / 0 | `efc6e73d-f61c-4d70-bf0d-d00b971d4c1e` | 0s | 40s | `.antiphon/c1079-s4-legacy/attention/run.trx` |
| wire | `/*/Antiphon.Tests.Application/AttentionKindWireTests/*` | 4 / 4 / 0 / 0 | `5dfe4be3-1420-4c00-a020-61f245ed0b12` | 0s | 4s | `.antiphon/c1079-s4-legacy/wire/run.trx` |
| registry | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | 3 / 3 / 0 / 0 | `58fba4ae-3d7b-4de6-acc3-9d4f6dfb07c2` | 0s | 7s | `.antiphon/c1079-s4-legacy/registry/run.trx` |

No API route-inventory guard exists. The registry guard the brief names is the row above. No new Slow class, so `tests/Antiphon.Tests/slow-tests-allowlist.txt` was not edited. The whole Unit lane was not run: the task body limits this slice to the S4 plan rows and forbids a whole-Unit sweep. That sentence is the scope for this task.

## Ordinary V/R

- V-21, V-22, V-23 passed in CP-7 (3 methods, 0 failed, 0 skipped).
- V-24 not re-run. Passed on the landed S3 client slice (30 Vitest results). See CP-8 skip above.
- R-3 passed: `HostEndpointTests` 4/4.
- R-4 passed: `AgentTaskPipelineStatusTests` `C654_*` 4/4.
- V-1..V-20 and R-1, R-2, R-5, R-6 stay with the landed S1–S3 slices and were not re-run.

## Evidence diff

`scripts/check-evidence-diff.ps1 -BaseRef ed41f67cd7532336a3f0925fa9ed7d8c919d35fe -HeadRef d54bd8c674d27d1fbff0350e63fd9618ea2c9794` exited 0: commits=1, entries=0, violations=0. This report file is a later commit on the same branch. The caller message carries the check over the full pushed range.

## Positive controls

Not executed. Pending for method-scoped post-land SourceLanding Mutation: PC-1 through PC-16. S4 adds PC-14 (unknown host must stay 404) and PC-15 (`Take(limit)` must stay). PC-16 is the already-landed client label and stays pending. PC-1..PC-13 remain on their slices.

## Next

Review the tested source. Restart none.
