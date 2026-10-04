# CARD-0959 Linux final-verification continuation

Landing owner: `859094b8-62ea-4d2a-b987-47c14932d730`;
branch `feat/card-task-859094b8`, worktree `/work/worktrees/task-859094b8`.
Task base: `0def408517a4541238f54becf28d375cc313cf23`.
The [inert-observation plan](../superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md)
and [repair manifest](2026-10-04-card-0959-path-and-dispatch-repair.md) retain their IDs.
Full task report, final-SHA receipts and V/R matrix: `.antiphon/task-859094b8.md`.

The inherited warm-reuse test failed at task base because its three-minute-idle
agent was still within the default five-minute reservation without a matching
root. It fell through to cold launch, where the existing Worktree requirement
refused its already-queued Shared task. Commit
`4ed4a0c0b314aeb1f6199e082fc0462343fee8fb` sets
`PoolReservedForRootTaskId = taskId` in the fixture. No production code, setting,
timeout or assertion changed. Full CP-10 then passed 8/8. The test still requires
persisted claim, exact warm session, reuse event, queued brief and zero cold
launches; it does not claim complete UserPrompt receipt. The earlier CP-9
original-defect red remains recorded in the repair note; PCs remain pending.

Admission: both manifests imported (8 original / 5 repair rows). A read-only
Test/Arguments recount at task base and current master
`f7132d32976ae1d7d8bca5df9442c7c004f2667b` confirms the named class floors;
projection is 8 here / 7 on master, and the checkpoint census literals are
365 / 377. Runner defaults, runners, pipeline and both named concurrent Code
owners were inspected. No shared cleanup, placement, capability or helper file
was edited. The only unlisted build was the authorized slot-gated importer
bootstrap with `UseAppHost=false`; it passed with one existing CS8602 warning.

CP-11 passed 1/1, CP-5 passed 22/22, and post-repair CP-4 passed 8/8 at the
fixture-fix SHA. Their exact-SHA receipt validation passed, as did CP-10's.
CP-8 ran once, uninterrupted: **3942 passed, 0 failed, 52 skipped**. Fresh TRX
comparison found exactly the 33 recorded Windows exclusions plus 19 jq cases
(12 CARD-0973 and 7 CARD-0912); no unexpected skip. The unchanged 3961 floor
makes CP-8 **exit 3**; strict receipt validation is **exit 2, row_failed**.
These exclusions and the validator limitation are accepted by this continuation's
brief. This is not a strict-green Unit certificate. Unit includes R-3 76/76,
R-2 directory/retirement 16/16, and the 365-case census guard 1/1. The four
RunnerCatalogue integration cases retain earlier CP-6 evidence, not a new run.

Every run was serial, source-frozen, `slot=granted waited=0s`, with
`dirty=0 sourceState=clean buildSource=verified`. The standing Code contract's
checkpoint tool was used with imported plans and terminal waits; the brief's
direct-script form was not used. No full assembly, loaded repetition or deliberate
mutant was run. Only the failed CP-10 selection was rerun after its fixture changed.
CP-1 and CP-3 run after this note's commit so their exact tested SHA is the final
handoff SHA; their fresh receipts are retained in the final task report.

Native CP-2 (V-6, V-7, V-8, R-4; 29 required) remains caller-commissioned at that
final SHA, followed by delta Review. V-13 descriptor/transport variants,
V-21..V-26 delivery/recovery variants and manual qualification remain deferred to
CARD-1029 by the brief, not passed here. V-15, V-16, V-17, V-18 and V-20 are retired.
V-27/R-6 passed CP-10; R-7 passed CP-11; R-1 passed CP-5. No ordinary pass discharges
a PC: all **193** IDs and every variant remain pending SourceLanding Mutation:
PC-1..14,16..84,89..109,111..120,122..127,153..159,184..188,190..192,194,
199..203,205..256.

The pinned current-master evidence guard is absent from this branch and was
materialized into ignored `.antiphon/c959-history-guard/scripts/` with its policy
dependency. Full continuation base..HEAD has zero evidence violations. The full
CARD-0959 history from merge-base `edb96aecd93cbe90fe8887c1a8d1523527bd49d9`
retains **26 inherited non-Markdown evidence violations**; no new archive or
payload is committed. Final guard counts and owned-output cleanup are recorded
in the task report. Restart: **server**, caller owns activation after Review/land
for the inherited dispatcher repair; none performed in this task.

## Unedited checkpoint receipts

Raw payloads remain ignored under the named `.antiphon/checkpoints/` directories.

```text
CHECKPOINT CP-4 commit=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb build=ok filter=/*/*/CodexCliObservationTests*/C959_* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-859094b8/.antiphon/checkpoints/20261004-024655-5603/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=0def408517a4541238f54becf28d375cc313cf23 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/* executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-859094b8/.antiphon/checkpoints/20261004-022723-ccb5/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=0def408517a4541238f54becf28d375cc313cf23 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb build=ok filter=/*/*/CodexProviderAuthRoutingTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-859094b8/.antiphon/checkpoints/20261004-023548-2e75/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb build=ok filter=/*/*/(CodexPhoneHomeCreateTests*)|(PinnedCodexProfileDispatchLaunchTests*)|(ModelAvailabilityCreateTests*)|(ModelAvailabilityDispatcherTests*)/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-859094b8/.antiphon/checkpoints/20261004-023548-2e75/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb build=reused filter=/*/*/*/*[Category=Unit] executed=3942 passed=3942 failed=0 skipped=52 trx=/work/worktrees/task-859094b8/.antiphon/checkpoints/20261004-023548-2e75/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-859094b8/.antiphon/checkpoints/20261004-023132-099a/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=4ed4a0c0b314aeb1f6199e082fc0462343fee8fb sourceState=clean buildSource=verified
```
