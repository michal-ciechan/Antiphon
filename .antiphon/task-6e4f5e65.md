# CARD-1079 S2 sampler

Outcome: the occupancy sampler, snapshot, and hosted service are on `feat/card-task-6e4f5e65`. Closed S2 rows are green at `88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb`. Detection only. No session or task is stopped, killed, released, or written.

Worktree: `/work/worktrees/task-6e4f5e65`. Branch: `feat/card-task-6e4f5e65`. Base: `760c3a9b8396e08ca0da597bf7c46bb856d1a6bb`. Original Code task and landing owner: `6e4f5e65`. Restart: none (no server or runner activation).

## Commits

- `3a997f3e3ee05d830f8e27d1954feb022a2d1a61` feat(card-1079): sample host occupancy on a timer without touching sessions
- `88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb` fix(card-1079): select the host-budget pipeline tests by their real type

Tested source for the green checkpoint is `88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb`.

## What landed in S2

- `SeatOccupancySampler` writes one `HostOccupancySample` per host and publishes `SeatOccupancyState` only after save. A runner inventory failure becomes `InventoryState=unavailable` with a bounded reason, keeps desktop counters, emits no seats, and does not leave the timer.
- `SeatOccupancyHostedService` returns immediately when `SeatWatchEnabled` is false. The sampler itself does not read that flag.
- `SeatDesktopJoin` loads the latest task per session with a grouped `Max(CreatedAt)` query (CARD-1090), then tie-breaks with the existing newer-row rule. It no longer loads each session's whole task history.
- Program registers `SeatOccupancyState`, `SeatOccupancySampler`, and `SeatOccupancyHostedService` beside `HostStatsPollService`.

## Checkpoints

Green run `20261006-080132-a2b1`, wall 2m57s, verdict GREEN exit 0. Shared build `bin-c1079-s2/`; the tool recorded the build time on CP-4 and `build=0s` on CP-3.

```
CHECKPOINT CP-3 commit=88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb build=ok filter=/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-6e4f5e65/.antiphon/checkpoints/20261006-080132-a2b1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=50.9096215s testsWall=2.5273659s teardown=1.3784404s hostWall=54.815428s
CHECKPOINT CP-4 commit=88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb build=reused filter=/*/Antiphon.Tests.Application/(HostEndpointTests*)|(AgentTaskPipelineStatusTests*)/C654_* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-6e4f5e65/.antiphon/checkpoints/20261006-080132-a2b1/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=121.5831734s startup=47.8052191s testsWall=4.4738489s teardown=1.5035594s hostWall=53.7826029s
```

CP-4 TRX is 4 `HostEndpointTests` plus 4 `AgentTaskPipelineStatusTests` C654 methods, 0 failed.

Earlier closed run `20261006-075455-315f` at `3a997f3e3ee05d830f8e27d1954feb022a2d1a61` was red. CP-3 was already 6/6. CP-4 executed 4, passed 4, failed 0, and missed min 8 because the plan filter named `HostBudgetPipelineTests`. That name is the file. The type is the partial class `AgentTaskPipelineStatusTests`. The filter fix is the second commit. Repair used both rounds: the first inlined the live-session predicate EF could not translate (`SeatOccupancySampler.Live`), and the second corrected CP-4.

## V and R

| ID | Result |
|---|---|
| V-7 | passed (CP-3) |
| V-8 | passed (CP-3) |
| V-9 | passed (CP-3) |
| V-10 | passed (CP-3) |
| V-11 | passed (CP-3) |
| V-12 | passed (CP-3) |
| R-3 | passed, HostEndpointTests 4/4 (CP-4) |
| R-4 | passed, AgentTaskPipelineStatusTests C654_* 4/4 (CP-4) |

## Unlisted runs

- Compile `bin-c1079-s2-compile/` through build-slot `c1079-s2-compile`, lease `476b2093-2e31-4395-8189-9a2602f3399b`, waited 0s, held 39s, 0 errors. Reason: confirm the sampler and join compile before the closed checkpoint.
- Preflight of sampler plus join, then a rerun after the live-session fix: 9 executed, 9 passed. Reason: the first run failed 5 sampler tests because EF could not translate `Live`. Superseded by CP-3 and the join run below.
- Filter probe `c1079-s2-r4b`: 8/8 on the replacement CP-4 filter. Reason: confirm the eight methods before editing the plan. Superseded by official CP-4.
- Extra filter `/*/*/(SeatDesktopJoinTests*)|(RunnerAlarmWiringTests*)|(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/*` on `bin-c1079-s2-compile/`, lease `1f32a4ae-47c6-4e08-a610-4692e748ca54`, waited 0s, held 53s, trx `.antiphon/c1079-s2-extra/extra.trx`: 7 executed, 7 passed, 0 failed. SeatDesktopJoinTests 3/3 (includes `C1079_Join_latest_task_query_is_grouped_per_session`). RunnerAlarmWiringTests 1/1 (Program boot still wires). TestClassificationGuardTests 1/1. SlowTestTripwireTests 2/2. Reasons: CARD-1090 join, hosted-service registration, and the brief's registry guard. The new sampler class is Integration. No slow-allowlist edit. Sampler methods in CP-3 were 1.7s to 2.5s.

`SeatDesktopJoinTests` is now 3 methods. A future CP-1 run executes 9 methods. The plan sentence "all 8 methods executed (19 results...)" is one method low. Min 19 still holds. CP-1's expect cell was not edited.

## Evidence

`scripts/check-evidence-diff.ps1 -BaseRef 760c3a9b8396e08ca0da597bf7c46bb856d1a6bb -HeadRef 88ce1c982f990dcc2bfdae44ecf1cf4feb700ebb` exited 0. commits=2 entries=0 violations=0. This file is a later docs commit and is not part of the tested source.

## Positive controls

All pending for method-scoped SourceLanding Mutation. None were executed. S2's controls are PC-5, PC-7, PC-8, and PC-9. PC-1 through PC-4 and PC-6 belong to S1. PC-10 through PC-16 belong to later slices.

## Scope

This brief closed the dispatch to the S2 checkpoint rows plus the named touched classes and the registry guard, and it ruled out a whole Unit lane. The Unit lane was not run.

Isolated `bin-c1079-*` directories were deleted after the runs. The green checkpoint had already removed its project `bin-c1079-s2/` outputs.
