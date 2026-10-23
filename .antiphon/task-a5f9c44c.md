# CARD-1553 / 1554 / 1555 / 1551 fixture repairs

Original Code task and landing owner: `a5f9c44c`.
Branch: `feat/card-task-a5f9c44c`. Worktree: `/work/worktrees/task-a5f9c44c`.
Task base/current master at start: `877b9ce108e6e2bb4ba81840a99c4dd788302ca4`.
This report and manifest are frozen before final qualification. The final response and ignored `.antiphon/a5f9-final-report.log` retain the actual final SHA, fresh receipt, class timings and validation outcomes; this pre-run report makes no green claim.

## Changes and red witnesses

Fixture/test code only. No production behavior, timeout, retry or assertion was weakened. Existing failing tests preceded the fixes in history.

- CARD-1553: preserve absent-object/unavailable cases using an independently acquired real repository mutation lease during baseline observation. Release before evaluating movement. Add both primary and repair-source successful uncached-fetch witnesses for the released dispatch lease. Existing pins, persisted baseline, attribution and workspace-immutability assertions remain.
- CARD-1554: register real AgentSessionService and required composition in BootStallWorld, retaining a refusing protocol factory. Also seed the actual watchdog-failure cases with one attempted-but-undelivered brief: zero-attempt Pending debt belongs to CARD-1150 recovery and always defers failure even with correct DI. The test explicitly resolves the service and keeps every cleanup, concurrency-refusal and no-destructive-action assertion.
- CARD-1555: runners faults match desktop occupancy's CapacityWaitRetained/RunnerId-null predicate. SessionStateLoader counts remain usable during notice delivery. Fresh base execution additionally found 22 inherited missing-notice failures: the fixture lacked supported idle Claude dialog observation. Enable the existing real-generation fake dialog setup; unknown, deduplication and recovery assertions remain.
- CARD-1551: use bare real git status as the write control. Current production GetChangesAsync explicitly sets GIT_OPTIONAL_LOCKS=0, so it is no longer a valid writing control. Fixture child processes explicitly enable optional locks, disable ignorestat/fsmonitor, make the tracked stat entry stale and place the index timestamp safely in the past. Production counts still must preserve the index timestamp. Use the existing strict process limiter for rc compatibility.

Read the full diagnosis from `origin/feat/card-task-392d9d33:docs/investigations/2026-10-23-rc-5a126f6fc-qualification-groups-15-20-debug.md` and all four board cards. Fresh baseline run `20261023-120058-5a86` tested clean base HEAD: 38 executed, 5 passed, 33 failed, zero skipped. Fresh TRX identities inspected individually. C1115 reproduced six negatives (including unavailable-primary/no-claim, beyond the five diagnosed Windows cases); BootStall reproduced the two exact failures and missing-service warning; D3 reproduced the two exact SessionStateLoader fault escapes plus the 22 missing-notice assertions; the Git control reproduced its unchanged-mtime failure. Base build measured 412.78 seconds; test-host times 92.58 / 42.70 / 99.14 / 9.59 seconds. Preserve these historical reds.

### Historical baseline CHECKPOINT lines

CHECKPOINT CP-1 commit=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 build=ok filter=/*/*/RepairSourceDispatchTests/C1115_* executed=10 passed=4 failed=6 skipped=0 trx=/work/worktrees/task-a5f9c44c/.antiphon/checkpoints/20261023-120058-5a86/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 build=reused filter=/*/*/BootStallDetectionTests/C1151_Delivery_watchdog_stopper_requires_real_safe_failure* executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-a5f9c44c/.antiphon/checkpoints/20261023-120058-5a86/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 build=reused filter=/*/*/OrchestratorInstructionsRefreshTests/Failed_reads_are_unknown_in_file_and_notice_then_recover* executed=24 passed=0 failed=24 skipped=0 trx=/work/worktrees/task-a5f9c44c/.antiphon/checkpoints/20261023-120058-5a86/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 build=reused filter=/*/*/DelegateCheckProbeTests/a_status_probe_leaves_the_git_index_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a5f9c44c/.antiphon/checkpoints/20261023-120058-5a86/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 sourceState=clean buildSource=verified

## Final verification scope

The explicit brief overrides the generic Final profile: no Unit lane and no whole assembly. One shared isolated build and one exact OR-filter invocation cover all four repaired full classes plus the two full BootStallWorld consumers. No unbounded classes. Ordinary budget: roughly 7 minutes cold build plus 5 minutes tests/startup/cleanup, bounded by explicit 15m row and 25m total budgets. GIT_OPTIONAL_LOCKS=0 deliberately supplies the inherited setting that must not defeat the positive control. No repetitions after green. No new top-level test classes; existing RepairSourceDispatchTests Slow classification and allowlist stay valid.

V-1 = C1115 ten variants plus C1553 two uncached-fetch variants (CARD-1553).
V-2 = full BootStallDetectionTests including original C1151 V-15 three variants (CARD-1554).
V-3 = full OrchestratorInstructionsRefreshTests including all 24 D3 variants (CARD-1555).
V-4 = full DelegateCheckProbeTests, including Git write/read-only control (CARD-1551).
R-1 = full BootStallWorkingTickCharacterizationTests (shared fixture).
R-2 = full DelegationDispatchRecoveryBoundaryTests (shared fixture; all partial files).
R-3 = rc picks apply and Antiphon.Tests compiles in detached scratch (below).
R-4 = final receipt validation, evidence diff over full task range, fresh TRX class/method roster, merge-tree against freshly fetched origin/master, final remote tip.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-a5f9-final/ | rc-fixture-repairs | `/*/Antiphon.Tests.Application/(RepairSourceDispatchTests*)\|(BootStallDetectionTests*)\|(DelegateCheckProbeTests*)\|(OrchestratorInstructionsRefreshTests*)\|(BootStallWorkingTickCharacterizationTests*)\|(DelegationDispatchRecoveryBoundaryTests*)/*` | V-1, V-2, V-3, V-4, R-1, R-2 | all six full classes and repaired methods, 0 failed/skipped | 40 | 12 | `GIT_OPTIONAL_LOCKS=0;TUNIT_MAX_PARALLEL_TESTS=4` |

Run with the prebuilt checkpoint tool: `run --plan .antiphon/task-a5f9c44c.md --expected-source-sha <committed HEAD> --row-timeout 15m --total-timeout 25m --max-wait 55s`; continue `wait --run <id>` until not 75. Validate report.json with scripts/validate-checkpoint-receipt.ps1 at that same final source SHA. Delete only task-owned bin-a5f9-* outputs; preserve ignored evidence.

## RC application and platform obligations

Ordered code picks onto exact rc base `5a126f6fc5ae4e16d803b628716b8f0485a37174`:

1. `692d559ca2c5fae6211b41ad99d8e887badce451`
2. `762aebee213330b75b7c9623cea71bab78aaf3a5`

Both apply cleanly in `/tmp/a5f9-rc-scratch`; resulting detached source `fd1607c72` (full SHA in final evidence). First compile exposed rc's absent GitProcessSpawnLimit, corrected by pick 2. Prior fixture difference: rc RepairSourceDispatchTests uses ProcessSpawnLimit while master uses GitProcessSpawnLimit; this task preserves each branch's existing choice. The other four original fixture files were identical across the two bases. Recompile after pick 2 is a required bounded gated run; its actual result is retained in final evidence. Report-only/manifest/mutation-candidate commits are not required rc code picks.

GET /api/runner-defaults selected server2 and GET /api/session-runners confirmed Linux eligibility. No runner or platform was pinned. Desktop was not used. Windows rows WB-1553, WB-1554, WB-1555 and WB-1551 remain OWED to a separately commissioned desktop/native lane; no Linux receipt is represented as Windows qualification. Restart: none.

## Additional driver accounting

The base red-witness checkpoint selection was explicitly required by the brief and used one shared build. The tool bootstrap was separately gated (7s, granted/waited=0s). RC compilation is the brief's additional required build (15-minute hard cap including slot wait), not an unreported test selection. The initial rc compile was red with one missing-type diagnostic and 47 warnings; no test failure was blamed on this task without a fresh base witness.

A temporary reflection diagnostic outside source used the unchanged base test assembly to inspect the inherited zero-notice failures. Its first net9 invocation failed before fixture entry with a System.Text.Json load mismatch (not a test red); retargeting the temporary diagnostic to net10 succeeded and observed a Pending, zero-attempt note and no submitted body. Source inspection located the unsupported-dialog input gate and the existing ClaudeDialogFixture real-generation setup. These diagnostic/tooling builds and invocations were build-slot gated, with no assertion/qualification credit; a final diagnostic-only build was not invoked. Costs and slot receipts stay in /tmp/a5f9-probe-*.log and are copied to ignored final evidence before cleanup.

## Mutation and handoff

Every PC-1..PC-6 variant is PENDING. Candidate: `docs/superpowers/mutations/2026-10-23-rc-fixture-repairs-a5f9c44c.md`. No deliberate mutants were run in Code. Initial ordinary light Review remains required after ordinary Linux verification completes; caller lands original Code task, then commissions SourceLanding Mutation. Any missing required execution retains its incomplete gate under D-2.
