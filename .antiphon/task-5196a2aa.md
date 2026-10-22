# Task 5196a2aa report - CARD-1474 closing-report nudge after a valid report

Branch `feat/card-task-5196a2aa`, worktree `/work/worktrees/task-5196a2aa` (server2 runner mirror), base
`9cf77dbadfc6ab368cc9eb0ab188c82eb001b60a`. Landing owner: this Code task `5196a2aa`.
Plan: `docs/superpowers/plans/2026-10-22-card-1474-nudge-after-report-plan.md` (Step 1 findings, fix, PCs, closed `### Checkpoints`).

## Change

- `server/Application/Services/AgentTaskReplyService.cs`: new `RetireObsoleteClosingNudgeAsync`, called first in
  `SettleAsync`'s `afterPersist` (after the settlement commits; before the parent completion note and before the
  runtime's turn-end queue flush). Cancels the task's recorded `ReportNudgeMessageId` on its own session, and every
  parent row with ConversationKey `task-wait:<task:N>` + `SourceTaskId == task.Id`, only through
  `CancelPendingIfUntypedAsync` (Pending and `DeliveryAttempts == 0`, re-checked under the queue's per-session lock).
  Nudge id and Warning event preserved; failures logged, never undo the settlement. CARD-1473 untouched.
- Tests: new `tests/Antiphon.Tests/Application/ClosingReportNudgeLifecycleTests.cs` (5 cases). Fixture helpers in
  `AgentTaskReplyIntegrationTests.cs` widened from `private` to `internal` (no behaviour change);
  `NewDeliveryFactory` gained an optional `LandDeliveryBoundary` pass-through for the race barrier.

## Commits

- `0dd0f85518d286038c61b73a887bd4c82a89ed6f` test + plan (red slice; CP-1 first run failed to compile: missing using)
- `57f78333cc0ff0794646e59512ba8cad5b44c4e4` test using fix (red slice tested)
- `060c15d01b73f252c2fa13693b1c7a42db92186c` production fix
- this report commit; CP-2 runs on the report commit as the tested tip.

## Checkpoints

CP-1 (red witness, run `20261022-140610-96ff`; first attempt `20261022-135703-193d` was build=failed on CS0103 at 0dd0f855, not counted):

```
CHECKPOINT CP-1 commit=57f78333cc0ff0794646e59512ba8cad5b44c4e4 build=ok filter=/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/* executed=5 passed=4 failed=1 skipped=0 trx=/work/worktrees/task-5196a2aa/.antiphon/checkpoints/20261022-140610-96ff/rows/CP-1/run.trx slot=granted waited=180s dirty=0 source=57f78333cc0ff0794646e59512ba8cad5b44c4e4 sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.ClosingReportNudgeLifecycleTests.Valid_report_cancels_unsent_child_and_parent_wait_notes (CP-1) ShouldAssertException: nudge.Status     should be QueuedMessageStatus.Canceled     but was QueuedMessageStatus.Pending  Additional Info:     a valid report makes the never-typed closing-line ask obsolete
```

Expected red: V-1 assertion-red on the pending nudge, R-1..R-4 green. CP-2 lines are in the final settlement message.

## V/R outcomes

V-1 red at 57f78333 (as designed); R-1..R-4 green at 57f78333. CP-2 (all five on the fix) - see final message.

## Landability

- `git merge-tree --write-tree origin/master HEAD` clean at origin/master `e70a567107f56ffa35a9baa861f208d3ca5eea77`.
- rc: per-commit chained `merge-tree --merge-base <c>^` of 0dd0f855, 57f78333, 060c15d0 onto
  `origin/rc/2026-10-20` `ceb6e3d6b78471db6831258699c493b8eb2d619b`: all exit 0. The production hunk (060c15d0) picks
  cleanly alone; the plan doc is a new file and also applies (rc carries no plans dir; drop it from an rc pick if preferred).
- `scripts/check-evidence-diff.ps1 -BaseRef 9cf77dba -HeadRef 060c15d0`: commits=3 entries=0 violations=0.

## Pending for Mutation

PC-1 remove the `RetireObsoleteClosingNudgeAsync` call -> V-1 red. PC-2 replace the conditional cancel with an
unconditional status write -> R-1/R-3 red. PC-3 drop the session/key/SourceTaskId binding -> R-2/R-4 red. All pending.

## Residual

Terminal paths that bypass `SettleAsync` (task cancel, process-exit failure) are unchanged; `BlockUnmarkedWaitingAsync`
already cancels its own ask. CARD-1473 (phantom sibling boundary trigger) remains separate.
