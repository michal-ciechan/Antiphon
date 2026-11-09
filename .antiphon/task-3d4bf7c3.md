# CARD-1796 Code (task 3d4bf7c3)

Branch `feat/card-task-3d4bf7c3`, runner mirror `/work/worktrees/task-3d4bf7c3`, base `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa` (= origin/master at report time).
Source review: CARD-1794 Review e4276bff (`origin/feat/card-task-e4276bff:.antiphon/task-e4276bff.md`), advisories A-1..A-4. No earlier implementation existed (`git log --all --grep=CARD-1796` empty).

## Commits

| Role | SHA |
|---|---|
| red (witnesses + API shape, fix lines disabled and marked RED) | `c82d2d6fc3059ffa4a6cdebab6f38ecd02dbd8de` |
| fix + docs (tested source) | `98f94966bd3b8caa006dfb120b340e8fa65b2b62` |
| this report | the branch tip (report only) |

## What changed

- **A-1:** new singleton `server/Application/Services/RunnerWorktreeRetentionTicks.cs` holds the process's tick turn (`SemaphoreSlim(1,1)`). `RunnerWorktreeRetentionService.RunTickAsync` (the scheduled job and `RunExecuteOnceAsync`) waits for it. The new `RunPreviewAsync` (preview route) and `StartExecute` (execute route) refuse with 409 `tick_in_progress` while the turn is held. This is in-process only: a second server process running retention is not covered (only the desktop server lists `server2`).
- **A-2:** `AgentTaskEndpoints.ExecuteRunnerRetention` (was `ExecuteRunnerRetentionAsync`) now returns 202 `{ runnerId, runId }` at once, with Location `/api/agent-tasks/worktree-residue/runs/{runId}`.
  - The tick runs via `Task.Run` (execution context not flowed) in a fresh DI scope, on `IHostApplicationLifetime.ApplicationStopping` rather than `RequestAborted`. The background task releases the turn in `finally` and logs any failure.
  - The run row is saved right after creation (no `FinishedAt` yet), so the run is readable while the tick runs. This applies to every tick.
  - The kill switch and the allow-list now refuse at the route (409 `retention_disabled` / `runner_not_listed`) instead of the old `[]`. The service-level `RunExecuteOnceAsync` still returns `[]`.
- **A-3:** `C1677RetentionSettingsTests` pins `MaxAbsentPerTick`: default 200, appsettings 200, bound 200, and validator rows -1/0/1000/1001.
- **A-4:** when `StopReason` breaks the loop, every unsent in-bound candidate (from the current index on) gets a `queued_next_tick` Held row and is counted in `TickResult.Queued`. The Deferred stop row is kept.
- **Docs:** `docs/docker-stack.md`, `docs/ops-http.md`, `docs/antiphon-api.md`.
- `tests/test-execution-policy.json` is unchanged, so there is no policyHash or PinnedInputDigest refresh. There is no migration.

## Witnesses (C1677RetentionServerTests unless noted)

| Test | Guards | Red at c82d2d6f |
|---|---|---|
| `C1796_scheduled_tick_waits_for_a_running_execute_tick` | A-1 wait | yes: inventory count 2 (scheduled ran concurrently) |
| `C1796_a_running_tick_refuses_a_second_execute_and_a_preview` | A-1 409 | yes: no ConflictException |
| `C1796_execute_route_answers_with_its_run_before_the_tick_ends` | A-2 background + early run row | yes: TimeoutException (route blocked on the tick) |
| `C1796_execute_route_refuses_while_off_or_for_an_unlisted_runner` (disabled, unlisted) | route gate | yes x2 |
| `C1796_a_stopped_tick_queues_its_unsent_remainder` (enabled, execute) | A-4 | yes x2: Queued 0 |
| `C1794_execute_route_requires_the_operator_credential` (3 cases, rewritten for the new route shape) | 403 + accepted background run | green at red (credential path unchanged) |
| `C1677RetentionSettingsTests.Shipped_defaults_*` and the `Validator_bounds` MaxAbsentPerTick rows | A-3 | green at red (pin); killed by mutant M7 below |

## Runs (server2 Linux, each command build-slot leased, native default output, `-p:UseAppHost=false`, TEMP=/tmp)

| Run | Source | Filter | Result |
|---|---|---|---|
| red build + run | c82d2d6f | `/*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)\|(C1677TempSweepServerTests*)/*` | 115 total, 7 failed (all 7 C1796 cases, intended), 108 passed, 0 skipped; build 301 s |
| fix build + touched classes | 98f94966 | same plus `(C1677DiskAlarmTests*)` | **167 total, 167 passed, 0 failed, 0 skipped**. TRX: RetentionServer 90, DiskAlarm 52, RetentionSettings 18, TempSweep 7; build 279 s |
| guard batch | 98f94966 (same build) | `(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)` | 77 total, 68 passed, **9 failed (inherited)**, 0 skipped. TRX: 41/1/5/2/3/11/5/9 |
| mutant build + run (M4+M5+M7, batched: different files and methods) | 98f94966 + mutants | `(C1677RetentionSettingsTests*)\|(C1677RetentionServerTests*)/(Validator_bounds*)\|(C1796_a_stopped_tick_queues_its_unsent_remainder*)\|(C1796_execute_route_refuses_while_off_or_for_an_unlisted_runner*)` | 20 total, 6 failed as expected (MaxAbsentPerTick -1/1001; refuses x2; stopped-queue x2), 14 passed. Mutants restored with `git checkout --`; tree clean |

**Inherited red (guard batch):** all 9 failures are one violation, `lane-xor Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests`. The failing tests are `TestClassificationGuardTests.Registry_matches_compiled_metadata`, `C487_G068`, `C1383_Rebase_preserves_distinct_appended_pairs` x2, `C1383_Layout_opt_in_is_scoped(non-repository)` and `C1383_Valid_reason_pairs_are_accepted` x4.
- Cause: `tests/Antiphon.Tests/Antiphon.Tests.csproj` links `..\Antiphon.IO.WriteBehind.Tests\WriteBehindFileSystemTests.cs` (master 6a371bc38 / 68e276a9b), and that class carries neither the Unit nor the Integration category.
- Neither file is in base..HEAD, so it is inherited by construction. It was not re-run at base: that needs another ~5 min cold build.
- No card exists (`card.ps1 search WriteBehindFileSystemTests -All`: 0). One should be filed: add `[Category("Unit")]` to the class.

Other checks:
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef d2ff7e8d4d -HeadRef HEAD` at 98f94966: commits=2 entries=0 violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean, tree `d531dfa47e5e8110295b93a952b0eb8dd768df51`. origin/master = base.

Unlisted runs, and why: the mutant build and run (the brief asks for a mutant per witness); the red build and run (red-at-base proof). There was no checkpoint plan for this card, so there were no CP rows. The brief named the run shape.

## Owed / not run

- **Whole Unit lane: not run.** The brief's budget is 120 min and the lane is about 3 h on server2. The brief named touched classes plus the guard batch. This item is deferred to final and is not passed.
- **PCs pending for SourceLanding Mutation, method-scoped.** The red commit killed these only jointly:
  - M1 `EnterAsync` no-op, which should turn the scheduled-tick wait witness red.
  - M2 `TryEnter` always true, which should turn the 409 witness red.
  - M3 `StartExecute` running inline, which should turn the background witness red.
  - M6 the early run save removed, which should turn the background witness red (no row).
  - M4, M5 and M7 were run individually (batched above) and were killed.
- The operator action from the card is not done: remove the out-of-repo `MinTerminalHours=24` override on the desktop AppHost host (unreachable from server2).
- Build outputs (bin/obj of the built projects) and scratch TRX dirs were deleted after the runs.
