# CARD-1474 closing-report nudge after a valid report

Code task `5196a2aa`, branch `feat/card-task-5196a2aa`, base `9cf77dbadfc6ab368cc9eb0ab188c82eb001b60a`.
Evidence: `docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md` (Debug `fc28850a`, task `ca843435`).

## Step 1 findings (read-only)

- **Producer.** `AgentTaskReplyService.NudgeForClosingLineAsync` records the Warning event,
  `ReportNudgedAt`/`ReportNudgedSequence`, enqueues the child ask WhenIdle (Delegation origin) and
  stamps its id into `ReportNudgeMessageId` through an isolated save (`StampNudgeRowAsync`). It
  then queues the parent waiting note with ConversationKey `task-wait:<task:N>` and
  `SourceTaskId = task.Id` (`EnqueueParentWaitNoteAsync`).
- **Cancel conditions today.** Only `BlockUnmarkedWaitingAsync` cancels `ReportNudgeMessageId`
  (by `CancelAsync`, Pending-only). Requeue clears the id (`AgentTaskService`). `SettleAsync`,
  the path every settled report takes (marked, token-only, question, settle-anyway), cancels
  neither the child ask nor the parent waiting note.
- **Delivery path.** `AgentSessionRuntime.FlushQueueOnIdleAsync` runs task settlement before
  `SessionMessageQueueService.OnTurnEndAsync`, which flushes the queue. So at the report's turn
  end the task settles, nothing retires the ask, and the very next flush types it into the now
  settled session (ca843435: Completed 07:19:33.048Z, nudge UserPrompt 07:19:33.136Z, second
  report at 07:19:44Z). The parent waiting note is likewise still Pending and is typed with or
  before the completion note.
- **Race discipline.** The flush stamps Sent and charges `DeliveryAttempts` under the per-session
  queue lock before a byte is typed; `CancelPendingIfUntypedAsync` re-checks
  `Pending && DeliveryAttempts == 0` under the same lock. A canceled row is skipped by the flush; a
  claimed row is refused by the cancel.

## Fix (S2)

`SettleAsync`'s `afterPersist` step (after the settlement commits, before the parent is notified
and before the turn-end flush) calls `RetireObsoleteClosingNudgeAsync`:

1. Child ask: `CancelPendingIfUntypedAsync(task.AgentSessionId, task.ReportNudgeMessageId)` —
   bound to the task's recorded queue id and current session; Sent, attempted (ambiguous) and
   other-session rows are left alone.
2. Parent waiting note: every row on `task.ParentSessionId` with ConversationKey
   `task-wait:<task:N>`, `SourceTaskId == task.Id`, Pending and never typed, canceled through the
   same primitive.

`ReportNudgeMessageId` and the Warning event are preserved. Failures are logged, never fail the
settlement. No pool kill is relied on. CARD-1473 (sibling `end_turn` trigger) is untouched.

Residual: terminal paths that do not go through `SettleAsync` (cancel, process-exit failure) are not
changed in this slice; `BlockUnmarkedWaitingAsync` already cancels its own ask.

## Tests

`tests/Antiphon.Tests/Application/ClosingReportNudgeLifecycleTests.cs`, five nonparameterized
cases through the real queue and reply paths (fixture helpers shared from
`AgentTaskReplyIntegrationTests`, widened to `internal`):

| Id | Method | Role |
|---|---|---|
| V-1 | `Valid_report_cancels_unsent_child_and_parent_wait_notes` | red witness: nudge row still Pending after settlement (assertion), and typed on flush |
| R-1 | `Sent_or_attempted_nudge_is_not_retyped_or_deleted` | guard: attempted child row and Sent parent note untouched |
| R-2 | `Unrelated_pending_input_survives` | guard: human rows, a sibling's input and a sibling's wait note stay Pending |
| R-3 | `Delivery_claim_racing_report_keeps_single_input_ownership` | guard: flush held mid-write by a barrier, settlement races; typed once, never Canceled; no sleeps |
| R-4 | `Old_attempt_nudge_cannot_cancel_current_attempt_input` | guard: stale id on another session cannot cancel current-attempt input or the other session's row |

PC-1 (pending for SourceLanding Mutation): remove the `RetireObsoleteClosingNudgeAsync` call → V-1
red. PC-2: replace `CancelPendingIfUntypedAsync` with an unconditional status write → R-1/R-3 red.
PC-3: drop the session/key/SourceTaskId binding → R-2/R-4 red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-report-nudge-red/` | report-nudge-red | `/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/*` | V-1 red witness, R-1..R-4 | 5 executed, V-1 assertion-red, 4 pass, 0 skipped | 5 | 6 |
| CP-2 | S2-fix | `tests/Antiphon.Tests -> bin-report-nudge-green/` | report-nudge-green | `/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/*` | V-1, R-1..R-4 | 5 passed, 0 failed/skipped | 5 | 6 |

Closed list. Checkpoint tool on server2 per committed slice; no Unit lane, assembly, desktop or
full-suite run (brief: minimal testing). CP-1 is expected red on V-1 only and is reported as the
red witness, not as a failure. Alternate outputs are removed after each run.
