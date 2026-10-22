# CARD-1461 option C Code report (task 75c03be9)

Outcome: S1-S3 implemented and green. Default off is byte-identical (the fallback branch is guarded by `fallback > TimeSpan.Zero`; V-4 and the six existing tests pin it). Nothing enabled anywhere; no appsettings overlay, the 6144 MB floor and `Landing:BuildSlotWaitMinutes` untouched; `AgentTaskLandService` untouched. Landed on master is not activation: the desktop needs the change on the rc line the AppHost runs, an AppHost restart on that tip, and a positive `Landing:BuildSlotMemoryFloorFallbackMinutes` in the server's configuration overlay (plan Reconciliation).

Branch `feat/card-task-75c03be9`, worktree `/work/worktrees/task-75c03be9` (server2 mirror), base `cbb297f8f0cc5247a55459828c073edb039d7ffe`. Plan: `docs/superpowers/plans/2026-10-22-card-1461-memory-floor-fallback-plan.md` on master at `6c1b0704b8b078d26adb359f1d7e9fa28def2c19` (not on this branch: it landed after the base). Runs record: `docs/superpowers/plans/2026-10-22-card-1461-code-runs.md`. Evidence: `.antiphon/checkpoints/<run>/` (gitignored).

## Commits (in order)

| SHA | Slice | Files | rc pick |
|---|---|---|---|
| `ea5331cf891872858e40b373cdfd8347ec228504` | carried checkpoint table | runs doc | skip |
| `2fc898ef5beef3451449319debd66b110abf8e27` | S1 settings + V-1..V-4 (red) | `LandingSettings.cs`, `LandVerificationBuildSlotTests.cs` | pick |
| `4b4aa96a09065c7b55d43e86732ca0a495186dfd` | S2 gate | `SessionRunnerBuildSlotGate.cs` | pick |
| `d5b9ab09742f716e314d356872119061e50be1b2` | runs 1-2 record | runs doc | skip |
| `6dacaed6b826c568a9c4fa6aeffe2edc08819ffe` | S3 docs | `docs/testing-and-build.md`, `docs/orchestration-loop.md` | pick |
| final tip (this report + run 3 row) | docs only | runs doc, this file | skip |

## CHECKPOINT lines (unedited)

Run 20261022-073807-9deb, rows CP-1, expected SHA `2fc898ef5beef3451449319debd66b110abf8e27` (S1, red record):

```
CHECKPOINT CP-1 commit=2fc898ef5beef3451449319debd66b110abf8e27 build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/(A_memory_floor_*)|(A_busy_answer_restarts_*)|(A_reachable_memory_floor_*) executed=4 passed=1 failed=3 skipped=0 trx=/work/worktrees/task-75c03be9/.antiphon/checkpoints/20261022-073807-9deb/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=2fc898ef5beef3451449319debd66b110abf8e27 sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.LandVerificationBuildSlotTests.A_busy_answer_restarts_the_memory_floor_fallback_window (CP-1) TimeoutException: The operation has timed out. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.LandVerificationBuildSlotTests.A_memory_floor_fallback_builds_at_the_fallback_cpu_count_and_releases_nothing (CP-1) TimeoutException: The operation has timed out. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.LandVerificationBuildSlotTests.A_memory_floor_refusal_past_the_fallback_window_builds_unleased_at_the_fallback_cpu_count (CP-1) TimeoutException: The operation has timed out. -> rows/CP-1/failures.md
verdict: RED exit=1
```

Both validators on that run: `CHECKPOINT SOURCE INVALID reason=cleanup_refused` (exit 2; the red run keeps `bin-c1461a/`). That refusal is the red record, as the plan says; `bin-c1461a/` was deleted by hand afterwards. V-4 (`A_reachable_memory_floor_runner_times_out_at_the_wait_deadline_when_the_fallback_is_off`) passed; the TRX names all four methods.

Run 20261022-074555-6d49, rows CP-1,CP-2, expected SHA `4b4aa96a09065c7b55d43e86732ca0a495186dfd` (S1+S2):

```
CHECKPOINT CP-1 commit=4b4aa96a09065c7b55d43e86732ca0a495186dfd build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/(A_memory_floor_*)|(A_busy_answer_restarts_*)|(A_reachable_memory_floor_*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-75c03be9/.antiphon/checkpoints/20261022-074555-6d49/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=4b4aa96a09065c7b55d43e86732ca0a495186dfd sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=4b4aa96a09065c7b55d43e86732ca0a495186dfd build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-75c03be9/.antiphon/checkpoints/20261022-074555-6d49/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=4b4aa96a09065c7b55d43e86732ca0a495186dfd sourceState=clean buildSource=verified
verdict: GREEN exit=0
```

`validate --rows CP-1,CP-2` and `scripts/validate-checkpoint-receipt.ps1 -Rows CP-1,CP-2`: `CHECKPOINT SOURCE VALID source=4b4aa96a09065c7b55d43e86732ca0a495186dfd rows=2`. CP-2's TRX names all ten methods (four new, six existing).

Run 20261022-080414-fc05, rows CP-3, expected SHA `6dacaed6b826c568a9c4fa6aeffe2edc08819ffe` (S1+S2+S3):

```
CHECKPOINT CP-3 commit=6dacaed6b826c568a9c4fa6aeffe2edc08819ffe build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-75c03be9/.antiphon/checkpoints/20261022-080414-fc05/rows/CP-3/run.trx slot=granted waited=75s dirty=0 source=6dacaed6b826c568a9c4fa6aeffe2edc08819ffe sourceState=clean buildSource=verified
verdict: GREEN exit=0
```

Both validators: `CHECKPOINT SOURCE VALID source=6dacaed6b826c568a9c4fa6aeffe2edc08819ffe rows=1`.

Repetitions: none beyond the three planned runs (one red, two green). Unlisted: the checkpoint tool build through `scripts/build-slot.ps1` (`bin-c1461tool/`, lease granted, 9 s; deleted) and one `import --plan` table validation. No whole-suite run.

## V-n outcomes (plan verification design; it defines no R-n)

| Id | Method | Red at S1 | Green at S2/S3 |
|---|---|---|---|
| V-1 | `A_memory_floor_refusal_past_the_fallback_window_builds_unleased_at_the_fallback_cpu_count` | TimeoutException (gate polled to the deadline) | pass |
| V-2 | `A_memory_floor_fallback_builds_at_the_fallback_cpu_count_and_releases_nothing` | TimeoutException | pass |
| V-3 | `A_busy_answer_restarts_the_memory_floor_fallback_window` | TimeoutException | pass |
| V-4 | `A_reachable_memory_floor_runner_times_out_at_the_wait_deadline_when_the_fallback_is_off` | pass (guard) | pass |

Required manual acceptance: none in the plan. Invariant checked: default off leaves the gate's control flow unchanged (new branch unreachable at `fallback == 0`; V-4 pins the timeout line and the absence of any `unleased` line with `new LandingSettings()`).

## Deviations from the plan, each with its reason

1. CP-1 filter gained `(A_reachable_memory_floor_*)`: the plan's two operands select V-1..V-3 only, while its own V-4 name starts with `A_reachable_`, so `Min 4` and the "3 failed, 1 passed" record were unreachable as written. Everything else in the table is the plan's. The table lives in the runs doc because the plan landed on master after this branch's base (editing it here is an add/add landing conflict).
2. V-4 mechanics: the body advances the fake clock 30 minutes after the first floor answer, instead of the client advancing before it. The plan expects `BUILD SLOT timeout after 30m position=1`; the unchanged gate prints `position=0` when the very first answer is already past the deadline (position is set in the refusal branches after the deadline check), so the plan's expectation needs one floor answer first. Assertions are the plan's.
3. The three minute-stepping tests advance through a helper that waits (bounded 5 s) for the fake client's next poll before asserting, rather than a bare `await Task.Delay(1)`: V-3's answers are positional (busy is the fourth poll), so a lagging timer continuation on a loaded host must not merge two minutes into one poll. Sequencing and expectations are the plan's.
4. V-1's "one line starting `BUILD SLOT waiting ...`" is asserted as at-least-one: the unchanged once-a-minute rule writes that line at minutes 0-4. The fallback line is asserted exactly once (V-1 by full text, V-3 by reason token).

## Pending for Mutation (method-scoped SourceLanding, none discharged here)

| PC | Mutant | Test expected red |
|---|---|---|
| PC-1 | drop `floorSince = elapsed` in the busy branch | V-3 (`A_busy_answer_restarts_the_memory_floor_fallback_window`, completes at minute 4) |
| PC-2 | hold uses `UnleasedMaxCpuCount` instead of `fallbackCpuCount` | V-1, V-2 (`maxcpucount=2`) |
| PC-3 | `>=` to `>` on the window | V-1 (`waited=300s` line absent at minute 5) |
| PC-4 | fallback branch guarded by `fallback >= TimeSpan.Zero` (default on) | V-4 |

No test covers the `Warning` level (plan: Review reads the `Warn` helper).

## Landability and rc

- `git merge-tree --write-tree origin/master HEAD` at `6dacaed6b` with master `bb43ec3529b9d8ad2a799f81080b9e34d89fa9c9`: clean (tree `911c50bcd460d034459dfde20e5d21024d68a5d9`); re-run at the final tip in the final message. CARD-1446's files are not touched here.
- rc `rc/2026-10-20` tip `ceb6e3d6b78471db6831258699c493b8eb2d619b`: chained `git merge-tree --write-tree --merge-base <c>^ <prev-tree> <c>` for `2fc898ef5`, `4b4aa96a0`, `6dacaed6b` all exit 0 (trees `7b5ac3db7`, `cd971a7c5`, `fd16c9caa`); the three touched source files are byte-identical between rc and master. Pick those three in order; skip the plan-type commits.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef cbb297f8f0cc5247a55459828c073edb039d7ffe -HeadRef HEAD`: `commits=5 entries=0 violations=0` at `6dacaed6b`; re-run at the final tip in the final message.

## For the orchestrator

- Operator decisions 1-6 of the plan stand as defaults (off, 0/2, 30-minute wait, 6144 floor, rc path): none taken here.
- Plan operator decision 7: file the option F follow-up card (LandAged detail carries the last `BUILD SLOT` line).
- Restart: none for this task; activation is the rc pick plus AppHost restart plus overlay value, all operator calls.
